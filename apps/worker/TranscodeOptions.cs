namespace Tessera.Worker;

public sealed class TranscodeOptions
{
    public const string SectionName = "Transcode";

    // Binary paths; defaults assume they are on PATH.
    public string FfmpegPath { get; init; } = "ffmpeg";
    public string FfprobePath { get; init; } = "ffprobe";

    // Hard wall-clock ceiling for one transcode; the process is killed past it.
    public int TimeoutMinutes { get; init; } = 10;

    // How long to sleep when the queue is empty.
    public int PollSeconds { get; init; } = 3;
}
