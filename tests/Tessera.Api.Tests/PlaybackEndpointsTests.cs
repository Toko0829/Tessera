using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Tessera.Domain;
using Tessera.Persistence;

namespace Tessera.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class PlaybackEndpointsTests : IAsyncLifetime
{
    // The exact layout the worker writes: a master pointing at two variant playlists,
    // one of which points at a segment. Seeded straight into MinIO so these tests
    // cover serving without running ffmpeg.
    private const string MasterPlaylist =
        "#EXTM3U\n" +
        "#EXT-X-STREAM-INF:BANDWIDTH=2800000,RESOLUTION=1280x720\n" +
        "v0_index.m3u8\n" +
        "#EXT-X-STREAM-INF:BANDWIDTH=1400000,RESOLUTION=854x480\n" +
        "v1_index.m3u8\n";

    private const string VariantPlaylist =
        "#EXTM3U\n" +
        "#EXT-X-VERSION:3\n" +
        "#EXT-X-TARGETDURATION:4\n" +
        "#EXTINF:4.0,\n" +
        "v0_seg000.ts\n" +
        "#EXT-X-ENDLIST\n";

    private static readonly byte[] SegmentBytes = Encoding.ASCII.GetBytes("fake transport stream bytes");

    private readonly TesseraApiFactory _factory;

    public PlaybackEndpointsTests(TesseraApiFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.ResetRateLimitsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_owner_streams_the_master_and_variant_playlists()
    {
        var (client, videoId) = await ReadyVideoAsync();
        using (client)
        {
            var master = await client.GetAsync($"/videos/{videoId}/hls/master.m3u8");
            Assert.Equal(HttpStatusCode.OK, master.StatusCode);
            Assert.Equal("application/vnd.apple.mpegurl", master.Content.Headers.ContentType?.MediaType);
            Assert.Equal(MasterPlaylist, await master.Content.ReadAsStringAsync());

            var variant = await client.GetAsync($"/videos/{videoId}/hls/v0_index.m3u8");
            Assert.Equal(HttpStatusCode.OK, variant.StatusCode);
            Assert.Equal(VariantPlaylist, await variant.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task A_segment_request_redirects_to_a_signed_storage_url_that_serves_the_bytes()
    {
        var (client, videoId) = await ReadyVideoAsync();
        using (client)
        {
            var response = await client.GetAsync($"/videos/{videoId}/hls/v0_seg000.ts");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

            var location = response.Headers.Location;
            Assert.NotNull(location);
            Assert.True(location!.IsAbsoluteUri);
            Assert.Contains("X-Amz-Signature", location.Query);

            // The redirect target must serve the object with no further credentials.
            using var plain = new HttpClient();
            var bytes = await plain.GetByteArrayAsync(location);
            Assert.Equal(SegmentBytes, bytes);
        }
    }

    [Fact]
    public async Task A_user_cannot_play_another_users_video()
    {
        var (owner, videoId) = await ReadyVideoAsync();
        owner.Dispose();
        using var attacker = await SignedInClientAsync();

        var playlist = await attacker.GetAsync($"/videos/{videoId}/hls/master.m3u8");
        Assert.Equal(HttpStatusCode.Forbidden, playlist.StatusCode);

        var segment = await attacker.GetAsync($"/videos/{videoId}/hls/v0_seg000.ts");
        Assert.Equal(HttpStatusCode.Forbidden, segment.StatusCode);
    }

    [Fact]
    public async Task Playback_requires_authentication()
    {
        var (owner, videoId) = await ReadyVideoAsync();
        owner.Dispose();
        using var anonymous = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await anonymous.GetAsync($"/videos/{videoId}/hls/master.m3u8");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_video_that_is_not_ready_cannot_be_played()
    {
        using var client = await SignedInClientAsync();
        var videoId = await TestData.SeedVideoAsync(_factory, client, VideoStatus.Processing);

        var response = await client.GetAsync($"/videos/{videoId}/hls/master.m3u8");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task File_names_outside_the_ladder_layout_are_refused()
    {
        var (client, videoId) = await ReadyVideoAsync();
        using (client)
        {
            // Well-formed extension, name outside the worker's layout.
            var playlist = await client.GetAsync($"/videos/{videoId}/hls/evil.m3u8");
            Assert.Equal(HttpStatusCode.NotFound, playlist.StatusCode);

            var segment = await client.GetAsync($"/videos/{videoId}/hls/x.ts");
            Assert.Equal(HttpStatusCode.NotFound, segment.StatusCode);

            // A rendition this video does not have: valid name, no object.
            var missing = await client.GetAsync($"/videos/{videoId}/hls/v9_index.m3u8");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    [Fact]
    public async Task The_manifest_rate_limit_triggers()
    {
        var (client, videoId) = await ReadyVideoAsync();
        using (client)
        {
            for (var i = 0; i < 60; i++)
            {
                var allowed = await client.GetAsync($"/videos/{videoId}/hls/master.m3u8");
                Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            }

            var limited = await client.GetAsync($"/videos/{videoId}/hls/master.m3u8");
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        }
    }

    // A signed-in client plus a Ready video with its HLS ladder seeded in storage.
    private async Task<(HttpClient Client, Guid VideoId)> ReadyVideoAsync()
    {
        var client = await SignedInClientAsync();
        var videoId = await TestData.SeedVideoAsync(_factory, client, VideoStatus.Ready);

        await TestData.SeedObjectAsync(_factory, HlsPaths.Key(videoId, "master.m3u8"), Encoding.UTF8.GetBytes(MasterPlaylist), "application/vnd.apple.mpegurl");
        await TestData.SeedObjectAsync(_factory, HlsPaths.Key(videoId, "v0_index.m3u8"), Encoding.UTF8.GetBytes(VariantPlaylist), "application/vnd.apple.mpegurl");
        await TestData.SeedObjectAsync(_factory, HlsPaths.Key(videoId, "v0_seg000.ts"), SegmentBytes, "video/mp2t");

        return (client, videoId);
    }

    private Task<HttpClient> SignedInClientAsync() => TestData.SignedInClientAsync(_factory, "playback");
}
