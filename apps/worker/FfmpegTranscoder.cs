using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace Tessera.Worker;

public sealed class TranscodeException(string message) : Exception(message);

// Runs ffprobe and ffmpeg as external processes. Uploads are hostile input
// (CLAUDE.md section 6), so every run has a hard wall-clock ceiling and is killed
// past it; a non-zero exit fails the job with the tool's own error tail.
public sealed class FfmpegTranscoder(IOptions<TranscodeOptions> options)
{
    private readonly TranscodeOptions _options = options.Value;

    public async Task<bool> HasAudioAsync(string inputPath, CancellationToken ct)
    {
        var output = await RunAsync(
            _options.FfprobePath,
            ["-v", "error", "-select_streams", "a", "-show_entries", "stream=codec_type", "-of", "csv=p=0", inputPath],
            TimeSpan.FromMinutes(1),
            ct);

        return !string.IsNullOrWhiteSpace(output);
    }

    // The container's duration in seconds. Recorded on the video so the API can
    // validate watch-progress positions against the real length.
    public async Task<double> GetDurationSecondsAsync(string inputPath, CancellationToken ct)
    {
        var output = await RunAsync(
            _options.FfprobePath,
            ["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", inputPath],
            TimeSpan.FromMinutes(1),
            ct);

        if (!double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
            seconds <= 0)
        {
            throw new TranscodeException("ffprobe did not report a positive duration for the input.");
        }

        return seconds;
    }

    // Produces a two-rendition HLS ladder (720p and 480p) with a master playlist,
    // stripping all source metadata (GPS, device identifiers).
    public async Task TranscodeToHlsAsync(string inputPath, string outputDir, bool hasAudio, CancellationToken ct)
    {
        var arguments = new List<string>
        {
            "-hide_banner", "-y",
            "-i", inputPath,
            "-filter_complex", "[0:v]split=2[v1][v2];[v1]scale=-2:720[v1out];[v2]scale=-2:480[v2out]",
            "-map", "[v1out]", "-c:v:0", "libx264", "-preset", "veryfast", "-b:v:0", "2500k",
        };

        if (hasAudio)
        {
            arguments.AddRange(["-map", "a:0", "-c:a:0", "aac", "-b:a:0", "128k"]);
        }

        arguments.AddRange(["-map", "[v2out]", "-c:v:1", "libx264", "-preset", "veryfast", "-b:v:1", "1200k"]);

        if (hasAudio)
        {
            arguments.AddRange(["-map", "a:0", "-c:a:1", "aac", "-b:a:1", "96k"]);
        }

        arguments.AddRange(
        [
            "-map_metadata", "-1",
            "-f", "hls", "-hls_time", "4", "-hls_playlist_type", "vod",
            "-hls_segment_filename", Path.Combine(outputDir, "v%v_seg%03d.ts"),
            "-master_pl_name", "master.m3u8",
            "-var_stream_map", hasAudio ? "v:0,a:0 v:1,a:1" : "v:0 v:1",
            Path.Combine(outputDir, "v%v_index.m3u8"),
        ]);

        await RunAsync(_options.FfmpegPath, arguments, TimeSpan.FromMinutes(_options.TimeoutMinutes), ct);
    }

    private static async Task<string> RunAsync(
        string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new TranscodeException($"Could not start {executable}.");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(ct);
        var standardError = process.StandardError.ReadToEndAsync(ct);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            throw new TranscodeException(
                $"{Path.GetFileName(executable)} exceeded the {timeout.TotalMinutes:0}-minute limit and was killed.");
        }

        if (process.ExitCode != 0)
        {
            throw new TranscodeException(Tail(await standardError, 2000));
        }

        return await standardOutput;
    }

    private static string Tail(string text, int maxLength)
        => text.Length <= maxLength ? text : text[^maxLength..];
}
