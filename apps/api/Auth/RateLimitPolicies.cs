namespace Tessera.Api.Auth;

public static class RateLimitPolicies
{
    // Register and login get separate counters (charter section 6 lists them as
    // separate limits). Each is keyed by client IP; the account dimension is covered
    // separately by Identity lockout, so the two together cover "IP + account"
    // without reading the request body at the limiter.
    public const string AuthRegister = "auth-register";
    public const string AuthLogin = "auth-login";

    // Refresh and logout: token operations, limited more generously than login.
    public const string AuthRefresh = "auth-refresh";

    // Video upload initiation: keyed per user (charter section 6).
    public const string VideoUpload = "video-upload";

    // Playback: manifest requests at the charter's 60 per minute per user; segment
    // requests ride the general authenticated tier since the charter has no
    // playback-specific row for them.
    public const string PlaybackManifest = "playback-manifest";
    public const string PlaybackSegment = "playback-segment";

    // The charter's "everything else authenticated" tier: 300 per minute per user.
    public const string AuthenticatedDefault = "authenticated-default";
}
