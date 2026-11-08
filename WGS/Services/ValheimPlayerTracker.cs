using System.Text.RegularExpressions;

namespace WGS.Services;

/// <summary>
/// Observes player identities from Valheim's unmodified dedicated-server console.
///
/// Steam and PlayFab write different connection messages. In particular, PlayFab
/// exposes Xbox/PlayStation/platform identities separately from the character name.
/// We correlate these best-effort using the order of pending connection events.
/// A name is only shown after "Got character ZDOID from ..." is observed.
///
/// IMPORTANT: This is NOT an authoritative player counter. PlayFab's generic
/// "Player connection lost server" line does not identify the departing player;
/// it even sometimes says "now 1 player(s)" after the only player drops.
/// When such a line appears we discard all PlayFab observations rather than
/// report a stale or fictitiously exact player count. Callers MUST treat the
/// resulting list as partial / unverified, not evidence that the server is empty.
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

    // Console streams and WGS's 15-second refresh can run on different threads.
    // A single lock makes a connection/name/disconnect transition atomic.
    private readonly object _gate = new();
    private readonly Dictionary<string, Peer> _peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _pending = new();
    private readonly HashSet<string> _pendingSet = new(StringComparer.OrdinalIgnoreCase);

    private sealed class Peer
    {
        public required string Key { get; init; }
        public bool IsPlayFab { get; init; }
        public DateTime ConnectedUtc { get; init; }
        public string? PlatformId { get; set; }
        public string? CharacterName { get; set; }
    }

    public void OnLogLine(string line)
    {
        lock (_gate)
        {
            // Starting a new PlayFab lobby establishes a new session boundary.
            // Do not carry characters from a previous session into the new one.
            if (line.Contains("New session server ", StringComparison.Ordinal))
            {
                _peers.Clear();
                _pending.Clear();
                _pendingSet.Clear();
                return;
            }

            var steam = SteamConnect.Match(line);
            if (steam.Success)
            {
                EnqueueNewPeer("steam/" + steam.Groups[1].Value, false);
                return;
            }

            var playFab = PlayFabConnect.Match(line);
            if (playFab.Success)
            {
                EnqueueNewPeer("playfab/" + playFab.Groups[1].Value, true);
                return;
            }

            // A platform ID can arrive separately, even before a recorded
            // "child connected" message. Never count it as a named player.
            var platform = PlayFabPlatform.Match(line);
            if (platform.Success)
            {
                var key = "playfab/" + platform.Groups[1].Value;
                EnqueueNewPeer(key, true);
                _peers[key].PlatformId = platform.Groups[2].Value;
                return;
            }

            var character = Character.Match(line);
            if (character.Success)
            {
                // Character lines contain names but no connection IDs.
                // Match only the oldest still-live, unnamed connection.
                // Under simultaneous joins, the association is uncertain.
                while (_pending.Count > 0)
                {
                    var key = _pending.Dequeue();
                    _pendingSet.Remove(key);
                    if (!_peers.TryGetValue(key, out var peer) || peer.CharacterName != null)
                        continue;

                    peer.CharacterName = character.Groups[1].Value.Trim();
                    break;
                }
                return;
            }

            var closed = SteamDisconnect.Match(line);
            if (closed.Success)
            {
                _peers.Remove("steam/" + closed.Groups[1].Value);
                // Any abandoned queue items are ignored when dequeued later.
                return;
            }

            if (line.Contains("Player connection lost server ", StringComparison.Ordinal))
            {
                // This PlayFab line has no connection ID. A "now N player(s)"
                // suffix is not reliable after network errors (observed 4098).
                // Clear all PlayFab peers to avoid showing departed players as
                // definitely online; retain unrelated Steam observations.
                foreach (var key in _peers.Values.Where(p => p.IsPlayFab)
                             .Select(p => p.Key).ToArray())
                    _peers.Remove(key);
                return;
            }
        }
    }

    private void EnqueueNewPeer(string key, bool playFab)
    {
        // Duplicate log messages and repeated platform-ID notifications must
        // not create duplicate players or advance the pending-name queue.
        if (_peers.ContainsKey(key)) return;

        _peers[key] = new Peer
        {
            Key = key,
            IsPlayFab = playFab,
            ConnectedUtc = DateTime.UtcNow
        };
        if (_pendingSet.Add(key))
            _pending.Enqueue(key);
    }

    /// <summary>
    /// Returns identities whose character-name event has been observed.
    /// This list may omit unnamed connections and may miss unlogged departures.
    /// For PlayFab use, display it as "observed", never as an exact count.
    /// </summary>
    public List<(string Name, string SteamId, int ConnectedSeconds)> GetKnownPlayers()
    {
        lock (_gate)
        {
            return _peers.Values
                .Where(p => !string.IsNullOrWhiteSpace(p.CharacterName))
                .Select(p => (
                    Name: p.CharacterName!,
                    // OnlinePlayer.SteamId is also used for non-Steam identities.
                    SteamId: p.PlatformId ?? p.Key.Replace("steam/", ""),
                    ConnectedSeconds: Math.Max(0,
                        (int)(DateTime.UtcNow - p.ConnectedUtc).TotalSeconds)))
                .ToList();
        }
    }
}
