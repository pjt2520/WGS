using System.Text.RegularExpressions;

namespace WGS.Services;

/// <summary>
/// Reads vanilla Valheim server log messages for PlayFab/Steam player
/// observations and the *current* session's six-digit PlayFab join code.
///
/// Neither the player list nor a lobby-reported "now N player(s)" is a
/// reliable, authoritative census in crossplay mode. The caller must keep
/// PlayerCountUnknown=true when falling back to this parser.
///
/// Identity safety: "Got character ZDOID from NAME" contains no socket ID.
/// Attach a name to a platform ID only when exactly one unnamed socket
/// exists. With overlapping joins, retain names as anonymous observations
/// rather than inventing a correlation between an Xbox/PS5 ID and a Viking.
/// </summary>
public sealed class ValheimPlayerTracker
{
    private static readonly Regex SteamConnect = new(
        @"Got connection SteamID (\d+)", RegexOptions.Compiled);
    private static readonly Regex SteamDisconnect = new(
        @"Closing socket (\d+)", RegexOptions.Compiled);
    private static readonly Regex PlayFabConnect = new(
        @"PlayFab listen socket child connected to remote player ([A-Za-z0-9]+)",
        RegexOptions.Compiled);
    private static readonly Regex PlayFabPlatform = new(
        @"PlayFab socket with remote ID playfab/([A-Za-z0-9]+) received local Platform ID (\S+)",
        RegexOptions.Compiled);
    private static readonly Regex Character = new(
        @"Got character ZDOID from (.+?) :", RegexOptions.Compiled);
    private static readonly Regex Registration = new(
        @"Session ""[^""]+"" registered with join code (\d{6})(?!\d)",
        RegexOptions.Compiled);

    private sealed class Peer
    {
        public required string Key { get; init; }
        public bool IsPlayFab { get; init; }
        public DateTime ConnectedUtc { get; init; }
        public string? PlatformId { get; set; }
        public string? CharacterName { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Peer> _peers = new(StringComparer.OrdinalIgnoreCase);
    // A character-name event without an unambiguous peer is still a
    // useful *observed name*. Never fabricate a platform ID for it.
    private readonly Dictionary<string, DateTime> _unattributed = new(StringComparer.OrdinalIgnoreCase);
    private string? _currentJoinCode;

    public string? GetCurrentJoinCode()
    {
        lock (_gate) return _currentJoinCode;
    }

    public void OnLogLine(string line)
    {
        lock (_gate)
        {
            // A new PlayFab session replaces the old join code and identities.
            // Do not display a stale invite code during a PlayFab restart.
            if (line.Contains("New session server ", StringComparison.Ordinal))
            {
                _peers.Clear();
                _unattributed.Clear();
                _currentJoinCode = null;
                return;
            }

            var registration = Registration.Match(line);
            if (registration.Success)
            {
                _currentJoinCode = registration.Groups[1].Value;
                return;
            }

            var steam = SteamConnect.Match(line);
            if (steam.Success)
            {
                AddPeer("steam/" + steam.Groups[1].Value, isPlayFab: false);
                return;
            }

            var playfab = PlayFabConnect.Match(line);
            if (playfab.Success)
            {
                AddPeer("playfab/" + playfab.Groups[1].Value, isPlayFab: true);
                return;
            }

            var platform = PlayFabPlatform.Match(line);
            if (platform.Success)
            {
                var key = "playfab/" + platform.Groups[1].Value;
                AddPeer(key, isPlayFab: true);
                _peers[key].PlatformId = platform.Groups[2].Value;
                return;
            }

            var character = Character.Match(line);
            if (character.Success)
            {
                var name = character.Groups[1].Value.Trim();
                if (name.Length == 0) return;
                // Replayed/duplicate character events are not another player.
                if (_unattributed.ContainsKey(name)
                    || _peers.Values.Any(p => string.Equals(p.CharacterName,
                        name, StringComparison.OrdinalIgnoreCase)))
                    return;

                var candidates = _peers.Values.Where(p => p.CharacterName == null).ToArray();
                if (candidates.Length == 1)
                    candidates[0].CharacterName = name;
                else
                    _unattributed[name] = DateTime.UtcNow;
                return;
            }

            var closed = SteamDisconnect.Match(line);
            if (closed.Success)
            {
                _peers.Remove("steam/" + closed.Groups[1].Value);
                // Anonymous observations may have belonged to the socket
                // that just closed. Discard them rather than show stale names.
                _unattributed.Clear();
                return;
            }

            if (line.Contains("Player connection lost server ", StringComparison.Ordinal))
            {
                // The PlayFab loss line omits the player ID and may still say
                // "now 1 player(s)" after the sole player disconnects.
                // Since we cannot identify which connection was lost, all
                // PlayFab identities and name-only observations become unknown.
                foreach (var key in _peers.Values.Where(p => p.IsPlayFab)
                         .Select(p => p.Key).ToArray())
                    _peers.Remove(key);
                _unattributed.Clear();
            }
        }
    }

    private void AddPeer(string key, bool isPlayFab)
    {
        // Repeated socket and platform notifications must be idempotent.
        if (_peers.ContainsKey(key)) return;
        _peers[key] = new Peer
        {
            Key = key,
            IsPlayFab = isPlayFab,
            ConnectedUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Observed names only; caller MUST NOT infer an exact player count from
    /// this list when crossplay is enabled. Empty means "none observed".
    /// An empty SteamId indicates name seen but platform not established.
    /// </summary>
    public List<(string Name, string SteamId, int ConnectedSeconds)> GetKnownPlayers()
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            var namedPeers = _peers.Values
                .Where(p => !string.IsNullOrWhiteSpace(p.CharacterName))
                .Select(p => (
                    Name: p.CharacterName!,
                    SteamId: p.PlatformId ?? (p.IsPlayFab ? p.Key : p.Key["steam/".Length..]),
                    ConnectedSeconds: Math.Max(0, (int)(now - p.ConnectedUtc).TotalSeconds)));

            var namesWithoutIds = _unattributed.Select(kvp => (
                Name: kvp.Key,
                SteamId: string.Empty,
                ConnectedSeconds: Math.Max(0, (int)(now - kvp.Value).TotalSeconds)));

            return namedPeers.Concat(namesWithoutIds).ToList();
        }
    }
}
