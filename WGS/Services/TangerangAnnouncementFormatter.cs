using WGS.Models;

namespace WGS.Services;

/// <summary>
/// Stateless text and parsing rules for the Tangerang Discord Announcement
/// Center. These methods have no Discord credentials, disk writes, or network
/// calls so we can test every safety boundary without a live server.
///
/// IMPORTANT: the Discord HTTP request must also set allowed_mentions.parse
/// to [] to prevent user-generated text from pinging everyone or arbitrary
/// roles. Text cleaning alone is not an adequate mention-safety control.
/// </summary>
public static class TangerangAnnouncementFormatter
{
    /// <summary>
    /// Commands use a pipe delimiter so multiword server names and free-text
    /// messages are unambiguous: !announce Tangerang Test | Restart soon.
    /// Empty, oversized, and control-character-only messages are rejected.
    /// </summary>
    public static bool TryParseAnnouncement(string raw, out string serverName, out string message)
    {
        serverName = message = string.Empty;
        int separator = raw.IndexOf('|');
        if (separator <= 0) return false;
        serverName = raw[..separator].Trim();
        message = Clean(raw[(separator + 1)..]);
        return serverName.Length is > 0 and <= 100
            && message.Length is > 0 and <= 500;
    }

    public static bool TryParseRestart(string raw, int defaultSeconds,
        out string serverName, out int seconds)
    {
        serverName = string.Empty;
        seconds = 0;
        int separator = raw.IndexOf('|');
        var name = separator < 0 ? raw : raw[..separator];
        serverName = name.Trim();
        if (serverName.Length is < 1 or > 100) return false;
        if (separator < 0)
            seconds = defaultSeconds;
        else if (!int.TryParse(raw[(separator + 1)..].Trim(), out seconds))
            return false;
        return seconds is >= 30 and <= 600;
    }

    /// <summary>
    /// Removes control characters while preserving ordinary punctuation.
    /// Discord's allowed_mentions=[] remains the actual anti-ping safeguard.
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var cleaned = new string(text.Select(c =>
            char.IsControl(c) ? ' ' : c).ToArray());
        return cleaned.Trim();
    }

    /// <summary>Pure check to avoid sending any unrequested announcements.</summary>
    public static bool MayAnnounce(GameServer server)
        => server.DiscordAnnouncementsEnabled;

    public static string LifecycleMessage(GameServer server, ServerStatus status)
    {
        var name = Clean(server.DisplayName);
        return status switch
        {
            ServerStatus.Running => $"🟢 **{name}** is online. Players can join.",
            ServerStatus.Stopped => $"⚫ **{name}** is offline.",
            ServerStatus.Error   => $"🔴 **{name}** encountered a server error.",
            _ => string.Empty
        };
    }

    public static string CustomMessage(GameServer server, string text)
        => $"📣 **{Clean(server.DisplayName)}**\n{Clean(text)}";

    public static string RestartWarning(GameServer server, int seconds)
    {
        var unit = seconds == 60 ? "1 minute" : $"{seconds} seconds";
        return $"🔄 **{Clean(server.DisplayName)}** will restart in **{unit}**. Save your progress and find a safe place.";
    }

    /// <summary>
    /// Emission checkpoints are unique and ordered by remaining time.
    /// A 60s countdown announces 60s and 10s, a 30s countdown 30s and 10s;
    /// long countdowns also receive a 60s reminder. Existing manual
    /// Restart and daily automatic restart are unaffected.
    /// </summary>
    public static int[] RestartCheckpoints(int seconds)
    {
        if (seconds is < 30 or > 600) return [];
        var points = new List<int> { seconds };
        if (seconds > 60) points.Add(60);
        if (seconds > 10) points.Add(10);
        return points.Distinct().OrderDescending().ToArray();
    }
}
