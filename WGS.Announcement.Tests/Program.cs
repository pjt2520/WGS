using Newtonsoft.Json.Linq;
using WGS.Models;
using WGS.Services;

// Zero credentials, no Discord calls, no real game-server processes. These
// regression checks validate the pure authorization, formatting, anti-ping
// and countdown rules used by the Tangerang Announcement Center.
int pass = 0, fail = 0;

void Check(string name, bool result)
{
    if (result) { pass++; Console.WriteLine("PASS " + name); }
    else { fail++; Console.Error.WriteLine("FAIL " + name); }
}

const string admin = "123456789012345678";
const string secondAdmin = "234567890123456789";
const string outsider = "345678901234567890";

Check("Empty admin list grants nobody access",
    !TangerangAnnouncementFormatter.IsAuthorizedAdmin("", admin));
Check("Missing user ID denied",
    !TangerangAnnouncementFormatter.IsAuthorizedAdmin(admin, ""));
Check("Exact administrator accepted",
    TangerangAnnouncementFormatter.IsAuthorizedAdmin(admin, admin));
Check("Multiple admins supported with spaces",
    TangerangAnnouncementFormatter.IsAuthorizedAdmin($" {admin} , {secondAdmin} ", secondAdmin));
Check("Unlisted user denied",
    !TangerangAnnouncementFormatter.IsAuthorizedAdmin(admin, outsider));
Check("Partial ID cannot match",
    !TangerangAnnouncementFormatter.IsAuthorizedAdmin(admin, "1234567890"));

var one = new GameServer { DisplayName = "Tangerang Test" };
Check("Announcements opt-in defaults off",
    !TangerangAnnouncementFormatter.MayAnnounce(one));
one.DiscordAnnouncementsEnabled = true;
Check("Explicit announcements opt-in recognized",
    TangerangAnnouncementFormatter.MayAnnounce(one));

Check("Manual announcement parses multiword server name",
    TangerangAnnouncementFormatter.TryParseAnnouncement(
        "Tangerang Test | The servers restart tonight",
        out var serverName, out var body)
    && serverName == "Tangerang Test"
    && body == "The servers restart tonight");
Check("Manual message keeps newline from breaking formatting",
    TangerangAnnouncementFormatter.TryParseAnnouncement(
        "Tangerang Test | Hello\nWorld", out _, out body)
    && body == "Hello World");
Check("Missing pipe is rejected",
    !TangerangAnnouncementFormatter.TryParseAnnouncement(
        "Tangerang Test message", out _, out _));
Check("Empty announcement is rejected",
    !TangerangAnnouncementFormatter.TryParseAnnouncement(
        "Tangerang Test | \r\n", out _, out _));
Check("Oversized announcement is rejected",
    !TangerangAnnouncementFormatter.TryParseAnnouncement(
        "Tangerang Test | " + new string('A', 501), out _, out _));
Check("Message of 500 characters accepted",
    TangerangAnnouncementFormatter.TryParseAnnouncement(
        "Tangerang Test | " + new string('A', 500), out _, out _));

Check("Warned restart defaults to configured duration",
    TangerangAnnouncementFormatter.TryParseRestart(
        "Tangerang Test", 90, out serverName, out var seconds)
    && serverName == "Tangerang Test" && seconds == 90);
Check("Warned restart accepts explicit 30s minimum",
    TangerangAnnouncementFormatter.TryParseRestart(
        "Tangerang Test | 30", 60, out _, out seconds) && seconds == 30);
Check("Warned restart accepts explicit 600s maximum",
    TangerangAnnouncementFormatter.TryParseRestart(
        "Tangerang Test | 600", 60, out _, out seconds) && seconds == 600);
Check("Warned restart rejects too short",
    !TangerangAnnouncementFormatter.TryParseRestart(
        "Tangerang Test | 10", 60, out _, out _));
Check("Warned restart rejects too long",
    !TangerangAnnouncementFormatter.TryParseRestart(
        "Tangerang Test | 601", 60, out _, out _));
Check("Warned restart rejects nonnumeric value",
    !TangerangAnnouncementFormatter.TryParseRestart(
        "Tangerang Test | soon", 60, out _, out _));
Check("30-second restart schedules 30 and 10 warnings",
    TangerangAnnouncementFormatter.RestartCheckpoints(30).SequenceEqual([30, 10]));
Check("60-second restart schedules 60 and 10 warnings",
    TangerangAnnouncementFormatter.RestartCheckpoints(60).SequenceEqual([60, 10]));
Check("120-second restart includes final minute",
    TangerangAnnouncementFormatter.RestartCheckpoints(120).SequenceEqual([120, 60, 10]));
Check("Invalid restart yields no countdown",
    TangerangAnnouncementFormatter.RestartCheckpoints(10).Length == 0);
Check("One minute warning says one minute",
    TangerangAnnouncementFormatter.RestartWarning(one, 60).Contains("1 minute"));
Check("Restart warning contains no dangerous actual server command",
    !TangerangAnnouncementFormatter.RestartWarning(one, 60).Contains("!restart"));

Check("Lifecycle start message formatted",
    TangerangAnnouncementFormatter.LifecycleMessage(one, ServerStatus.Running).Contains("online"));
Check("Lifecycle stop message formatted",
    TangerangAnnouncementFormatter.LifecycleMessage(one, ServerStatus.Stopped).Contains("offline"));
Check("Lifecycle errors differentiated",
    TangerangAnnouncementFormatter.LifecycleMessage(one, ServerStatus.Error).Contains("error"));
Check("Intermediate lifecycle state does not announce",
    TangerangAnnouncementFormatter.LifecycleMessage(one, ServerStatus.Starting) == "");

const string status = "456789012345678901";
const string control = "567890123456789012";
Check("Dedicated announcement channel accepted",
    TangerangAnnouncementFormatter.ResolveAnnouncementChannel(status, "", control) == status);
Check("Bot status channel used when server override blank",
    TangerangAnnouncementFormatter.ResolveAnnouncementChannel("", status, control) == status);
Check("Missing announcement channel rejected",
    TangerangAnnouncementFormatter.ResolveAnnouncementChannel("", "", control) == null);
Check("Private control channel rejected",
    TangerangAnnouncementFormatter.ResolveAnnouncementChannel(control, status, control) == null);
Check("Status channel equal to admin channel rejected",
    TangerangAnnouncementFormatter.ResolveAnnouncementChannel("", control, control) == null);
Check("Non-numeric channel rejected",
    TangerangAnnouncementFormatter.ResolveAnnouncementChannel("server-status", status, control) == null);
Check("Short malformed channel ID rejected",
    TangerangAnnouncementFormatter.ResolveAnnouncementChannel("1234", status, control) == null);

// Discord API takes explicit allowed_mentions=[] so even user-supplied
// @everyone and role names cannot cause pings in a public channel.
var payload = JObject.FromObject(TangerangAnnouncementFormatter.DiscordPayload(
    "@everyone maintenance is starting <@&123456789012345678>"));
Check("Safe payload contains intended text",
    payload["content"]?.ToString().Contains("maintenance") == true);
Check("Safe payload has an empty mention parse array",
    payload["allowed_mentions"]?["parse"] is JArray parsed && parsed.Count == 0);

Console.WriteLine($"Results: {pass} passed, {fail} failed.");
return fail == 0 ? 0 : 1;
