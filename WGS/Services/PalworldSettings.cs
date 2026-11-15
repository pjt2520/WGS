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
        var original = line.Groups[1].Value;
        var parts = new List<string>();
        int start = 0, depth = 0;
        bool quoted = false, escaped = false;
        for (int i = 0; i < original.Length; i++)
        {
            var c = original[i];
            if (quoted && c == '\\') { escaped = !escaped; continue; }
            if (c == '"' && !escaped) quoted = !quoted;
            escaped = false;
            if (quoted) continue;
            if (c == '(') depth++;
            if (c == ')' && --depth < 0) throw new InvalidOperationException("Unbalanced Palworld configuration.");
            if (c == ',' && depth == 0) { parts.Add(original[start..i]); start = i + 1; }
        }
        if (quoted || depth != 0) throw new InvalidOperationException("Unbalanced Palworld configuration.");
        parts.Add(original[start..]);
        foreach (var (key, value) in values)
        {
            var indices = parts.Select((part, index) => (part, index))
                .Where(x => x.part.Split('=', 2)[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.index).ToList();
            if (indices.Count > 1) throw new InvalidOperationException("Duplicate managed Palworld option: " + key);
            if (indices.Count == 1) parts[indices[0]] = key + "=" + value;
            else parts.Add(key + "=" + value);
        }
        var options = string.Join(",", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
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
