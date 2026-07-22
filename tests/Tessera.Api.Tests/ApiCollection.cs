namespace Tessera.Api.Tests;

// All API integration tests share one factory (one set of containers) and run
// sequentially, so per-test Redis resets are not raced by a parallel class.
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<TesseraApiFactory>
{
    public const string Name = "api";
}
