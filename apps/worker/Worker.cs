namespace Tessera.Worker;

// Skeleton host. The transcode loop (dequeue → FFmpeg in a sandbox → write ABR
// output) lands with the transcode module. Until then the host stays alive so the
// container has a running process, without faking work it does not yet do.
public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Tessera transcoding worker started; no queue wired yet");

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Tessera transcoding worker stopping");
        }
    }
}
