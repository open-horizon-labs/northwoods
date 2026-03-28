using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Dapper;
using Microsoft.IdentityModel.Tokens;
using Northwoods.Contracts;
using Northwoods.Tenancy;

namespace Northwoods.Api;

internal static class AuthEndpoints
{
    public static WebApplication MapAuthEndpoints(
        this WebApplication app,
        SigningCredentials jwtSigningCredentials,
        string jwtIssuer,
        string jwtAudience,
        TimeSpan jwtExpiration)
    {
        app.MapPost("/auth/login", async (LoginRequest request, DbConnectionFactory dbFactory) =>
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(request.Email)) errors.Add("Email is required.");
            if (string.IsNullOrWhiteSpace(request.Password)) errors.Add("Password is required.");

            static bool ContainsNullByte(string? value) => value is not null && value.Contains('\0');
            if (ContainsNullByte(request.Email) || ContainsNullByte(request.Password) || ContainsNullByte(request.TenantId))
                errors.Add("Fields must not contain null bytes.");

            if (errors.Count > 0)
                return Results.BadRequest(new { errors });

            var tenantId = request.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                await using var global = await dbFactory.OpenUnscopedSessionAsync();
                var tenants = (await global.Connection.QueryAsync<string>(
                    "SELECT tenant_id FROM users WHERE email = @Email",
                    new { request.Email },
                    global.Transaction)).AsList();
                if (tenants.Count != 1)
                    return Results.Unauthorized();
                tenantId = tenants[0];
            }

            await using var session = await dbFactory.OpenSessionAsync(tenantId);
            var user = await session.Connection.QueryFirstOrDefaultAsync<(Guid id, string role, string password_hash)>(
                "SELECT id, role, password_hash FROM users WHERE email = @Email AND tenant_id = @TenantId",
                new { request.Email, TenantId = tenantId },
                session.Transaction);

            if (user == default || !VerifyPassword(request.Password, user.password_hash))
                return Results.Unauthorized();

            if (!Enum.TryParse<UserRole>(user.role, true, out var role))
                return Results.Unauthorized();

            var token = CreateJwt(
                user.id,
                tenantId,
                role,
                jwtSigningCredentials,
                jwtIssuer,
                jwtAudience,
                jwtExpiration);

            return Results.Ok(new LoginResponse(token, tenantId, role));
        })
            .WithName("Login").WithTags("Auth")
            .WithSummary("Authenticates a user and returns a signed JWT access token.");

        return app;
    }

    private static string CreateJwt(
        Guid userId,
        string tenantId,
        UserRole role,
        SigningCredentials credentials,
        string issuer,
        string audience,
        TimeSpan lifetime)
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(AuthClaims.UserId, userId.ToString()),
                new Claim(AuthClaims.TenantId, tenantId),
                new Claim(AuthClaims.Role, role.ToString())
            }),
            NotBefore = now,
            IssuedAt = now,
            Expires = now.Add(lifetime),
            SigningCredentials = credentials,
            Issuer = issuer,
            Audience = audience
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(descriptor);
        return handler.WriteToken(token);
    }

    private static bool VerifyPassword(string requestPassword, string passwordHash)
    {
        return BCrypt.Net.BCrypt.Verify(requestPassword, passwordHash);
    }
}
