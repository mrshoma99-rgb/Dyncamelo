using System;
using Autodesk.Navisworks.Api;
using Dyncamelo.Navisworks;

namespace Dyncamelo.App;

/// <summary>
/// Supplies the live Navisworks document to graph runs. Registered on each
/// <see cref="Dyncamelo.Core.Execution.EvaluationContext"/> (for NodeModel-based
/// nodes) and on <see cref="NavisworksContext.HostService"/> (for zero-touch
/// static nodes). All access happens on the Navisworks main thread — the dock
/// pane's dispatcher thread is that thread.
/// </summary>
public class HostDocumentService : IHostDocumentService
{
    /// <inheritdoc />
    public Document? ActiveDocument
    {
        get
        {
            try
            {
                return Autodesk.Navisworks.Api.Application.ActiveDocument;
            }
            catch (Exception)
            {
                // Outside a fully initialized Navisworks session there is no document.
                return null;
            }
        }
    }
}
