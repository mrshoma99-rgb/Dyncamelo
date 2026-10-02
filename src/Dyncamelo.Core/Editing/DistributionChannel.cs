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
