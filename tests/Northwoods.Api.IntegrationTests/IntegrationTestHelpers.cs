using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Northwoods.Contracts;

namespace Northwoods.Api.IntegrationTests;

internal static class IntegrationTestHelpers
{
    internal const string DefaultBaseUrl = "http://localhost:5100";

    internal static HttpClient CreateClient(string? token = null)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable("NORTHWOODS_API_BASE_URL") ?? DefaultBaseUrl)
        };

        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    internal static async Task<bool> IsRuntimeAvailableAsync(HttpClient client)
    {
        try
        {
            using var response = await client.GetAsync("/healthz");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    internal static async Task<string> LoginAsync(HttpClient client, string tenantId, string email, string password)
    {
        using var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, password, tenantId));
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return payload?.AccessToken ?? throw new InvalidOperationException("Login response missing AccessToken.");
    }
}
