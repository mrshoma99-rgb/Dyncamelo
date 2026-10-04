using System;
using System.IO;
using System.Reflection;

namespace CamelGraph.UI.Services;

/// <summary>
/// The privacy policy that ships inside the app. Autodesk App Store rules require the text of the policy inside the app and a link to it
/// from the listing; the text is the repository's <c>PRIVACY.md</c>, embedded at build time.
/// </summary>
public static class PrivacyPolicy
{
    /// <summary>Where the same text is published online.</summary>
    public const string OnlineUrl = "https://github.com/mrshoma99-rgb/dyncamelo/blob/main/PRIVACY.md";

    /// <summary>The policy text (Markdown, written to read well as plain text).</summary>
    public static string Text()
    {
        using (var stream = typeof(PrivacyPolicy).Assembly.GetManifestResourceStream("CamelGraph.UI.PRIVACY.md"))
        {
            if (stream == null)
            {
                return "The privacy policy text is missing from this build. It is published at " + OnlineUrl;
            }

            using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
            {
                return reader.ReadToEnd().Replace("\r\n", "\n");
            }
        }
    }
}
