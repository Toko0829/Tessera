using System.Net;
using System.Net.Http.Json;
using Tessera.Persistence;

namespace Tessera.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class WatchProgressTests : IAsyncLifetime
{
    private const double DurationSeconds = 120;

    private readonly TesseraApiFactory _factory;

    public WatchProgressTests(TesseraApiFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.ResetRateLimitsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Saved_progress_comes_back_on_the_detail_and_the_list()
    {
        using var client = await SignedInClientAsync();
        var videoId = await ReadyVideoAsync(client);

        var save = await client.PutAsJsonAsync($"/videos/{videoId}/progress", new { positionSeconds = 42.5 });
        Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);

        var detail = await client.GetFromJsonAsync<VideoBody>($"/videos/{videoId}");
        Assert.Equal(42.5, detail!.PositionSeconds);
        Assert.Equal(DurationSeconds, detail.DurationSeconds);

        var list = await client.GetFromJsonAsync<List<VideoBody>>("/videos");
        Assert.Equal(42.5, list!.Single(v => v.Id == videoId).PositionSeconds);
    }

    [Fact]
    public async Task The_latest_write_wins()
    {
        using var client = await SignedInClientAsync();
        var videoId = await ReadyVideoAsync(client);

        (await client.PutAsJsonAsync($"/videos/{videoId}/progress", new { positionSeconds = 10 })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/videos/{videoId}/progress", new { positionSeconds = 20 })).EnsureSuccessStatusCode();

        var detail = await client.GetFromJsonAsync<VideoBody>($"/videos/{videoId}");
        Assert.Equal(20, detail!.PositionSeconds);
    }

    [Fact]
    public async Task A_user_cannot_write_progress_on_another_users_video()
    {
        using var owner = await SignedInClientAsync();
        using var attacker = await SignedInClientAsync();
        var videoId = await ReadyVideoAsync(owner);

        var response = await attacker.PutAsJsonAsync($"/videos/{videoId}/progress", new { positionSeconds = 5 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Progress_on_a_video_that_is_not_ready_is_rejected()
    {
        using var client = await SignedInClientAsync();
        var videoId = await TestData.SeedVideoAsync(_factory, client, VideoStatus.Processing);

        var response = await client.PutAsJsonAsync($"/videos/{videoId}/progress", new { positionSeconds = 5 });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Positions_outside_the_video_are_rejected()
    {
        using var client = await SignedInClientAsync();
        var videoId = await ReadyVideoAsync(client);

        var negative = await client.PutAsJsonAsync($"/videos/{videoId}/progress", new { positionSeconds = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);

        var pastTheEnd = await client.PutAsJsonAsync(
            $"/videos/{videoId}/progress", new { positionSeconds = DurationSeconds + 1 });
        Assert.Equal(HttpStatusCode.BadRequest, pastTheEnd.StatusCode);
    }

    [Fact]
    public async Task The_progress_rate_limit_triggers()
    {
        using var client = await SignedInClientAsync();
        var videoId = await ReadyVideoAsync(client);

        for (var i = 0; i < 120; i++)
        {
            var allowed = await client.PutAsJsonAsync($"/videos/{videoId}/progress", new { positionSeconds = i });
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        }

        var limited = await client.PutAsJsonAsync($"/videos/{videoId}/progress", new { positionSeconds = 1 });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    private Task<HttpClient> SignedInClientAsync() => TestData.SignedInClientAsync(_factory, "progress");

    private Task<Guid> ReadyVideoAsync(HttpClient client)
        => TestData.SeedVideoAsync(_factory, client, VideoStatus.Ready, DurationSeconds);

    private sealed record VideoBody(
        Guid Id, string Title, string Status, DateTimeOffset CreatedAt, double? DurationSeconds, double? PositionSeconds);
}
