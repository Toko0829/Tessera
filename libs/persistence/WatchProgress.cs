namespace Tessera.Persistence;

// Where a user last was in a video, one row per (user, video). Written from the
// player at a fixed cadence and read back to resume; last write wins.
public sealed class WatchProgress
{
    public Guid UserId { get; init; }
    public Guid VideoId { get; init; }

    public double PositionSeconds { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public TesseraUser? User { get; init; }
    public Video? Video { get; init; }
}
