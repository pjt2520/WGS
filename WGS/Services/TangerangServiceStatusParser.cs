using System.Text.Json;
using WGS.Models;

namespace WGS.Services;

/// <summary>
/// Typed, defensive representation of the remote engine status feed.
/// Missing fields are not considered proof that a server is stopped or
/// empty. Parse errors leave the GUI's previously observed state intact.
/// </summary>
public sealed record TangerangServiceStatus(
    string Id, ServerStatus Status, int Players, bool PlayerCountUnknown);

public static class TangerangServiceStatusParser
{
    public static IReadOnlyList<TangerangServiceStatus> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new FormatException("Engine status response must be an array.");

        var result = new List<TangerangServiceStatus>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("Id", out var idEl) ||
                idEl.ValueKind != JsonValueKind.String) continue;
            var id = idEl.GetString();
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128) continue;

            if (!item.TryGetProperty("Status", out var statusEl) ||
                statusEl.ValueKind != JsonValueKind.String ||
                !Enum.TryParse<ServerStatus>(statusEl.GetString(), true, out var status) ||
                !Enum.IsDefined(status))
                continue;

            // Negative or missing counts are never displayed as confirmed 0.
            int players = 0;
            bool unknown = true;
            if (item.TryGetProperty("CurrentPlayers", out var playersEl) &&
                playersEl.ValueKind == JsonValueKind.Number &&
                playersEl.TryGetInt32(out var value) && value >= 0)
            {
                players = value;
                if (item.TryGetProperty("PlayerCountUnknown", out var unknownEl) &&
                    (unknownEl.ValueKind == JsonValueKind.True ||
                     unknownEl.ValueKind == JsonValueKind.False))
                    unknown = unknownEl.GetBoolean();
            }

            result.Add(new TangerangServiceStatus(id, status, players, unknown));
        }
        return result;
    }
}
