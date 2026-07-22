var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Liveness probe: proves the process is up and serving. This is the only
// endpoint that exists so far and is intentionally public and unauthenticated.
// Real routes land per-module, each with the auth and rate limiting of CLAUDE.md §6.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// Exposed so the integration test host (WebApplicationFactory<Program>) can boot the real app.
public partial class Program;
