using StackExchange.Redis;

namespace Tessera.Queue;

// A Redis-backed work queue for transcode jobs. Enqueue pushes the video id; the
// worker claims a job by moving it onto a processing list (so a crash cannot silently
// drop it), removes it when done, and re-queues anything left in flight on startup.
// The recovery step assumes a single worker instance; scaling out replaces this with
// per-consumer pending lists or streams.
public sealed class TranscodeQueue(IConnectionMultiplexer redis)
{
    private const string QueueKey = "queue:transcode";
    private const string ProcessingKey = "queue:transcode:processing";

    public Task EnqueueAsync(Guid videoId)
        => redis.GetDatabase().ListLeftPushAsync(QueueKey, videoId.ToString());

    // Claims the oldest waiting job, or returns null when the queue is empty.
    public async Task<Guid?> ClaimAsync()
    {
        var value = await redis.GetDatabase()
            .ListMoveAsync(QueueKey, ProcessingKey, ListSide.Right, ListSide.Left);

        return value.IsNullOrEmpty ? null : Guid.Parse(value.ToString());
    }

    public Task CompleteAsync(Guid videoId)
        => redis.GetDatabase().ListRemoveAsync(ProcessingKey, videoId.ToString());

    // Pushes jobs a previous run left mid-flight back onto the queue.
    public async Task RequeueInFlightAsync()
    {
        var database = redis.GetDatabase();
        while (true)
        {
            var value = await database
                .ListMoveAsync(ProcessingKey, QueueKey, ListSide.Right, ListSide.Left);

            if (value.IsNullOrEmpty)
            {
                break;
            }
        }
    }
}
