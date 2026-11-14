using WGS.Models;

namespace WGS.Services;

/// <summary>
/// Stateless service startup policy. The background engine may automatically
/// start only servers that owners already opted into AutoStart. Never infer
/// intent from RunningPid or from an old developer sandbox session.
/// </summary>
public static class TangerangServiceBootPolicy
{
    public static bool ShouldAutoStart(GameServer server, bool alreadyRunning)
        => server.AutoStart && !alreadyRunning
           && !string.IsNullOrWhiteSpace(server.InstallPath)
           && !string.IsNullOrWhiteSpace(server.GameId);

    /// <summary>
    /// Never run the service API on a public interface. Remote control for a
    /// detached desktop UI uses localhost and a separate bearer token.
    /// </summary>
    public const string LocalApiUrl = "http://localhost:8766";

    /// <summary>
    /// HTTP.sys may listen on a wildcard TCP socket even with a localhost
    /// URL prefix. Inspect the REAL remote network address, not the Host
    /// header, before processing any request in service-only mode.
    /// </summary>
    public static bool IsLocalClient(System.Net.IPAddress? address)
        => address is not null &&
           (System.Net.IPAddress.IsLoopback(address) ||
            (address.IsIPv4MappedToIPv6 &&
             System.Net.IPAddress.IsLoopback(address.MapToIPv4())));

    /// <summary>
    /// Service mode must be configured explicitly before the UI launches.
    /// A local GUI cannot become a second game-engine owner.
    /// </summary>
    public static bool MayLaunchLocalGui(bool serviceMode, bool serviceOwnsData)
        => serviceMode || !serviceOwnsData;
}
