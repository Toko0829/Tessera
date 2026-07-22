using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Tessera.Queue;

namespace Tessera.Worker;

// The consume loop: recover anything a previous run left mid-flight, then poll the
// queue, processing one job at a time in its own DI scope.
public sealed class Worker(
    IServiceScopeFactory scopeFactory,
    TranscodeQueue queue,
    IOptions<TranscodeOptions> options,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Tessera transcoding worker started");
        await queue.RequeueInFlightAsync();

        var idleDelay = TimeSpan.FromSeconds(options.Value.PollSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            Guid? videoId;
            try
            {
                videoId = await queue.ClaimAsync();
            }
            catch (RedisException exception)
            {
                logger.LogError(exception, "Queue unavailable; retrying shortly");
                videoId = null;
            }

            if (videoId is null)
            {
                try
                {
                    await Task.Delay(idleDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<TranscodeService>();
                await service.ProcessAsync(videoId.Value, stoppingToken);
                await queue.CompleteAsync(videoId.Value);
            }
            catch (OperationCanceledException)
            {
                // Shutdown mid-job: the entry stays in the processing list and is
                // re-queued on the next start; the service already released the claim.
                break;
            }
        }

        logger.LogInformation("Tessera transcoding worker stopping");
    }
}
