namespace Tessera.Domain;

// The single definition of where a video's HLS ladder lives in object storage.
// The worker writes this layout and the API serves it; if the two drifted apart
// playback would break silently, so the convention is code, not folklore.
public static class HlsPaths
{
    public const string MasterPlaylist = "master.m3u8";

    public static string Key(Guid videoId, string fileName) => $"videos/{videoId}/hls/{fileName}";
}
