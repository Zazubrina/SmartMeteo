using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;

namespace Meteo.Api.SecurityTests;

public sealed class ApiSecurityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiSecurityTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task Login_WithValidAdminCredentials_ReturnsJwtAndNoStoreHeaders()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "admin",
            password = "admin-password"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorizedWithoutSensitiveDetails()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "admin",
            password = "wrong-password"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Calibration_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();
        using var content = JsonContent.Create(new { temperature = new[] { 20.0 } });

        using var response = await client.PostAsync("/api/calibration", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Calibration_WithOperatorJwt_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        var token = await LoginAndGetTokenAsync(client, "operator", "operator-password");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var content = JsonContent.Create(new { temperature = new[] { 20.0 } });

        using var response = await client.PostAsync("/api/calibration", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Calibration_WithAdminJwt_AcceptsValidatedPayload()
    {
        using var client = _factory.CreateClient();
        var token = await LoginAndGetTokenAsync(client, "admin", "admin-password");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var content = JsonContent.Create(new { temperature = new[] { 20.0, 21.5 } });

        using var response = await client.PostAsync("/api/calibration", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Calibration_RejectsUnknownParameterAndDoesNotReflectXssInput()
    {
        using var client = _factory.CreateClient();
        var token = await LoginAndGetTokenAsync(client, "admin", "admin-password");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var json = "{\"<script>alert(1)</script>\":[1]}";
        using var content = new StringContent(json);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await client.PostAsync("/api/calibration", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("<script>", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("2026-06-07') OR 1=1--")]
    [InlineData("<script>alert(1)</script>")]
    public async Task HistorySqlOrXssInput_ReturnsBadRequest(string maliciousInput)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/api/history?from={Uri.EscapeDataString(maliciousInput)}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(maliciousInput, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApiResponses_ContainSecurityHeaders()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/current");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    private static async Task<string> LoginAndGetTokenAsync(HttpClient client, string username, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("accessToken").GetString()!;
    }
}
