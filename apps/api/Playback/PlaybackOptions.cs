namespace Tessera.Api.Playback;

public sealed class PlaybackOptions
{
    public const string SectionName = "Playback";

    // The charter caps playback URL expiry at 5 minutes (CLAUDE.md section 6). Each
    // segment URL is minted fresh at request time, so this never needs to cover the
    // whole runtime of a video.
    public int SegmentUrlExpiryMinutes { get; init; } = 5;

    public TimeSpan SegmentUrlExpiry => TimeSpan.FromMinutes(SegmentUrlExpiryMinutes);
}
