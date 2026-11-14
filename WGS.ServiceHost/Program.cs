using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WGS.ServiceHost;
using WGS.Services;

// The Windows Service Control Manager does not inherit per-user environment
// variables from Start-Tangerang.ps1. A published service must receive its
// own --tangerang-home argument, before ConfigService is constructed.
//
// Refuse to run against an executable-adjacent WGS_Data by accident, or in
// the disposable developer sandbox. This host is now ONLY an explicit owner
// of the durable managed installation.
var homeFlag = Array.FindIndex(args, a =>
    string.Equals(a, "--tangerang-home", StringComparison.OrdinalIgnoreCase));
if (homeFlag < 0 || homeFlag + 1 >= args.Length)
    throw new ArgumentException(
        "ServiceHost requires --tangerang-home <absolute managed installation directory>.");

var managedHome = TangerangManagedPaths.ResolveHome(args[homeFlag + 1], isDev: false)
    ?? throw new ArgumentException("Managed home is required.");
if (!Directory.Exists(Path.Combine(managedHome, "Data")) ||
    !Directory.Exists(Path.Combine(managedHome, "Servers")))
    throw new DirectoryNotFoundException(
        "Managed Data/Servers not found. Install Tangerang WGS before enabling the service.");

Environment.SetEnvironmentVariable("TANGERANG_WGS_DEV", null);
Environment.SetEnvironmentVariable("TANGERANG_WGS_HOME", managedHome);

// Do not forward private paths as host option arguments.
var hostArgs = args.Where((_, i) => i != homeFlag && i != homeFlag + 1).ToArray();
var builder = Host.CreateApplicationBuilder(hostArgs);
builder.Services.AddWindowsService(options => options.ServiceName = "Tangerang WGS Engine");
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
