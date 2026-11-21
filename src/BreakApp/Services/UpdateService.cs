using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using BreakApp.Core.Updates;
using BreakApp.Notifications;

namespace BreakApp.Services;

/// <summary>
/// Checks the latest GitHub release on start (after 30 s) and once a day. When a newer version exists, a card offers
/// "Update now": download, check it's a real exe, swap it in place (the running exe is renamed to .old), restart.
/// Nothing installs without a click.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private static readonly HttpClient Http = CreateClient();
    private readonly BreakCoordinator _c;
    private readonly Action _quit;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private string? _notifiedVersion;
    private bool _installing;

    public ReleaseInfo? Available { get; private set; }
    public event Action? Changed;

    public static string CurrentVersion => typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public UpdateService(BreakCoordinator c, Action quit)
    {
        _c = c;
        _quit = quit;
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = TimeSpan.FromHours(24);
            if (_c.Settings.General.CheckForUpdates) await CheckAsync(userInitiated: false);
        };
        _timer.Start();
    }

    private static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("BreakApp-Updater"); // generic client name, nothing personal
        h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return h;
    }

    /// <summary>Returns the latest release (null when the check failed). Shows the update card when it's newer.</summary>
    public async Task<ReleaseInfo?> CheckAsync(bool userInitiated)
    {
        ReleaseInfo? latest;
        try { latest = ReleaseInfo.Parse(await Http.GetStringAsync(ReleaseInfo.LatestApi)); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            App.Log("update check failed: " + e.Message);
            if (userInitiated) _c.Info("Couldn't check for updates", "No connection to GitHub right now. Try again later.", NotificationKind.Warning);
            return null;
        }
        if (latest is null)
        {
            if (userInitiated) _c.Info("Couldn't check for updates", "GitHub didn't send a usable release.", NotificationKind.Warning);
            return null;
        }
        if (ReleaseInfo.IsNewer(latest.Version, CurrentVersion))
        {
            Available = latest;
            Changed?.Invoke();
            if (userInitiated || _notifiedVersion != latest.Version) ShowCard(latest);
            _notifiedVersion = latest.Version;
        }
        else
        {
            Available = null;
            Changed?.Invoke();
            if (userInitiated) _c.Info("You're up to date", $"Break App {CurrentVersion} is the latest version.", NotificationKind.Success);
        }
        return latest;
    }

    public void ShowCard(ReleaseInfo r) =>
        _c.Notifications.Show(new NotificationRequest("update", NotificationKind.Info, $"Break App {r.Version} is out",
                $"You have {CurrentVersion}. Updating takes a few seconds and restarts the app; your settings stay.",
                [new("now", "Update now", Primary: true), new("notes", "What's new"), new("later", "Later")], TimeSpan.Zero),
            id =>
            {
                if (id == "now") _ = InstallAsync(r);
                else if (id == "notes" && r.PageUrl.Length > 0)
                    Process.Start(new ProcessStartInfo(r.PageUrl) { UseShellExecute = true });
            });

    public async Task InstallAsync(ReleaseInfo r)
    {
        if (_installing) return;
        _installing = true;
        var exe = Environment.ProcessPath;
        try
        {
            if (exe is null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Can't find the running exe.");
            _c.Info("Downloading update…", $"Break App {r.Version}");
            var dir = Path.Combine(Path.GetTempPath(), "BreakApp-update");
            Directory.CreateDirectory(dir);
            var download = Path.Combine(dir, $"BreakApp-{r.Version}.exe");
            var bytes = await Http.GetByteArrayAsync(r.ExeUrl);
            if (bytes.Length < 100_000 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z' || (r.ExeSize > 0 && bytes.Length != r.ExeSize))
                throw new InvalidDataException("The download doesn't look like Break App. Nothing was changed.");
            await File.WriteAllBytesAsync(download, bytes);

            // A running exe can't be overwritten, but it can be renamed: move it aside, put the new one in its place.
            var old = exe + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try { File.Move(download, exe); }
            catch { File.Move(old, exe); throw; }

            App.Log($"updated {CurrentVersion} -> {r.Version}, restarting");
            Process.Start(new ProcessStartInfo(exe, $"--updated-from {CurrentVersion}") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
            _quit();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or InvalidDataException or InvalidOperationException)
        {
            _installing = false;
            App.Log("update failed: " + e);
            _c.Info("Update didn't work", e.Message + " You can also download it from GitHub › Releases.", NotificationKind.Error);
        }
    }

    /// <summary>Called by the new version on its first start: tidy up and say what happened.</summary>
    public static void FinishUpdate(BreakCoordinator c, string? from)
    {
        var old = (Environment.ProcessPath ?? "") + ".old";
        for (var i = 0; i < 10 && File.Exists(old); i++)
        {
            try { File.Delete(old); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Thread.Sleep(300); }
        }
        c.Info($"Updated to {CurrentVersion}", from is null ? "Break App is up to date." : $"Was {from}. Your settings are unchanged.", NotificationKind.Success);
    }

    public void Dispose() => _timer.Stop();
}
