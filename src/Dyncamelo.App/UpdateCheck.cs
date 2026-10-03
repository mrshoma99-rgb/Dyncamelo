using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Dyncamelo.App;

/// <summary>
/// Once-a-day, fire-and-forget check of the newest GitHub release when the editor pane opens.
/// If it is newer than this build, offers (once per version) to open the download page.
/// Every failure path — offline, proxy, rate limit, bad JSON — is silent: the editor must never
/// be slowed down or degraded by the check. Mirrors the BIMCamel IFC Exporter update check so all
/// BIMCamel tools behave the same.
/// </summary>
internal static class UpdateCheck
{
    private const string Owner = "mrshoma99-rgb";
    private const string Repo = "dyncamelo";
    private const string ProductName = "Dyncamelo";
    internal const string DownloadPage = "https://github.com/" + Owner + "/" + Repo + "/releases/latest";

    // %APPDATA%\Dyncamelo\update-check.txt: "<last check yyyy-MM-dd>|<last version offered>|<newest version seen>"
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Dyncamelo", "update-check.txt");

    private static bool _ranThisSession;
    private static Version? _knownLatest;

    /// <summary>
    /// Kicks off the background check. <paramref name="onUiThread"/> marshals the prompt back
    /// to the UI thread (the caller passes its Dispatcher's Invoke). <paramref name="onUpdateKnown"/> is told, on the UI thread, about
    /// a newer release whenever one is known — also on the days the check does not ask GitHub again, and for a pane opened again in
    /// the same session — so the start screen can show it without a prompt.
    /// </summary>
    public static void Run(Action<Action> onUiThread, Func<bool>? enabled = null, Action<Version, string>? onUpdateKnown = null)
    {
        // Switched off in Settings > Privacy: no request at all (and the session's one chance is kept for when it is switched on).
        if (enabled != null && !enabled())
        {
            return;
        }

        if (_ranThisSession)
        {
            var known = _knownLatest;
            if (known != null && onUpdateKnown != null)
            {
                onUiThread(() => onUpdateKnown(known, DownloadPage));
            }

            return;
        }

        _ranThisSession = true;

        Task.Run(() =>
        {
            try
            {
                var current = Assembly.GetExecutingAssembly().GetName().Version;
                if (current == null || (current.Major == 0 && current.Minor == 0))
                {
                    return; // dev build
                }

                var currentThree = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
                var (lastCheck, lastOffered, lastLatest) = ReadState();
                var checkedToday = lastCheck == DateTime.UtcNow.Date;
                Version? latest;
                if (checkedToday)
                {
                    latest = lastLatest; // at most one request per day: today's answer was kept
                }
                else
                {
                    latest = FetchLatestVersion();
                    WriteState(DateTime.UtcNow.Date, lastOffered, latest ?? lastLatest);
                    latest = latest ?? lastLatest;
                }

                if (latest == null || latest <= currentThree)
                {
                    return;
                }

                var newer = latest;
                _knownLatest = newer;
                if (onUpdateKnown != null)
                {
                    onUiThread(() => onUpdateKnown(newer, DownloadPage));
                }

                if (checkedToday || (lastOffered != null && newer <= lastOffered))
                {
                    return; // asked about it already (or declined it): the start screen shows it, no second prompt
                }

                WriteState(DateTime.UtcNow.Date, newer, newer);
                onUiThread(() => Prompt(currentThree, newer));
            }
            catch
            {
                // Never surface anything from the updater.
            }
        });
    }

    private static void Prompt(Version current, Version latest)
    {
        var pick = System.Windows.Forms.MessageBox.Show(
            ProductName + " " + latest + " is available (you have " + current + ").\n\n" +
            "Open the download page to get the update?",
            ProductName + " — update available",
            System.Windows.Forms.MessageBoxButtons.YesNo,
            System.Windows.Forms.MessageBoxIcon.Information);
        if (pick == System.Windows.Forms.DialogResult.Yes)
        {
            try
            {
                Process.Start(new ProcessStartInfo(DownloadPage) { UseShellExecute = true });
            }
            catch
            {
                // Browser launch failed; nothing useful to do.
            }
        }
    }

    /// <summary>Returns the latest release tag as a Version, or null when unavailable.</summary>
    private static Version? FetchLatestVersion()
    {
        // .NET Framework 4.8 on older Windows/policies may not negotiate TLS 1.2 by default,
        // and api.github.com requires it.
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

        var request = (HttpWebRequest)WebRequest.Create(
            "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest");
        request.UserAgent = "Dyncamelo-UpdateCheck";
        request.Accept = "application/vnd.github+json";
        request.Timeout = 10000;
        request.ReadWriteTimeout = 10000;

        string body;
        using (var response = (HttpWebResponse)request.GetResponse())
        using (var reader = new StreamReader(response.GetResponseStream()!))
        {
            body = reader.ReadToEnd();
        }

        // One string field is all we need — a regex keeps this free of JSON dependencies.
        var m = Regex.Match(body, "\"tag_name\"\\s*:\\s*\"v?([0-9]+\\.[0-9]+\\.[0-9]+)\"");
        return m.Success && Version.TryParse(m.Groups[1].Value, out var v) ? v : null;
    }

    // ------------------------------------------------------------- state

    private static (DateTime? lastCheck, Version? lastOffered, Version? lastLatest) ReadState()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                return (null, null, null);
            }

            var parts = File.ReadAllText(StatePath).Split('|');
            DateTime? day = DateTime.TryParseExact(parts[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d.Date : (DateTime?)null;
            Version? offered = parts.Length > 1 && Version.TryParse(parts[1].Trim(), out var v) ? v : null;
            Version? latest = parts.Length > 2 && Version.TryParse(parts[2].Trim(), out var l) ? l : null;
            return (day, offered, latest);
        }
        catch
        {
            return (null, null, null);
        }
    }

    private static void WriteState(DateTime day, Version? offered, Version? latest)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath,
                day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "|" + (offered?.ToString() ?? "") + "|" + (latest?.ToString() ?? ""));
        }
        catch
        {
            // Unwritable settings dir: the check simply runs again next time.
        }
    }
}
