namespace Tessera.Domain;

// Recognises supported video containers by their leading bytes, so an upload is
// accepted for what it actually is, not for a `.mp4` name or a client-claimed MIME
// type (CLAUDE.md section 6).
public static class VideoSignature
{
    // Bytes to read from the front of an upload to run every check below.
    public const int HeaderBytesNeeded = 16;

    public static bool IsSupportedVideo(ReadOnlySpan<byte> header)
        => IsIsoBaseMedia(header) || IsMatroska(header) || IsAvi(header);

    // MP4, MOV, M4V: an ISO base media file has "ftyp" at offset 4.
    private static bool IsIsoBaseMedia(ReadOnlySpan<byte> h)
        => h.Length >= 8 && h[4] == (byte)'f' && h[5] == (byte)'t' && h[6] == (byte)'y' && h[7] == (byte)'p';

    // WebM, MKV: Matroska starts with the EBML magic 1A 45 DF A3.
    private static bool IsMatroska(ReadOnlySpan<byte> h)
        => h.Length >= 4 && h[0] == 0x1A && h[1] == 0x45 && h[2] == 0xDF && h[3] == 0xA3;

    // AVI: "RIFF" then a size then "AVI ".
    private static bool IsAvi(ReadOnlySpan<byte> h)
        => h.Length >= 12
            && h[0] == (byte)'R' && h[1] == (byte)'I' && h[2] == (byte)'F' && h[3] == (byte)'F'
            && h[8] == (byte)'A' && h[9] == (byte)'V' && h[10] == (byte)'I' && h[11] == (byte)' ';
}
