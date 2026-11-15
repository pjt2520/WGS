using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using WGS.Models;

namespace WGS.Services;

/// <summary>Change only managed connection settings; preserve gameplay tuning and unknown options.</summary>
public static class PalworldSettings
{
    public static string Apply(string text, IReadOnlyDictionary<string, string> values)
    {
        var line = Regex.Match(text, @"(?m)^\s*OptionSettings\s*=\s*\(([^\r\n]*)\)[ \t]*\r?$", RegexOptions.IgnoreCase);
        if (!line.Success) throw new InvalidOperationException("Palworld configuration has no single-line OptionSettings block. No settings changed.");
        var options = line.Groups[1].Value;
        foreach (var (key, value) in values)
        {
            var expression = new Regex(@"(^|,)\s*" + Regex.Escape(key) + @"\s*=\s*(?:""[^""]*""|[^,()]*)", RegexOptions.IgnoreCase);
            if (expression.IsMatch(options)) options = expression.Replace(options, m => m.Groups[1].Value + key + "=" + value);
            else options += (options.Length == 0 ? "" : ",") + key + "=" + value;
        }
        return text[..line.Groups[1].Index] + options + text[(line.Groups[1].Index + line.Groups[1].Length)..];
    }

    public static void Prepare(GameServer server)
    {
        string Quote(string value)
        {
            if (value.IndexOfAny(['"', '\r', '\n', '\\']) >= 0)
                throw new InvalidOperationException("Palworld managed text settings cannot contain quotes, backslashes or newlines.");
            return "\"" + value + "\"";
        }
        var path = Path.Combine(server.InstallPath, "Pal", "Saved", "Config", "WindowsServer", "PalWorldSettings.ini");
        var source = File.Exists(path) ? path : Path.Combine(server.InstallPath, "DefaultPalWorldSettings.ini");
        if (!File.Exists(source)) throw new InvalidOperationException("Palworld default/settings INI missing. Install/validate the game before starting.");
        var original = File.ReadAllText(source);
        var changes = new Dictionary<string, string> {
            ["RESTAPIEnabled"] = "True", ["RESTAPIPort"] = PalworldControl.Port(server).ToString(),
            ["AdminPassword"] = Quote(server.GameSpecificSettings.GetValueOrDefault("adminPassword", "")),
            ["ServerName"] = Quote(string.IsNullOrWhiteSpace(server.ServerName) ? server.DisplayName : server.ServerName),
            ["ServerPassword"] = Quote(server.ServerPassword), ["PublicPort"] = server.ServerPort.ToString(),
            ["ServerPlayerMaxNum"] = server.MaxPlayers.ToString()
        };
        var updated = Apply(original, changes);
        if (File.Exists(path) && original == updated) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var staging = path + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(staging, updated, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(staging, path, path + ".before-wgs-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N") + ".bak");
            else File.Move(staging, path);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
}
