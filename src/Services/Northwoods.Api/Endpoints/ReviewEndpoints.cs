using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dapper;
using Northwoods.Contracts;
using Northwoods.Tenancy;
using Npgsql;

namespace Northwoods.Api;

internal static class ReviewEndpoints
{
    private const int CaseEmbeddingDimensions = 1536;

    public static WebApplication MapReviewEndpoints(
        this WebApplication app,
        HttpClient embeddingHttpClient,
        string? openAiApiKey,
        bool useAiSummaries,
        ApiObservability observability)
    {
        app.MapGet("/review-queue", async (HttpContext httpContext, DbConnectionFactory dbFactory) =>
        {
            var authContext = ApiHelpers.GetAuthContext(httpContext.User);
            if (authContext is null)
                return Results.Unauthorized();

            if (authContext.Role != UserRole.Reviewer)
                return Results.Forbid();

            await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

            var items = (await session.Connection.QueryAsync<ReviewQueueItem>(
                """
                SELECT d.id AS ReviewId, d.id AS IntakeId,
                       COALESCE((SELECT ef.extracted_value FROM extracted_fields ef WHERE ef.document_id = d.id AND ef.tenant_id = d.tenant_id AND ef.field_key = 'applicantName' LIMIT 1), '(unknown)') AS ApplicantName,
                       d.template_id AS TemplateId,
                       (SELECT COUNT(*)::int FROM extracted_fields ef WHERE ef.document_id = d.id AND ef.tenant_id = d.tenant_id AND ef.requires_review) AS UncertainFieldCount,
                       d.created_at AS UploadDate
                FROM documents d
                WHERE d.tenant_id = @TenantId
                ORDER BY d.created_at
                """,
                new { TenantId = authContext.TenantId },
                transaction: session.Transaction)).ToList();
            return Results.Ok(items);
        })
            .WithName("GetReviewQueue").WithTags("Reviews")
            .WithSummary("Returns documents awaiting review for the active tenant.")
            .RequireAuthorization();

        app.MapGet("/reviews/{id:guid}", async (Guid id, HttpContext httpContext, DbConnectionFactory dbFactory) =>
        {
            var authContext = ApiHelpers.GetAuthContext(httpContext.User);
            if (authContext is null)
                return Results.Unauthorized();

            if (authContext.Role != UserRole.Reviewer)
                return Results.Forbid();

            await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

            var doc = await session.Connection.QueryFirstOrDefaultAsync<(Guid id, string tenant_id, string template_id, string status, string original_file_key)>(
                "SELECT id, tenant_id, template_id, status, original_file_key FROM documents WHERE id = @Id AND tenant_id = @TenantId",
                new { Id = id, TenantId = authContext.TenantId },
                session.Transaction);

            if (doc == default) return Results.NotFound();

            var fields = (await session.Connection.QueryAsync<ConfidenceField>(
                """
                SELECT field_key AS FieldKey,
                       COALESCE(corrected_value, extracted_value) AS Value,
                       confidence AS Confidence,
                       requires_review AS RequiresReview
                FROM extracted_fields
                WHERE document_id = @Id AND tenant_id = @TenantId
                """,
                new { Id = id, TenantId = authContext.TenantId },
                session.Transaction)).ToList();

            var attemptRows = (await session.Connection.QueryAsync<(string field_key, string provider, string raw_value, decimal raw_confidence)>(
                """
                SELECT field_key, provider, COALESCE(normalized_value, raw_value) AS raw_value, COALESCE(normalized_confidence, raw_confidence) AS raw_confidence
                FROM extraction_attempts
                WHERE document_id = @Id AND tenant_id = @TenantId
                ORDER BY field_key, provider
                """,
                new { Id = id, TenantId = authContext.TenantId },
                session.Transaction)).ToList();

            var attemptsByField = attemptRows
                .GroupBy(a => a.field_key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var auditRows = (await session.Connection.QueryAsync<(string event_type, DateTimeOffset created_at, Guid? actor_id)>(
                "SELECT event_type, created_at, actor_id FROM audit_events WHERE document_id = @Id AND tenant_id = @TenantId ORDER BY created_at",
                new { Id = id, TenantId = authContext.TenantId },
                session.Transaction)).ToList();

            // Resolve distinct actor IDs to emails
            var actorIds = auditRows
                .Where(r => r.actor_id.HasValue)
                .Select(r => r.actor_id!.Value)
                .Distinct()
                .ToArray();

            var actorEmails = new Dictionary<Guid, string>();
            if (actorIds.Length > 0)
            {
                var emailRows = await session.Connection.QueryAsync<(Guid id, string email)>(
                    "SELECT id, email FROM users WHERE id = ANY(@Ids)",
                    new { Ids = actorIds },
                    session.Transaction);
                foreach (var (uid, email) in emailRows)
                    actorEmails[uid] = email;
            }

            var auditEvents = auditRows
                .Select(r => new AuditEventItem(
                    r.event_type,
                    r.created_at,
                    r.actor_id.HasValue && actorEmails.TryGetValue(r.actor_id.Value, out var email) ? email : null))
                .ToList();

            // Extract failure reason from the most recent extraction_failed audit event
            string? failureReason = null;
            if (doc.status == "failed")
            {
                var failedDetailsJson = await session.Connection.QueryFirstOrDefaultAsync<string>(
                    "SELECT details::text FROM audit_events WHERE document_id = @Id AND tenant_id = @TenantId AND event_type = 'extraction_failed' ORDER BY created_at DESC LIMIT 1",
                    new { Id = id, TenantId = authContext.TenantId },
                    session.Transaction);

                if (!string.IsNullOrEmpty(failedDetailsJson))
                {
                    try
                    {
                        using var detailsDoc = JsonDocument.Parse(failedDetailsJson);
                        if (detailsDoc.RootElement.TryGetProperty("error", out var errEl))
                            failureReason = errEl.GetString();
                    }
                    catch (JsonException)
                    {
                        // ignore malformed details
                    }
                }
            }

            var similarCases = await FindSimilarCasesAsync(
                session.Connection, session.Transaction, id, authContext.TenantId, doc.template_id, fields, 5,
                embeddingHttpClient, openAiApiKey, useAiSummaries, app.Logger, httpContext.RequestAborted);

            var reviewFields = fields.Select(f =>
            {
                var attempts = attemptsByField.TryGetValue(f.FieldKey, out var rows)
                    ? rows.Select(r => new FieldAttempt(r.provider, r.raw_value, r.raw_confidence)).ToList()
                    : new List<FieldAttempt>();

                var distinctProviders = attempts.Select(a => a.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var consensusNote = distinctProviders.Count switch
                {
                    0 => "No extraction data available",
                    1 => "Single provider extraction",
                    _ => AllProvidersAgree(attempts) ? "All providers agree" : "Providers disagree on value",
                };

                return new ReviewField(f.FieldKey, f.Value, f.Confidence, f.RequiresReview, attempts, consensusNote);
            }).ToList();

            var sourceUrl = $"/documents/{doc.id}/source";
            var status = ApiHelpers.ParseStatus(doc.status);

            return Results.Ok(new ReviewDetailResponse(doc.id, doc.id, doc.tenant_id, doc.template_id, sourceUrl, status, reviewFields, similarCases, auditEvents, failureReason));
        })
            .WithName("GetReview").WithTags("Reviews")
            .WithSummary("Returns the document, extracted fields, and audit trail for a review task.")
            .RequireAuthorization();

        app.MapPost("/reviews/{id:guid}/finalize", async (Guid id, FinalizeReviewRequest request, HttpContext httpContext, DbConnectionFactory dbFactory) =>
        {
            var authContext = ApiHelpers.GetAuthContext(httpContext.User);
            if (authContext is null)
                return Results.Unauthorized();

            if (authContext.Role != UserRole.Reviewer)
                return Results.Forbid();

            var correlationId = ApiHelpers.GetCorrelationId(httpContext);

            await using var session = await dbFactory.OpenSessionAsync(authContext.TenantId);

            var doc = await session.Connection.QueryFirstOrDefaultAsync<(Guid id, string template_id, string status)>(
                "SELECT id, template_id, status FROM documents WHERE id = @Id AND tenant_id = @TenantId",
                new { Id = id, TenantId = authContext.TenantId },
                session.Transaction);

            if (doc == default) return Results.NotFound();
            if (doc.status != "review_ready" && doc.status != "completed")
                return Results.BadRequest(new { error = $"Document is in '{doc.status}' state, not review_ready or completed." });

            var currentFields = (await session.Connection.QueryAsync<(string field_key, string extracted_value, string? corrected_value)>(
                "SELECT field_key, extracted_value, corrected_value FROM extracted_fields WHERE document_id = @DocId AND tenant_id = @TenantId",
                new { DocId = id, TenantId = authContext.TenantId },
                session.Transaction)).ToDictionary(f => f.field_key);

            foreach (var field in request.Fields)
            {
                if (currentFields.TryGetValue(field.FieldKey, out var current))
                {
                    var oldValue = current.corrected_value ?? current.extracted_value;
                    if (!string.Equals(oldValue, field.Value, StringComparison.Ordinal))
                    {
                        await session.Connection.ExecuteAsync(
                            """
                            INSERT INTO audit_events (document_id, tenant_id, event_type, actor_id, details)
                            VALUES (@DocId, @TenantId, 'field_corrected', @ActorId, @Details::jsonb)
                            """,
                            new
                            {
                                DocId = id,
                                TenantId = authContext.TenantId,
                                ActorId = authContext.UserId,
                                Details = JsonSerializer.Serialize(new
                                {
                                    field_key = field.FieldKey,
                                    old_value = oldValue,
                                    new_value = field.Value,
                                    correlation_id = correlationId
                                })
                            },
                            session.Transaction);
                    }
                }

                await session.Connection.ExecuteAsync(
                    """
                    UPDATE extracted_fields
                    SET corrected_value = @Value, requires_review = false, updated_at = now()
                    WHERE document_id = @DocId AND tenant_id = @TenantId AND field_key = @FieldKey
                    """,
                    new { DocId = id, TenantId = authContext.TenantId, field.FieldKey, field.Value },
                    session.Transaction);
            }

            await session.Connection.ExecuteAsync(
                "UPDATE documents SET status = 'finalized', updated_at = now() WHERE id = @Id AND tenant_id = @TenantId",
                new { Id = id, TenantId = authContext.TenantId },
                session.Transaction);

            await session.Connection.ExecuteAsync(
                """
                INSERT INTO audit_events (document_id, tenant_id, event_type, actor_id, details)
                VALUES (@DocId, @TenantId, 'finalized', @ActorId, @Details::jsonb)
                """,
                new
                {
                    DocId = id,
                    TenantId = authContext.TenantId,
                    ActorId = authContext.UserId,
                    Details = JsonSerializer.Serialize(new
                    {
                        correlation_id = correlationId,
                        note = request.ReviewerNote
                    })
                },
                session.Transaction);

            if (await SupportsCaseProfiles(session.Connection, session.Transaction))
            {
                var ocrSegments = (await session.Connection.QueryAsync<(string field_key, string raw_value)>(
                    "SELECT field_key, raw_value FROM extraction_attempts WHERE document_id = @DocId AND tenant_id = @TenantId AND stage = 'ocr'",
                    new { DocId = id, TenantId = authContext.TenantId },
                    session.Transaction)).ToList();

                var searchText = CaseProfileText.BuildFinalized(
                    doc.template_id,
                    request.Fields,
                    ocrSegments.Select(o => (o.field_key, o.raw_value)),
                    request.ReviewerNote);

                string? embeddingLiteral = null;
                var apiKey = openAiApiKey;
                if (!string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(searchText))
                {
                    try
                    {
                        var (embedding, promptTokens, totalTokens) = await GenerateCaseEmbeddingAsync(embeddingHttpClient, searchText, apiKey, httpContext.RequestAborted);
                        embeddingLiteral = ToPgVectorLiteral(embedding);

                        await session.Connection.ExecuteAsync(
                            """
                            INSERT INTO audit_events (document_id, tenant_id, event_type, actor_id, details)
                            VALUES (@DocId, @TenantId, 'embedding_regenerated', @ActorId, @Details::jsonb)
                            """,
                            new
                            {
                                DocId = id,
                                TenantId = authContext.TenantId,
                                ActorId = authContext.UserId,
                                Details = JsonSerializer.Serialize(new
                                {
                                    model = "text-embedding-3-small",
                                    dimensions = CaseEmbeddingDimensions,
                                    prompt_tokens = promptTokens,
                                    total_tokens = totalTokens,
                                    trigger = "finalization"
                                })
                            },
                            session.Transaction);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        app.Logger.LogWarning(ex, "Failed to regenerate embedding for {DocId} during finalization; updating search_text only", id);
                    }
                }

                await session.Connection.ExecuteAsync(
                    $"""
                    UPDATE case_profiles
                    SET search_text = @SearchText,
                        embedding = CASE WHEN @Embedding IS NULL THEN embedding ELSE CAST(@Embedding AS vector({CaseEmbeddingDimensions})) END,
                        updated_at = now()
                    WHERE document_id = @DocId AND tenant_id = @TenantId
                    """,
                    new
                    {
                        DocId = id,
                        TenantId = authContext.TenantId,
                        SearchText = searchText,
                        Embedding = embeddingLiteral
                    },
                    session.Transaction);
            }

            await session.CommitAsync();
            observability.IncrementReviewFinalizationCount();
            return Results.Ok(new FinalizeReviewResponse(id, ProcessingStatus.Finalized));
        })
            .WithName("FinalizeReview").WithTags("Reviews")
            .WithSummary("Persists reviewer corrections and finalizes the intake record.")
            .RequireAuthorization();

        return app;
    }

    // Intentionally uses simpler normalization (trim + upper) than the worker's FieldConsensus.Normalize.
    // This is for display-only consensus labels, not extraction decisions.
    private static bool AllProvidersAgree(IReadOnlyList<FieldAttempt> attempts)
    {
        if (attempts.Count <= 1) return true;
        var values = attempts
            .Where(a => !string.IsNullOrWhiteSpace(a.Value))
            .Select(a => a.Value.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return values.Count <= 1;
    }

    private static async Task<IReadOnlyList<SimilarCaseItem>> FindSimilarCasesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid sourceDocumentId,
        string tenantId,
        string sourceTemplateId,
        IReadOnlyList<ConfidenceField> sourceFields,
        int limit,
        HttpClient httpClient,
        string? apiKey,
        bool useAiSummaries,
        ILogger logger,
        CancellationToken ct)
    {
        if (!await SupportsCaseProfiles(connection, transaction))
        {
            return [];
        }

        var targetApplicant = sourceFields.FirstOrDefault(f => string.Equals(f.FieldKey, "applicantName", StringComparison.OrdinalIgnoreCase))?.Value;
        var targetDob = sourceFields.FirstOrDefault(f => string.Equals(f.FieldKey, "dateOfBirth", StringComparison.OrdinalIgnoreCase))?.Value;
        var targetAddress = sourceFields.FirstOrDefault(f => string.Equals(f.FieldKey, "address", StringComparison.OrdinalIgnoreCase))?.Value;

        var sourceExists = await connection.ExecuteScalarAsync<int>(
            "SELECT EXISTS(SELECT 1 FROM case_profiles WHERE document_id = @Id AND tenant_id = @TenantId)::int",
            new { Id = sourceDocumentId, TenantId = tenantId },
            transaction);
        if (sourceExists == 0)
        {
            return [];
        }

        var ranked = (await connection.QueryAsync<SimilarCaseCandidate>(
            @"WITH target AS (
                SELECT tenant_id, template_id, search_tsv, search_text, embedding, applicant_name, date_of_birth, address
                FROM case_profiles
                WHERE document_id = @Id AND tenant_id = @TenantId
                LIMIT 1
            ),
            fts AS (
                SELECT cp.document_id, row_number() OVER (
                    ORDER BY ts_rank_cd(cp.search_tsv, websearch_to_tsquery('simple', COALESCE(t.search_text, '')))
                        DESC,
                        cp.document_id
                ) AS rank
                FROM case_profiles cp
                CROSS JOIN target t
                WHERE cp.tenant_id = t.tenant_id
                  AND cp.document_id <> @Id
                  AND cp.search_tsv @@ websearch_to_tsquery('simple', COALESCE(t.search_text, ''))
            ),
            vector AS (
                SELECT cp.document_id, row_number() OVER (
                    ORDER BY (cp.embedding <=> t.embedding) ASC,
                             cp.document_id
                ) AS rank
                FROM case_profiles cp
                CROSS JOIN target t
                WHERE cp.tenant_id = t.tenant_id
                  AND cp.document_id <> @Id
                  AND cp.embedding IS NOT NULL
                  AND t.embedding IS NOT NULL
            ),
            name_fuzzy AS (
                SELECT cp.document_id, row_number() OVER (
                    ORDER BY similarity(lower(cp.applicant_name), lower(t.applicant_name)) DESC,
                             cp.document_id
                ) AS rank
                FROM case_profiles cp
                CROSS JOIN target t
                WHERE cp.tenant_id = t.tenant_id
                  AND cp.document_id <> @Id
                  AND cp.applicant_name IS NOT NULL
                  AND t.applicant_name IS NOT NULL
                  AND similarity(lower(cp.applicant_name), lower(t.applicant_name)) > 0.25
            ),
            address_fuzzy AS (
                SELECT cp.document_id, row_number() OVER (
                    ORDER BY similarity(lower(cp.address), lower(t.address)) DESC,
                             cp.document_id
                ) AS rank
                FROM case_profiles cp
                CROSS JOIN target t
                WHERE cp.tenant_id = t.tenant_id
                  AND cp.document_id <> @Id
                  AND cp.address IS NOT NULL
                  AND t.address IS NOT NULL
                  AND similarity(lower(cp.address), lower(t.address)) > 0.2
            ),
            dob_exact AS (
                SELECT cp.document_id, 1 AS rank
                FROM case_profiles cp
                CROSS JOIN target t
                WHERE cp.tenant_id = t.tenant_id
                  AND cp.document_id <> @Id
                  AND cp.date_of_birth IS NOT NULL
                  AND t.date_of_birth IS NOT NULL
                  AND cp.date_of_birth = t.date_of_birth
            ),
            fused AS (
                SELECT document_id,
                       SUM(1.0 / (60.0 + rank)) AS match_score,
                       array_agg(DISTINCT source) AS match_sources
                FROM (
                    SELECT document_id, rank, 'fts'     AS source FROM fts
                    UNION ALL
                    SELECT document_id, rank, 'vector'  AS source FROM vector
                    UNION ALL
                    SELECT document_id, rank, 'name'    AS source FROM name_fuzzy
                    UNION ALL
                    SELECT document_id, rank, 'address' AS source FROM address_fuzzy
                    UNION ALL
                    SELECT document_id, rank, 'dob'     AS source FROM dob_exact
                ) x
                GROUP BY document_id
            )
            SELECT
                cp.document_id AS IntakeId,
                cp.template_id AS TemplateId,
                cp.applicant_name AS ApplicantName,
                cp.date_of_birth AS DateOfBirth,
                cp.address AS Address,
                ROUND(f.match_score::numeric, 4) AS MatchScore,
                f.match_sources AS MatchSources
            FROM fused f
            JOIN case_profiles cp ON cp.document_id = f.document_id
            ORDER BY f.match_score DESC, cp.applicant_name NULLS LAST
            LIMIT @Limit;
            ",
            new { Id = sourceDocumentId, TenantId = tenantId, Limit = limit },
            transaction)).ToList();

        if (ranked.Count == 0)
        {
            return [];
        }

        var candidateIds = ranked.Select(r => r.IntakeId).ToArray();
        var fieldsByCase = (await connection.QueryAsync<CaseFieldValue>(
            @"SELECT document_id AS IntakeId,
                   field_key AS FieldKey,
                   COALESCE(corrected_value, extracted_value) AS Value
            FROM extracted_fields
            WHERE document_id = ANY(@CandidateIds) AND tenant_id = @TenantId
            ",
            new { CandidateIds = candidateIds, TenantId = tenantId },
            transaction))
            .ToLookup(r => r.IntakeId, r => r)
            .ToDictionary(g => g.Key, g => g.ToDictionary(f => f.FieldKey, f => f.Value, StringComparer.OrdinalIgnoreCase));

        var sourceFieldDict = sourceFields.ToDictionary(f => f.FieldKey, f => f.Value, StringComparer.OrdinalIgnoreCase);
        var result = new List<SimilarCaseItem>(ranked.Count);
        foreach (var caseItem in ranked)
        {
            var candidateFields = fieldsByCase.GetValueOrDefault(caseItem.IntakeId, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            var (algorithmicSummary, signals) = BuildCaseSummary(caseItem, sourceTemplateId, targetApplicant, targetDob, targetAddress, candidateFields);

            var summary = algorithmicSummary;
            if (useAiSummaries && !string.IsNullOrWhiteSpace(apiKey))
            {
                try
                {
                    summary = await GenerateContextualSummaryAsync(
                        httpClient, apiKey, signals, sourceTemplateId, sourceFieldDict, caseItem, candidateFields, logger, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not TaskCanceledException)
                {
                    logger.LogWarning(ex, "AI summary generation failed for case {CaseId}, using algorithmic fallback", caseItem.IntakeId);
                }
            }

            result.Add(new SimilarCaseItem(caseItem.IntakeId, caseItem.IntakeId, caseItem.ApplicantName ?? "Unknown applicant", caseItem.TemplateId, caseItem.MatchScore, summary, signals));
        }

        return result;
    }

    private static (string Summary, IReadOnlyList<string> Signals) BuildCaseSummary(
        SimilarCaseCandidate candidate,
        string sourceTemplateId,
        string? sourceApplicant,
        string? sourceDob,
        string? sourceAddress,
        IReadOnlyDictionary<string, string> candidateFields)
    {
        var signals = new List<string>();
        var sources = candidate.MatchSources ?? [];

        // Field-level exact/fuzzy matches
        var candidateApplicant = candidate.ApplicantName;
        if (!string.IsNullOrWhiteSpace(candidateApplicant) && string.Equals(candidateApplicant, sourceApplicant, StringComparison.OrdinalIgnoreCase))
            signals.Add("same applicant");
        else if (sources.Contains("name"))
            signals.Add("similar name");

        var candidateDob = candidate.DateOfBirth;
        if (!string.IsNullOrWhiteSpace(candidateDob) && !string.IsNullOrWhiteSpace(sourceDob) && candidateDob == sourceDob)
            signals.Add("matching DOB");

        var candidateAddress = candidate.Address;
        if (!string.IsNullOrWhiteSpace(candidateAddress) && !string.IsNullOrWhiteSpace(sourceAddress) &&
            string.Equals(candidateAddress, sourceAddress, StringComparison.OrdinalIgnoreCase))
            signals.Add("matching address");
        else if (sources.Contains("address"))
            signals.Add("similar address");

        if (string.Equals(candidate.TemplateId, sourceTemplateId, StringComparison.OrdinalIgnoreCase))
            signals.Add("same template");

        // Query-level signals not captured by field comparison
        if (sources.Contains("fts"))
            signals.Add("text match");

        if (sources.Contains("vector"))
            signals.Add("semantic match");

        if (signals.Count == 0)
            signals.Add("cross-field lexical overlap");

        var requestedServices = candidateFields.TryGetValue("requestedServices", out var services) && !string.IsNullOrWhiteSpace(services)
            ? services
            : candidateFields.TryGetValue("notes", out var notes) && !string.IsNullOrWhiteSpace(notes)
                ? notes
                : "review case context available";

        var evidence = string.Join(", ", signals);
        var maxSnippet = requestedServices.Length > 120 ? requestedServices[..117] + "\u2026" : requestedServices;
        return ($"{evidence}; top hints: {maxSnippet}", signals);
    }

    private static async Task<string> GenerateContextualSummaryAsync(
        HttpClient httpClient,
        string apiKey,
        IReadOnlyList<string> algorithmicSignals,
        string sourceTemplateId,
        IReadOnlyDictionary<string, string> sourceFields,
        SimilarCaseCandidate matchedCase,
        IReadOnlyDictionary<string, string> matchedCaseFields,
        ILogger logger,
        CancellationToken ct)
    {
        static string Truncate(string v) => v.Length > 500 ? v[..497] + "..." : v;
        var sourceFieldsSummary = string.Join("; ", sourceFields.Select(kv => $"{kv.Key}: {Truncate(kv.Value)}"));
        var matchedFieldsSummary = string.Join("; ", matchedCaseFields.Select(kv => $"{kv.Key}: {Truncate(kv.Value)}"));
        var signalsSummary = string.Join(", ", algorithmicSignals);

        var systemPrompt = """
            You are a case reviewer assistant for a human-services intake system.
            Given a current case and a previously matched case, write a 1-2 sentence factual summary
            explaining the relationship between them. Reference specific shared fields, dates, statuses,
            and services. Do NOT speculate, recommend actions, or editorialize. Be concise and precise.
            """;

        var userPrompt = $"""
            Current case template: {sourceTemplateId}
            Current case fields: {sourceFieldsSummary}

            Matched case template: {matchedCase.TemplateId}
            Matched case applicant: {matchedCase.ApplicantName ?? "unknown"}
            Matched case DOB: {matchedCase.DateOfBirth ?? "unknown"}
            Matched case address: {matchedCase.Address ?? "unknown"}
            Matched case fields: {matchedFieldsSummary}
            Matched case score: {matchedCase.MatchScore}

            Algorithmic match signals: {signalsSummary}
            """;

        var requestPayload = new
        {
            model = "gpt-5.4-nano",
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            max_tokens = 200,
            temperature = 0.2
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var res = await httpClient.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            logger.LogWarning("OpenAI chat completion failed ({StatusCode}): {Body}", (int)res.StatusCode, body);
            throw new InvalidOperationException($"OpenAI chat completion failed ({(int)res.StatusCode})");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("usage", out var usage))
        {
            var promptTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt64() : 0;
            var completionTokens = usage.TryGetProperty("completion_tokens", out var cmplt) ? cmplt.GetInt64() : 0;
            var totalTokens = usage.TryGetProperty("total_tokens", out var tt) ? tt.GetInt64() : 0;
            logger.LogInformation("AI summary tokens — prompt: {PromptTokens}, completion: {CompletionTokens}, total: {TotalTokens}",
                promptTokens, completionTokens, totalTokens);
        }

        if (!root.TryGetProperty("choices", out var choices))
            throw new InvalidOperationException("OpenAI response missing 'choices' property");
        if (choices.GetArrayLength() == 0)
            throw new InvalidOperationException("OpenAI returned empty choices array");
        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var message))
            throw new InvalidOperationException("OpenAI response missing 'message' property in first choice");
        var content = message.TryGetProperty("content", out var contentEl) ? contentEl.GetString()?.Trim() : null;

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("OpenAI returned empty summary content");

        return content;
    }

    private static async Task<bool> SupportsCaseProfiles(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var exists = await connection.ExecuteScalarAsync<int>(
            @"
            SELECT EXISTS(
                SELECT 1
                FROM pg_class
                WHERE relname = 'case_profiles'
                  AND relkind = 'r'
            )::int
            ",
            transaction: transaction);

        return exists == 1;
    }

    private static async Task<(double[] Embedding, long PromptTokens, long TotalTokens)> GenerateCaseEmbeddingAsync(
        HttpClient httpClient, string text, string apiKey, CancellationToken ct)
    {
        var requestPayload = new { model = "text-embedding-3-small", input = text };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var res = await httpClient.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI embedding failed ({(int)res.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var dataArray = doc.RootElement.GetProperty("data");
        var embeddingElement = dataArray[0].GetProperty("embedding");
        var values = new double[CaseEmbeddingDimensions];
        var idx = 0;
        foreach (var el in embeddingElement.EnumerateArray())
        {
            if (idx < values.Length)
                values[idx++] = el.GetDouble();
        }

        long promptTokens = 0, totalTokens = 0;
        if (doc.RootElement.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt) && pt.ValueKind == JsonValueKind.Number)
                promptTokens = pt.GetInt64();
            if (usage.TryGetProperty("total_tokens", out var tt) && tt.ValueKind == JsonValueKind.Number)
                totalTokens = tt.GetInt64();
        }

        return (values, promptTokens, totalTokens);
    }

    private static string ToPgVectorLiteral(double[] values)
        => $"[{string.Join(',', values.Select(v => v.ToString(CultureInfo.InvariantCulture)))}]";
}
