using Dapper;
using Northwoods.Contracts;
using Northwoods.Tenancy;

namespace Northwoods.Api;

internal static class SearchEndpoints
{
    private const double SearchFuzzySimilarityThreshold = 0.3;
    private const double CaseAggregateSimilarityThreshold = 0.6;

    public static WebApplication MapSearchEndpoints(this WebApplication app)
    {
        app.MapGet("/search", async (HttpContext httpContext, DbConnectionFactory dbFactory, string? q) =>
        {
            var authContext = ApiHelpers.GetAuthContext(httpContext.User);
            if (authContext is null)
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(q))
                return Results.Ok(new SearchResponse("", []));

            await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

            var items = (await session.Connection.QueryAsync<SearchResultItem>(
                @"
                SELECT
                    cp.document_id AS IntakeId,
                    cp.template_id AS TemplateId,
                    COALESCE(cp.applicant_name, '(unknown)') AS ApplicantName,
                    d.status AS Status,
                    COALESCE(
                        (SELECT AVG(ef.confidence) FROM extracted_fields ef WHERE ef.document_id = cp.document_id AND ef.tenant_id = cp.tenant_id),
                        0
                    )::decimal AS Confidence,
                    ts_headline('simple', cp.search_text, websearch_to_tsquery('simple', @Query),
                        'StartSel=**, StopSel=**, MaxWords=30, MinWords=15') AS Snippet
                FROM case_profiles cp
                JOIN documents d ON d.id = cp.document_id AND d.tenant_id = cp.tenant_id
                WHERE cp.tenant_id = @TenantId
                  AND (cp.search_tsv @@ websearch_to_tsquery('simple', @Query)
                   OR similarity(lower(COALESCE(cp.applicant_name, '')), lower(@Query)) > @SearchThreshold)
                ORDER BY
                    ts_rank_cd(cp.search_tsv, websearch_to_tsquery('simple', @Query)) DESC,
                    similarity(lower(COALESCE(cp.applicant_name, '')), lower(@Query)) DESC
                LIMIT 50
                ",
                new { Query = q, TenantId = authContext.TenantId, SearchThreshold = SearchFuzzySimilarityThreshold },
                session.Transaction)).ToList();

            return Results.Ok(new SearchResponse(q, items));
        })
            .WithName("Search").WithTags("Search")
            .WithSummary("Full-text and fuzzy search across processed intakes within the active tenant.")
            .RequireAuthorization();

        app.MapGet("/cases/{personKey}", async (string personKey, HttpContext httpContext, DbConnectionFactory dbFactory) =>
        {
            var authContext = ApiHelpers.GetAuthContext(httpContext.User);
            if (authContext is null)
                return Results.Unauthorized();

            var decodedKey = Uri.UnescapeDataString(personKey);

            await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

            var docs = (await session.Connection.QueryAsync<(Guid id, string template_id, string status, DateTimeOffset created_at)>(
                @"
                SELECT d.id, d.template_id, d.status, d.created_at
                FROM documents d
                JOIN case_profiles cp ON cp.document_id = d.id AND cp.tenant_id = d.tenant_id
                WHERE cp.tenant_id = @TenantId
                  AND (
                    lower(cp.applicant_name) = lower(@PersonKey)
                    OR similarity(lower(COALESCE(cp.applicant_name, '')), lower(@PersonKey)) > @CaseThreshold
                  )
                ORDER BY d.created_at DESC
                ",
                new { TenantId = authContext.TenantId, PersonKey = decodedKey, CaseThreshold = CaseAggregateSimilarityThreshold },
                session.Transaction)).ToList();

            var docIds = docs.Select(d => d.id).ToArray();
            var allFields = (await session.Connection.QueryAsync<(Guid document_id, string FieldKey, string Value, decimal Confidence, bool RequiresReview)>(
                @"SELECT document_id,
                       field_key AS FieldKey,
                       COALESCE(corrected_value, extracted_value) AS Value,
                       confidence AS Confidence,
                       requires_review AS RequiresReview
                FROM extracted_fields
                WHERE document_id = ANY(@DocIds) AND tenant_id = @TenantId",
                new { DocIds = docIds, TenantId = authContext.TenantId },
                session.Transaction)).ToLookup(f => f.document_id);

            var caseDocuments = new List<CaseDocumentItem>(docs.Count);
            foreach (var doc in docs)
            {
                var fields = allFields[doc.id]
                    .Select(f => new ConfidenceField(f.FieldKey, f.Value, f.Confidence, f.RequiresReview))
                    .ToList();
                caseDocuments.Add(new CaseDocumentItem(doc.id, doc.template_id, doc.status, doc.created_at, fields));
            }

            return Results.Ok(new CaseAggregateResponse(decodedKey, caseDocuments));
        })
            .WithName("GetCaseAggregate").WithTags("Cases")
            .WithSummary("Aggregates all documents for a person/case identified by applicant name within the active tenant.")
            .RequireAuthorization();

        return app;
    }
}
