using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Tessera.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class VideoEndpointsTests : IAsyncLifetime
{
    // 32 bytes with "ftyp" at offset 4, so it passes the ISO base media check.
    private static readonly byte[] ValidMp4 = BuildMp4Header();

    private readonly TesseraApiFactory _factory;

    public VideoEndpointsTests(TesseraApiFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.ResetRateLimitsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Upload_then_complete_marks_the_video_uploaded()
    {
        using var client = await SignedInClientAsync();

        var init = await InitiateAsync(client, ValidMp4.Length);
        var uploaded = await UploadToStorageAsync(init, ValidMp4);
        Assert.InRange((int)uploaded, 200, 299);

        var complete = await client.PostAsync($"/videos/{init.VideoId}/complete", content: null);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var video = await complete.Content.ReadFromJsonAsync<VideoBody>();
        Assert.Equal("Uploaded", video?.Status);
    }

    [Fact]
    public async Task Completing_a_non_video_upload_is_rejected()
    {
        using var client = await SignedInClientAsync();

        var init = await InitiateAsync(client, 100);
        await UploadToStorageAsync(init, Encoding.UTF8.GetBytes("this is plain text, not a video file"));

        var complete = await client.PostAsync($"/videos/{init.VideoId}/complete", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, complete.StatusCode);
    }

    [Fact]
    public async Task A_user_cannot_complete_another_users_upload()
    {
        using var owner = await SignedInClientAsync();
        using var attacker = await SignedInClientAsync();

        var init = await InitiateAsync(owner, ValidMp4.Length);
        await UploadToStorageAsync(init, ValidMp4);

        var complete = await attacker.PostAsync($"/videos/{init.VideoId}/complete", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, complete.StatusCode);
    }

    [Fact]
    public async Task Initiating_an_upload_requires_authentication()
    {
        using var anonymous = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await anonymous.PostAsJsonAsync(
            "/videos",
            new { title = "x", fileName = "a.mp4", contentType = "video/mp4", sizeBytes = 100 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Listing_returns_only_the_callers_videos()
    {
        using var mine = await SignedInClientAsync();
        using var other = await SignedInClientAsync();

        var init = await InitiateAsync(mine, ValidMp4.Length);
        await InitiateAsync(other, ValidMp4.Length);

        var videos = await mine.GetFromJsonAsync<List<VideoBody>>("/videos");

        Assert.NotNull(videos);
        Assert.Single(videos!);
        Assert.Equal(init.VideoId, videos![0].Id);
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var credentials = new { email = $"video-{Guid.NewGuid():N}@tessera.test", password = "Str0ng!Passphrase" };
        (await client.PostAsJsonAsync("/auth/register", credentials)).EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/auth/login", credentials);
        var token = (await login.Content.ReadFromJsonAsync<TokenBody>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<InitiateBody> InitiateAsync(HttpClient client, long sizeBytes)
    {
        var response = await client.PostAsJsonAsync(
            "/videos",
            new { title = "My clip", fileName = "clip.mp4", contentType = "video/mp4", sizeBytes });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InitiateBody>())!;
    }

    private static async Task<HttpStatusCode> UploadToStorageAsync(InitiateBody init, byte[] content)
    {
        using var form = new MultipartFormDataContent();
        foreach (var (name, value) in init.Fields)
        {
            form.Add(new StringContent(value), name);
        }
        form.Add(new ByteArrayContent(content), "file", "clip.mp4");

        using var http = new HttpClient();
        using var response = await http.PostAsync(init.UploadUrl, form);
        return response.StatusCode;
    }

    private static byte[] BuildMp4Header()
    {
        var bytes = new byte[32];
        bytes[3] = 0x18;
        bytes[4] = (byte)'f';
        bytes[5] = (byte)'t';
        bytes[6] = (byte)'y';
        bytes[7] = (byte)'p';
        bytes[8] = (byte)'i';
        bytes[9] = (byte)'s';
        bytes[10] = (byte)'o';
        bytes[11] = (byte)'m';
        return bytes;
    }

    private sealed record TokenBody(string AccessToken);

    private sealed record InitiateBody(Guid VideoId, string UploadUrl, Dictionary<string, string> Fields);

    private sealed record VideoBody(Guid Id, string Title, string Status, DateTimeOffset CreatedAt);
}
