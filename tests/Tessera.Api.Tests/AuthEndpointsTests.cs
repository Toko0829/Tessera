using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
        _client = factory.CreateClient();
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

    private async Task<string> RegisterAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, ValidPassword));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return email;
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
}
