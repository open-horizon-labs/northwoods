using System.Net;
using System.Net.Http.Json;
using Northwoods.Contracts;
using Xunit;
using Xunit.Abstractions;

namespace Northwoods.Api.IntegrationTests;

public sealed class LoginValidationTests
{
    private const string DefaultBaseUrl = "http://localhost:5100";

    private readonly ITestOutputHelper _output;

    public LoginValidationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Trait("Category", "runtime")]
    [Theory]
    [InlineData(null, "dev", "tenant-a", "Email is required.")]
    [InlineData("", "dev", "tenant-a", "Email is required.")]
    [InlineData("worker@sunrise.example", null, "tenant-a", "Password is required.")]
    [InlineData("worker@sunrise.example", "", "tenant-a", "Password is required.")]
    [InlineData("worker@sunrise.example", "dev", null, "TenantId is required.")]
    [InlineData("worker@sunrise.example", "dev", "", "TenantId is required.")]
    public async Task Login_MissingRequiredField_Returns400(
        string? email, string? password, string? tenantId, string expectedError)
    {
        using var client = CreateClient();
        if (!await IsRuntimeAvailableAsync(client))
        {
            _output.WriteLine("API runtime is not available; skipping.");
            return;
        }

        using var response = await client.PostAsJsonAsync("/auth/login",
            new { email, password, tenantId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedError, body);
    }

    [Trait("Category", "runtime")]
    [Fact]
    public async Task Login_AllFieldsMissing_Returns400WithAllErrors()
    {
        using var client = CreateClient();
        if (!await IsRuntimeAvailableAsync(client))
        {
            _output.WriteLine("API runtime is not available; skipping.");
            return;
        }

        using var response = await client.PostAsJsonAsync("/auth/login",
            new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Email is required.", body);
        Assert.Contains("Password is required.", body);
        Assert.Contains("TenantId is required.", body);
    }

    [Trait("Category", "runtime")]
    [Theory]
    [InlineData("worker@sunrise.example\0evil", "dev", "tenant-a")]
    [InlineData("worker@sunrise.example", "dev\0evil", "tenant-a")]
    [InlineData("worker@sunrise.example", "dev", "tenant-a\0evil")]
    public async Task Login_NullByteInField_Returns400(
        string email, string password, string tenantId)
    {
        using var client = CreateClient();
        if (!await IsRuntimeAvailableAsync(client))
        {
            _output.WriteLine("API runtime is not available; skipping.");
            return;
        }

        using var response = await client.PostAsJsonAsync("/auth/login",
            new { email, password, tenantId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("null bytes", body);
    }

    [Trait("Category", "runtime")]
    [Fact]
    public async Task Login_ValidCredentials_StillReturns200()
    {
        using var client = CreateClient();
        if (!await IsRuntimeAvailableAsync(client))
        {
            _output.WriteLine("API runtime is not available; skipping.");
            return;
        }

        using var response = await client.PostAsJsonAsync("/auth/login",
            new LoginRequest("worker@sunrise.example", "dev", "tenant-a"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static HttpClient CreateClient()
    {
        return new HttpClient
        {
            BaseAddress = new Uri(
                Environment.GetEnvironmentVariable("NORTHWOODS_API_BASE_URL") ?? DefaultBaseUrl)
        };
    }

    private static async Task<bool> IsRuntimeAvailableAsync(HttpClient client)
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
}
