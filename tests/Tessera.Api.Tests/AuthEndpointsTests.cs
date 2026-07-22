using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Tessera.Api.Auth;

namespace Tessera.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class AuthEndpointsTests : IAsyncLifetime
{
    private const string ValidPassword = "Str0ng!Passphrase";
    private const string WrongPassword = "Wr0ng!Passphrase";

    private readonly TesseraApiFactory _factory;
    private readonly HttpClient _client;

    public AuthEndpointsTests(TesseraApiFactory factory)
    {
        _factory = factory;
        // HTTPS so the Secure refresh cookie is stored and returned by the client.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
    }

    // Reset rate-limit counters before each test so one test's requests do not
    // exhaust another's window.
    public Task InitializeAsync() => _factory.ResetRateLimitsAsync();

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@tessera.test";

    private HttpClient ManualCookieClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = false,
    });

    private async Task<string> RegisterAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, ValidPassword));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return email;
    }

    private static string? RefreshCookieFrom(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }

        foreach (var cookie in cookies)
        {
            if (cookie.StartsWith(RefreshTokenCookie.Name + "=", StringComparison.Ordinal))
            {
                var value = cookie[(RefreshTokenCookie.Name.Length + 1)..];
                var end = value.IndexOf(';');
                return end >= 0 ? value[..end] : value;
            }
        }

        return null;
    }

    private static Task<HttpResponseMessage> RefreshWithCookieAsync(HttpClient client, string cookieValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        request.Headers.Add("Cookie", $"{RefreshTokenCookie.Name}={cookieValue}");
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Register_then_login_issues_a_usable_token()
    {
        var email = await RegisterAsync(NewEmail());

        var login = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, ValidPassword));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
        Assert.True(body.ExpiresAt > DateTimeOffset.UtcNow);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        var me = await _client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var meBody = await me.Content.ReadFromJsonAsync<MeResponse>();
        Assert.Equal(email, meBody?.Email);
    }

    [Fact]
    public async Task Register_with_a_weak_password_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/auth/register", new RegisterRequest(NewEmail(), "short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_the_wrong_password_is_unauthorized()
    {
        var email = await RegisterAsync(NewEmail());

        var response = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, WrongPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Repeated_failed_logins_lock_the_account()
    {
        var email = await RegisterAsync(NewEmail());

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, WrongPassword));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.True(await _factory.IsLockedOutAsync(email));
    }

    [Fact]
    public async Task Me_without_a_token_is_unauthorized()
    {
        var response = await _client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_is_rate_limited()
    {
        var email = await RegisterAsync(NewEmail());

        var last = HttpStatusCode.OK;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var response = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, WrongPassword));
            last = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task Login_sets_a_hardened_refresh_cookie()
    {
        var email = await RegisterAsync(NewEmail());

        var login = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, ValidPassword));

        var setCookie = login.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith(RefreshTokenCookie.Name + "=", StringComparison.Ordinal));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_returns_a_new_access_token()
    {
        var email = await RegisterAsync(NewEmail());

        var login = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, ValidPassword));
        var firstCookie = RefreshCookieFrom(login);
        Assert.False(string.IsNullOrEmpty(firstCookie));

        var refresh = await _client.PostAsync("/auth/refresh", content: null);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        var body = await refresh.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body?.AccessToken));

        var rotatedCookie = RefreshCookieFrom(refresh);
        Assert.False(string.IsNullOrEmpty(rotatedCookie));
        Assert.NotEqual(firstCookie, rotatedCookie);
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_revokes_the_family()
    {
        var email = await RegisterAsync(NewEmail());

        using var client = ManualCookieClient();
        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, ValidPassword));
        var original = RefreshCookieFrom(login)!;

        var firstRotation = await RefreshWithCookieAsync(client, original);
        Assert.Equal(HttpStatusCode.OK, firstRotation.StatusCode);
        var rotated = RefreshCookieFrom(firstRotation)!;

        // Present the already-rotated token again: this is the theft signal.
        var reuse = await RefreshWithCookieAsync(client, original);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        // The whole family is now revoked, so even the legitimate latest token fails.
        var afterRevoke = await RefreshWithCookieAsync(client, rotated);
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
    }

    [Fact]
    public async Task Refresh_without_a_cookie_is_unauthorized()
    {
        var response = await _client.PostAsync("/auth/refresh", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var email = await RegisterAsync(NewEmail());

        using var client = ManualCookieClient();
        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, ValidPassword));
        var cookie = RefreshCookieFrom(login)!;

        var logout = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        logout.Headers.Add("Cookie", $"{RefreshTokenCookie.Name}={cookie}");
        var logoutResponse = await client.SendAsync(logout);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var afterLogout = await RefreshWithCookieAsync(client, cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }
}
