using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WGS.Models;

namespace WGS.Services;

public sealed record PalworldHealth(bool Healthy, string Detail, int? Players = null, double? Fps = null);

/// <summary>Per-request results: a shared plugin's last error must never decide another world's health.</summary>
public static class PalworldControl
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(30) };

    public static int Port(GameServer server)
    {
        var text = server.GameSpecificSettings.GetValueOrDefault("restApiPort", "21512");
        if (!int.TryParse(text, out var port) || port < 1 || port > 65535)
            throw new InvalidOperationException("Palworld REST API port must be between 1 and 65535.");
        return port;
    }

    public static async Task<string> RequestAsync(GameServer server, string endpoint, object? body = null)
    {
        var password = server.GameSpecificSettings.GetValueOrDefault("adminPassword", "");
        if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("Palworld admin password is required for REST control.");
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post,
            $"http://127.0.0.1:{Port(server)}/v1/api/{endpoint}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:" + password)));
        if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Palworld {endpoint}: HTTP {(int)response.StatusCode}. Check REST configuration and admin credentials.");
        return await response.Content.ReadAsStringAsync();
    }

    public static PalworldHealth ParseMetrics(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            double Number(string key, bool integer = false)
            {
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(key, out var value)
                    || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number)
                    || !double.IsFinite(number) || number < 0 || (integer && number != Math.Truncate(number)))
                    throw new FormatException("Missing or invalid " + key);
                return number;
            }
            var players = Number("currentplayernum", true);
            var maximum = Number("maxplayernum", true);
            var fps = Number("serverfps");
            _ = Number("uptime", true); _ = Number("days", true);
            if (maximum < 1 || players > maximum || players > int.MaxValue) throw new FormatException("Invalid player capacity/count");
            if (fps <= 0) return new(false, "Degraded: server FPS is zero; occupancy is not trusted.");
            return new(true, "Usable game metrics", (int)players, fps);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        { return new(false, "Incomplete metrics: " + ex.Message); }
    }

    public static async Task<PalworldHealth> GetHealthAsync(GameServer server)
    {
        try { return ParseMetrics(await RequestAsync(server, "metrics")); }
        catch (Exception ex) { return new(false, "REST unavailable: " + ex.Message); }
    }

    public static async Task<(List<OnlinePlayer> Players, string? Error)> GetPlayersAsync(GameServer server)
    {
        var health = await GetHealthAsync(server);
        if (!health.Healthy) return ([], health.Detail);
        try
        {
            using var doc = JsonDocument.Parse(await RequestAsync(server, "players"));
            if (!doc.RootElement.TryGetProperty("players", out var array) || array.ValueKind != JsonValueKind.Array)
                return ([], "Incomplete player response; occupancy unknown.");
            var players = new List<OnlinePlayer>();
            foreach (var player in array.EnumerateArray())
            {
                var ping = player.TryGetProperty("ping", out var p) && p.ValueKind == JsonValueKind.Number
                    && p.TryGetDouble(out var n) && double.IsFinite(n) && n >= 0 && n <= int.MaxValue ? (int)n : 0;
                players.Add(new OnlinePlayer {
                    Name = player.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    SteamId = player.TryGetProperty("userId", out var id) ? id.GetString() ?? "" : "", Ping = ping });
            }
            // Two requests can straddle a join/leave. Retry next poll instead of calling this verified empty.
            return players.Count == health.Players ? (players, null) : (players, "Player list and metrics disagree; occupancy unknown.");
        }
        catch (Exception ex) { return ([], "Player query failed: " + ex.Message); }
    }

    public static async Task SaveAndShutdownAsync(Func<string, object, Task> post, Func<Task> waitForExit, Action<string> log)
    {
        await post("save", new { });
        log("Palworld acknowledged the save request; live save/reload verification is still required.");
        await post("shutdown", new { waittime = 10, message = "Server shutting down. Please reconnect after maintenance." });
        log("Palworld accepted shutdown; waiting for process exit.");
        await waitForExit();
    }

    public static Task StopAsync(GameServer server, Process process, Action<string> log)
        => SaveAndShutdownAsync(async (endpoint, body) => { await RequestAsync(server, endpoint, body); },
            () => process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(120)), log);
}
