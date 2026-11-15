using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using WGS.Models;
using WGS.Services;

internal static class PalworldChecks
{
    public static async Task RunAsync()
    {
        int passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); passed++; }
        const string valid = "{\"currentplayernum\":0,\"maxplayernum\":12,\"serverfps\":58,\"uptime\":100,\"days\":3}";
        Check(PalworldControl.ParseMetrics(valid).Healthy, "Palworld verified empty world accepted");
        Check(!PalworldControl.ParseMetrics("{\"maxplayernum\":12,\"uptime\":63245}").Healthy, "Nov 14 incomplete metrics rejected");
        foreach (var field in new[] { "currentplayernum", "maxplayernum", "serverfps", "uptime", "days" })
        {
            var missing = JsonNode.Parse(valid)!.AsObject(); missing.Remove(field);
            Check(!PalworldControl.ParseMetrics(missing.ToJsonString()).Healthy, "missing " + field + " rejected");
            missing[field] = null;
            Check(!PalworldControl.ParseMetrics(missing.ToJsonString()).Healthy, "null " + field + " rejected");
        }
        foreach (var value in new[] { "-1", "0.5", "false", "\"0\"", "13" })
        {
            var invalid = JsonNode.Parse(valid)!.AsObject(); invalid["currentplayernum"] = JsonNode.Parse(value);
            Check(!PalworldControl.ParseMetrics(invalid.ToJsonString()).Healthy, "invalid Palworld player count " + value);
        }
        Check(!PalworldControl.ParseMetrics(valid.Replace("58", "0")).Healthy, "zero FPS does not authorize empty maintenance");
        var server = new GameServer { GameId = "palworld", CurrentPlayers = 0, PlayerCountUnknown = false };
        Check(!server.HasFreshPlayerCount, "unobserved empty count is not trusted");
        server.PlayerCountVerifiedAtUtc = DateTime.UtcNow;
        Check(server.HasFreshPlayerCount, "recent verified occupancy accepted");
        server.PlayerCountVerifiedAtUtc = DateTime.UtcNow.AddMinutes(-2);
        Check(!server.HasFreshPlayerCount, "stale empty count expires");

        var calls = new List<string>();
        await PalworldControl.SaveAndShutdownAsync((endpoint, body) => {
            calls.Add(endpoint);
            if (endpoint == "shutdown") Check(System.Text.Json.JsonSerializer.Serialize(body).Contains("\"message\""), "shutdown uses documented lowercase message");
            return Task.CompletedTask;
        }, () => { calls.Add("exit"); return Task.CompletedTask; }, _ => { });
        Check(string.Join(",", calls) == "save,shutdown,exit", "save precedes shutdown and verified exit");
        foreach (var failedStage in new[] { "save", "shutdown", "exit" })
        {
            calls.Clear(); bool failed = false;
            try
            {
                await PalworldControl.SaveAndShutdownAsync((endpoint, body) => {
                    calls.Add(endpoint);
                    if (endpoint == failedStage) throw new IOException("synthetic transport failure");
                    return Task.CompletedTask;
                }, () => { calls.Add("exit"); throw new TimeoutException("synthetic timeout"); }, _ => { });
            }
            catch (Exception ex) when (ex is IOException or TimeoutException) { failed = true; }
            Check(failed && calls.Last() == failedStage && !calls.Contains("stop"), failedStage + " failure propagates without force stop");
        }
        var ini = "[/Script/Pal.PalGameWorldSettings]\nOptionSettings=(PalSpawnNumRate=1.5,CrossplayPlatforms=(Steam,Xbox,PS5),AdminPassword=\"old\",RESTAPIEnabled=False,RESTAPIPort=8212,ServerName=\"Old\")\n";
        var changed = PalworldSettings.Apply(ini, new Dictionary<string,string> { ["RESTAPIEnabled"]="True", ["AdminPassword"]="\"new\"" });
        Check(changed.Contains("PalSpawnNumRate=1.5") && changed.Contains("CrossplayPlatforms=(Steam,Xbox,PS5)"), "Palworld tuning and nested platform list preserved");
        Check(changed.Contains("AdminPassword=\"new\"") && changed.Contains("RESTAPIEnabled=True"), "managed REST settings updated");
        bool badConfig = false;
        try { PalworldSettings.Apply("invalid", new Dictionary<string,string>()); } catch (InvalidOperationException) { badConfig = true; }
        Check(badConfig, "malformed configuration refused");
        var status = TangerangServiceStatusParser.Parse("[{\"Id\":\"x\",\"Status\":\"Running\",\"Uptime\":\"01:02:03\"}]")[0];
        Check(status.Uptime == "01:02:03" && status.PlayerCountUnknown, "service uptime does not fabricate occupancy");

        // Real loopback HTTP fake: auth failure, invalid body and valid players exercise the production client.
        using var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        using var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        server.GameSpecificSettings["restApiPort"] = port.ToString(); server.GameSpecificSettings["adminPassword"] = "fixture";
        async Task Reply(string body, int statusCode = 200)
        {
            var ctx = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check(ctx.Request.Headers["Authorization"] == "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:fixture")), "REST authenticates per request");
            ctx.Response.StatusCode = statusCode;
            var bytes = Encoding.UTF8.GetBytes(body); ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes); ctx.Response.Close();
        }
        var query = PalworldControl.GetHealthAsync(server); await Reply("unauthorized", 401);
        Check(!(await query).Healthy, "HTTP 401 never healthy");
        var playersTask = PalworldControl.GetPlayersAsync(server);
        await Reply(valid); await Reply("{}");
        Check((await playersTask).Error != null, "missing players array never verified empty");
        playersTask = PalworldControl.GetPlayersAsync(server); await Reply(valid); await Reply("{\"players\":[]}");
        Check((await playersTask).Error == null, "validated metrics plus empty list accepted");
        listener.Stop();
        Console.WriteLine($"{passed} Palworld/service checks passed; only a synthetic loopback API was accessed.");
    }
}
