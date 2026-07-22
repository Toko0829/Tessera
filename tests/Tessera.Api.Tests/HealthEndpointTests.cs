using System.Net;
using System.Net.Http.Json;

namespace Tessera.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class HealthEndpointTests(TesseraApiFactory factory)
{
    [Fact]
    public async Task Health_returns_ok()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("ok", body?.Status);
    }

    private sealed record HealthResponse(string Status);
}
