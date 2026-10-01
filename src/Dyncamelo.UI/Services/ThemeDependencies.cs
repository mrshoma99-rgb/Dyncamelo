using System;

namespace Dyncamelo.UI.Services;

/// <summary>
/// The theme merges Nodify's own dark theme by a pack URI (<c>pack://application:,,,/Nodify;component/…</c>). WPF resolves that
/// by assembly <em>name</em>, and finds Nodify only if it is already loaded. The editor loads it through the types it uses; the
/// Script Player touches no Nodify type, so in a host that is asked for assemblies one reference at a time (Navisworks) its pane
/// failed with "Could not load file or assembly 'Nodify'". Every control that loads the theme calls <see cref="EnsureLoaded"/> first.
/// </summary>
internal static class ThemeDependencies
{
    private static bool _loaded;

    /// <summary>Loads the assemblies the theme refers to by name.</summary>
    public static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        // Naming a type is what makes the runtime bind the assembly the way it binds Dyncamelo.UI's own references.
        GC.KeepAlive(typeof(Nodify.NodifyEditor).Assembly);
        _loaded = true;
    }
}
