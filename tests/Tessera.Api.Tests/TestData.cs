using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Tessera.Persistence;
using Tessera.Storage;

namespace Tessera.Api.Tests;

// Seeding shared by the playback and watch-progress suites: a signed-in client and
// videos in a chosen state, written straight to the database and storage so these
// tests exercise serving without running the transcode pipeline.
internal static class TestData
{
    public sealed record TokenBody(string AccessToken);

    public sealed record MeBody(Guid Id, string Email);

    // AllowAutoRedirect is off so a segment 302 can be asserted rather than followed.
    public static async Task<HttpClient> SignedInClientAsync(TesseraApiFactory factory, string prefix)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        var credentials = new { email = $"{prefix}-{Guid.NewGuid():N}@tessera.test", password = "Str0ng!Passphrase" };
        (await client.PostAsJsonAsync("/auth/register", credentials)).EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/auth/login", credentials);
        var token = (await login.Content.ReadFromJsonAsync<TokenBody>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task<Guid> SeedVideoAsync(
        TesseraApiFactory factory, HttpClient owner, VideoStatus status, double? durationSeconds = null)
    {
        var me = await owner.GetFromJsonAsync<MeBody>("/auth/me");
        var videoId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TesseraDbContext>();
        db.Videos.Add(new Video
        {
            Id = videoId,
            OwnerId = me!.Id,
            Title = "Playable clip",
            OriginalFileName = "clip.mp4",
            ContentType = "video/mp4",
            SizeBytes = 1234,
            StorageKey = $"uploads/{me.Id}/{videoId}",
            Status = status,
            DurationSeconds = durationSeconds,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return videoId;
    }

    public static async Task SeedObjectAsync(
        TesseraApiFactory factory, string key, byte[] content, string contentType)
    {
        using var scope = factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();

        var path = Path.Combine(Path.GetTempPath(), $"tessera-test-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllBytesAsync(path, content);
            await storage.UploadFileAsync(key, path, contentType, CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
