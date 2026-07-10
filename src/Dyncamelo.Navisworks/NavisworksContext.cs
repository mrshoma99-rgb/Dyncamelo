using System;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Supplies the Navisworks <see cref="Document"/> a graph run should operate on.
/// The App layer implements this and registers it both on the
/// <see cref="Dyncamelo.Core.Execution.EvaluationContext"/> (for NodeModel-based
/// nodes) and on <see cref="NavisworksContext.HostService"/> (for zero-touch
/// static nodes, which cannot see the evaluation context).
/// </summary>
public interface IHostDocumentService
{
    /// <summary>The document the current run targets, or null when none is open.</summary>
    Document? ActiveDocument { get; }
}

/// <summary>
/// Ambient host access for zero-touch Navisworks nodes. Core's zero-touch loader
/// invokes plain static methods and provides no way to inject the
/// <c>EvaluationContext</c> into them, so the App must assign
/// <see cref="HostService"/> (once at startup, or before each run) on the
/// Navisworks main thread. When it is unset, nodes fall back to
/// <c>Autodesk.Navisworks.Api.Application.ActiveDocument</c>, which is correct
/// inside a live Navisworks session.
/// </summary>
[IsVisibleInLibrary(false)]
public static class NavisworksContext
{
    /// <summary>The host-provided document service; set by the App layer.</summary>
    public static IHostDocumentService? HostService { get; set; }

    /// <summary>
    /// Resolves the document a node should operate on: an explicitly wired
    /// document wins, then <see cref="HostService"/>, then the live
    /// <c>Application.ActiveDocument</c>.
    /// </summary>
    /// <param name="document">The value of the node's optional "document" input.</param>
    /// <returns>A non-null, non-empty document.</returns>
    /// <exception cref="InvalidOperationException">No active Navisworks document is available.</exception>
    public static Document ResolveDocument(Document? document = null)
    {
        return ResolveDocument(document, allowClear: false);
    }

    /// <summary>
    /// Resolves the document a node should operate on, optionally accepting an
    /// empty (IsClear) document. Lifecycle nodes whose purpose is to load the
    /// FIRST file(s) into a fresh session (Document.Open, Document.AppendFiles,
    /// Document.Merge — the headless Batch-Utility scenario) pass
    /// <paramref name="allowClear"/> = true; read/query nodes keep the default
    /// rejection of clear documents.
    /// </summary>
    /// <param name="document">The value of the node's optional "document" input.</param>
    /// <param name="allowClear">True to accept a non-null document with no files loaded.</param>
    /// <returns>A non-null document; non-empty unless <paramref name="allowClear"/> is true.</returns>
    /// <exception cref="InvalidOperationException">No active Navisworks document is available.</exception>
    public static Document ResolveDocument(Document? document, bool allowClear)
    {
        var resolved = document ?? HostService?.ActiveDocument ?? GetApplicationDocument();
        if (resolved == null || (!allowClear && resolved.IsClear))
        {
            throw new InvalidOperationException("No active Navisworks document.");
        }

        return resolved;
    }

    private static Document? GetApplicationDocument()
    {
        try
        {
            return Autodesk.Navisworks.Api.Application.ActiveDocument;
        }
        catch (Exception)
        {
            // Outside a Navisworks host the Application type cannot initialize.
            return null;
        }
    }
}
