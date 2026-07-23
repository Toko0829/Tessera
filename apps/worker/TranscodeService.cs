using Microsoft.EntityFrameworkCore;
using Tessera.Domain;
using Tessera.Persistence;
using Tessera.Storage;

namespace Tessera.Worker;

// Processes one transcode job end to end: claim the video, pull the original, run
// ffmpeg into an HLS ladder, push the renditions, and record the outcome. A failed
// job marks the video Failed instead of throwing past the worker loop; a shutdown
// mid-job puts the video back to Uploaded so a restart can claim it again.
public sealed class TranscodeService(
    TesseraDbContext db,
    IObjectStorage storage,
    FfmpegTranscoder transcoder,
    ILogger<TranscodeService> logger,
    TimeProvider clock)
{
    public async Task ProcessAsync(Guid videoId, CancellationToken ct)
    {
        // Claim: only an Uploaded video moves to Processing. Anything else means the
        // job is stale (already handled, or left over from a crashed run).
        var claimed = await db.Videos
            .Where(v => v.Id == videoId && v.Status == VideoStatus.Uploaded)
            .ExecuteUpdateAsync(setters => setters.SetProperty(v => v.Status, VideoStatus.Processing), ct);
        if (claimed == 0)
        {
            logger.LogWarning("Skipping transcode for {VideoId}: not in the Uploaded state", videoId);
            return;
        }

        var video = await db.Videos.AsNoTracking().SingleAsync(v => v.Id == videoId, ct);
        var workDir = Path.Combine(Path.GetTempPath(), "tessera-transcode", videoId.ToString("N"));
        var outputDir = Path.Combine(workDir, "out");
        var started = clock.GetUtcNow();

        try
        {
            Directory.CreateDirectory(outputDir);

            var inputPath = Path.Combine(workDir, "input.bin");
            await storage.DownloadToFileAsync(video.StorageKey, inputPath, ct);

            var hasAudio = await transcoder.HasAudioAsync(inputPath, ct);
            var durationSeconds = await transcoder.GetDurationSecondsAsync(inputPath, ct);
            await transcoder.TranscodeToHlsAsync(inputPath, outputDir, hasAudio, ct);

            foreach (var file in Directory.EnumerateFiles(outputDir))
            {
                var name = Path.GetFileName(file);
                await storage.UploadFileAsync(HlsPaths.Key(videoId, name), file, ContentTypeFor(name), ct);
            }

            // Status and duration land together: a Ready video always has a length.
            await db.Videos
                .Where(v => v.Id == videoId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(v => v.Status, VideoStatus.Ready)
                    .SetProperty(v => v.DurationSeconds, durationSeconds), CancellationToken.None);
            logger.LogInformation(
                "Transcoded {VideoId} to HLS in {ElapsedSeconds:0}s", videoId, (clock.GetUtcNow() - started).TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a bad input: release the claim so a restart retries it.
            await SetStatusAsync(videoId, VideoStatus.Uploaded, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Transcode failed for {VideoId}", videoId);
            await SetStatusAsync(videoId, VideoStatus.Failed, CancellationToken.None);
        }
        finally
        {
            TryDeleteWorkDir(workDir);
        }
    }

    private Task SetStatusAsync(Guid videoId, VideoStatus status, CancellationToken ct)
        => db.Videos
            .Where(v => v.Id == videoId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(v => v.Status, status), ct);

    private static string ContentTypeFor(string fileName) => Path.GetExtension(fileName) switch
    {
        ".m3u8" => "application/vnd.apple.mpegurl",
        ".ts" => "video/mp2t",
        _ => "application/octet-stream",
    };

    private void TryDeleteWorkDir(string workDir)
    {
        try
        {
            Directory.Delete(workDir, recursive: true);
        }
        catch (IOException exception)
        {
            // Best-effort cleanup of a temp directory; the job outcome is unaffected.
            logger.LogWarning(exception, "Could not delete transcode work directory {WorkDir}", workDir);
        }
    }
}
