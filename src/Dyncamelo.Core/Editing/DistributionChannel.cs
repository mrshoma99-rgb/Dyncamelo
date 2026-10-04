using System;
using System.IO;

namespace Dyncamelo.Core.Editing;

/// <summary>
/// Where an installation came from. The Autodesk App Store package carries a one-line file, <c>distribution.txt</c>, next to the
/// plug-in's DLLs; the GitHub installer and the zip do not. A store installation is updated by the store, so it never offers the
/// GitHub download (a second copy of the bundle from another channel would load next to the first).
/// </summary>
public static class DistributionChannel
{
    /// <summary>The file beside the plug-in's assemblies that names the channel.</summary>
    public const string MarkerFileName = "distribution.txt";

    /// <summary>The marker text of the Autodesk App Store package.</summary>
    public const string AppStore = "autodesk-app-store";

    /// <summary>The channel of every other install (GitHub installer, zip, a build from source).</summary>
    public const string Direct = "direct";

    /// <summary>
    /// The Autodesk App Store, where the professional (commercial-use) copy is sold. This is the store's address until the listing
    /// exists; replace it with the listing's own address when the app is published.
    /// </summary>
    public const string AppStorePage = "https://apps.autodesk.com/";

    /// <summary>
    /// False until the professional copy is in the Autodesk App Store. While it is false every store link in the app is greyed out and
    /// reads "coming soon"; set it to true (and <see cref="AppStorePage"/> to the listing's address) when the listing is live.
    /// </summary>
    public const bool AppStoreListed = false;

    /// <summary>The text of the store button on the start screen.</summary>
    public static string StoreButtonText => AppStoreListed ? "Autodesk App Store ↗" : "Autodesk App Store: coming soon";

    /// <summary>The name of the edition of an install: "Personal use" (GitHub installer, zip, bimcamel.com, a build from source) or "Professional" (Autodesk App Store).</summary>
    /// <param name="channel">The channel, from <see cref="Detect"/>.</param>
    public static string EditionName(string channel) => channel == AppStore ? "Professional" : "Personal use";

    /// <summary>One sentence saying what the licence of an install allows and where the other edition is.</summary>
    /// <param name="channel">The channel, from <see cref="Detect"/>.</param>
    public static string EditionNote(string channel) => channel == AppStore
        ? "Licensed for professional use. Updates come from the Autodesk App Store."
        : AppStoreListed
            ? "Free for personal and other noncommercial use. For professional use (work at a company, a paid project) get CamelGraph from the Autodesk App Store."
            : "Free for personal and other noncommercial use. A copy for professional use (work at a company, a paid project) is coming soon to the Autodesk App Store.";

    /// <summary>The channel named by the first non-empty line of a marker file; anything unrecognised is <see cref="Direct"/>.</summary>
    /// <param name="markerText">The marker file's text, or null.</param>
    public static string Parse(string? markerText)
    {
        if (markerText == null)
        {
            return Direct;
        }

        foreach (var line in markerText.Split('\r', '\n'))
        {
            var text = line.Trim();
            if (text.Length > 0)
            {
                return string.Equals(text, AppStore, StringComparison.OrdinalIgnoreCase) ? AppStore : Direct;
            }
        }

        return Direct;
    }

    /// <summary>The channel of the install whose assemblies are in <paramref name="folder"/>; <see cref="Direct"/> when there is no readable marker.</summary>
    /// <param name="folder">The folder holding the plug-in's DLLs, or null.</param>
    public static string Detect(string? folder)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return Direct;
        }

        try
        {
            var path = Path.Combine(folder, MarkerFileName);
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Direct;
        }
        catch (Exception)
        {
            // An unreadable marker means "not the store"; the answer only decides whether to ask GitHub for updates.
            return Direct;
        }
    }

    /// <summary>True when the install in <paramref name="folder"/> came from the Autodesk App Store.</summary>
    /// <param name="folder">The folder holding the plug-in's DLLs, or null.</param>
    public static bool IsAppStore(string? folder) => Detect(folder) == AppStore;
}
