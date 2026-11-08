using WGS.Services;

// Tangerang's source-only regression runner. It never connects to a real
// server or touches save files. All platform identities below are fabricated.
//
// A positive result proves event parsing behaves as expected. It does not
// prove log observations are a complete, authoritative player census.
var failures = 0;
var checks = 0;

void Expect(string scenario, bool condition)
{
    checks++;
    if (condition) Console.WriteLine("PASS " + scenario);
    else { Console.Error.WriteLine("FAIL " + scenario); failures++; }
}

var xbox = new ValheimPlayerTracker();
xbox.OnLogLine("11/08/2026 13:47:02: New session server \"Tangerang\" that has join code , now 0 player(s)");
Expect("New lobby does not claim an observed player", xbox.GetKnownPlayers().Count == 0);

// This sequence is patterned after an actual Valheim 1.0.21 console log.
// The identifiers are synthetic to avoid putting player IDs in the repository.
xbox.OnLogLine("11/08/2026 13:57:31: PlayFab listen socket child connected to remote player AABB1122");
xbox.OnLogLine("11/08/2026 13:57:31: Player joined server \"Tangerang\" that has join code 123456, now 1 player(s)");
xbox.OnLogLine("11/08/2026 13:57:31: PlayFab socket with remote ID playfab/AABB1122 received local Platform ID Xbox_111222333");
Expect("Handshake and lobby count alone do not count a named player", xbox.GetKnownPlayers().Count == 0);
xbox.OnLogLine("11/08/2026 13:57:31: Got handshake from client playfab/AABB1122");
xbox.OnLogLine("11/08/2026 13:57:40: Got character ZDOID from Viking One : 12345:9");
Expect("Xbox name and platform identity captured",
    xbox.GetKnownPlayers() is [var x]
    && x.Name == "Viking One"
    && x.SteamId == "Xbox_111222333"
    && x.ConnectedSeconds >= 0);

// Repeated platform-ID events must not double-count a peer.
xbox.OnLogLine("PlayFab socket with remote ID playfab/AABB1122 received local Platform ID Xbox_111222333");
xbox.OnLogLine("PlayFab listen socket child connected to remote player AABB1122");
Expect("Duplicate PlayFab messages produce one observed player", xbox.GetKnownPlayers().Count == 1);

// This line can still claim 1 player after the sole peer disconnects.
// We must not trust that suffix or keep a stale identity.
xbox.OnLogLine("11/08/2026 13:57:40: Player connection lost server \"Tangerang\" that has join code 123456, now 1 player(s)");
Expect("ID-less PlayFab disconnect clears uncertain observations", xbox.GetKnownPlayers().Count == 0);

// The same remote ID can be reused during recovery. Re-queue cleanly.
xbox.OnLogLine("PlayFab listen socket child connected to remote player AABB1122");
xbox.OnLogLine("PlayFab socket with remote ID playfab/AABB1122 received local Platform ID Xbox_111222333");
xbox.OnLogLine("Got character ZDOID from Viking One : 12345:10");
Expect("Reconnect re-assigns one name without stale queue entries",
    xbox.GetKnownPlayers() is [var re] && re.Name == "Viking One");

// The parser deliberately does NOT hardcode Xbox prefixes. Actual
// PlayStation platform IDs must still be confirmed using a PS5 session log.
var ps5 = new ValheimPlayerTracker();
ps5.OnLogLine("PlayFab listen socket child connected to remote player CC445566");
ps5.OnLogLine("PlayFab socket with remote ID playfab/CC445566 received local Platform ID PSN_example123");
ps5.OnLogLine("Got character ZDOID from Shield Bearer : 888:2");
Expect("Generic platform identifier works for synthetic PS5-shaped event",
    ps5.GetKnownPlayers() is [var p] && p.Name == "Shield Bearer"
    && p.SteamId == "PSN_example123");

var steam = new ValheimPlayerTracker();
steam.OnLogLine("Got connection SteamID 76561198000000000");
steam.OnLogLine("Got character ZDOID from Old Viking : 12345:2");
Expect("Legacy Steam event still resolves player",
    steam.GetKnownPlayers() is [var s]
    && s.Name == "Old Viking"
    && s.SteamId == "76561198000000000");
steam.OnLogLine("Closing socket 76561198000000000");
Expect("Legacy Steam disconnect clears identity", steam.GetKnownPlayers().Count == 0);

// Two peers with a positional name association produce two observations,
// but the caller must still mark PlayFab's total player count UNKNOWN.
var multiple = new ValheimPlayerTracker();
multiple.OnLogLine("PlayFab listen socket child connected to remote player AA01");
multiple.OnLogLine("PlayFab listen socket child connected to remote player BB02");
multiple.OnLogLine("Got character ZDOID from First : 1:1");
multiple.OnLogLine("Got character ZDOID from Second : 2:2");
Expect("Two pending players yield two observations", multiple.GetKnownPlayers().Count == 2);
multiple.OnLogLine("Player connection lost server \"Tangerang\" that has join code 123456, now 2 player(s)");
Expect("Ambiguous disconnect avoids claiming either identity remains online",
    multiple.GetKnownPlayers().Count == 0);

xbox.OnLogLine("New session server \"Tangerang\" that has join code , now 0 player(s)");
Expect("New lobby resets identities from previous session", xbox.GetKnownPlayers().Count == 0);

Console.WriteLine($"Results: {checks - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;
