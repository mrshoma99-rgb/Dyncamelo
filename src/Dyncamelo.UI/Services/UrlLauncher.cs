using System;
using System.Diagnostics;

namespace Dyncamelo.UI.Services;

/// <summary>Opens a web address in the user's browser.</summary>
internal static class UrlLauncher
{
    /// <summary>Opens <paramref name="url"/> (an http or https address) in the default browser.</summary>
    /// <param name="url">The address.</param>
    /// <returns>False when it is not a web address or no browser could be started.</returns>
    public static bool Open(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            // No browser association: nothing useful to do from here.
            return false;
        }
    }
}
