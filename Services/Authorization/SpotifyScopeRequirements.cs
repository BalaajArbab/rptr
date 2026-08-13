namespace rptr.Services.Authorization;

public static class SpotifyScopeRequirements
{
    public static readonly string[] Required =
    [
        "streaming",
        "user-read-private",
        "user-read-email",
        "playlist-read-private",
        "user-read-playback-state",
        "user-modify-playback-state"
    ];

    public static bool ContainsAll(string scopes)
    {
        var granted = scopes.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return Required.All(required =>
            granted.Contains(required, StringComparer.Ordinal));
    }
}
