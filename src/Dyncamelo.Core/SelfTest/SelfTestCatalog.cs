using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.Core.SelfTest;

/// <summary>
/// The checks Help &gt; Run Self-Test performs inside the host: nodes that only read, run on the open model and looked at afterwards. The
/// Navisworks nodes cannot be run by the project's own automated tests (there is no Navisworks on the build machines), so this is how a
/// person with Navisworks finds out in a minute whether the nodes work on their version and their model.
/// </summary>
public static class SelfTestCatalog
{
    /// <summary>
    /// Every node a self-test case may use: all of them only read. The runner refuses any other, so the self-test can never change the
    /// model, a saved item or a file. Adding a node here is a statement that it only reads (a test pins the list against the node catalogue).
    /// </summary>
    public static readonly ISet<string> ReadOnlyNodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Application.Version", "Document.Current", "Document.Info", "Document.Models", "Models.RootItems", "Model.Info",
        "Units.Current", "Units.All", "Export.IfcClasses", "Camera.Current",
        "Selection.Current", "SelectionSets.All", "Viewpoints.All", "TimeLiner.Tasks", "Clash.Tests", "Clash.SummaryTable",
        "Search.ByProperty",
        "ModelItem.DisplayName", "ModelItem.ClassInfo", "ModelItem.ModelName", "ModelItem.SourceInfo", "ModelItem.HasGeometry",
        "ModelItem.BoundingBox", "ModelItem.Children", "ModelItem.IfcGuid",
        "Properties.Categories", "Properties.AsDictionary", "Properties.Discover", "Properties.ToTable",
    };

    /// <summary>The cases, in the order they run.</summary>
    public static IReadOnlyList<SelfTestCase> All { get; } = Build();

    private static IReadOnlyList<SelfTestCase> Build()
    {
        var cases = new List<SelfTestCase>();

        // ----- the application and the document (no model needed) -----------------------------------------------------------
        cases.Add(new SelfTestCase("Application and document", "product and API version", new SelfTestStep("Application.Version"))
        {
            Check = n => Text(n[0], "product") ?? Text(n[0], "apiVersion"),
        });
        cases.Add(new SelfTestCase("Application and document", "the active document", new SelfTestStep("Document.Current"))
        {
            Check = n => NotNull(n[0], "document"),
        });
        cases.Add(new SelfTestCase("Application and document", "document information", new SelfTestStep("Document.Info")));
        cases.Add(new SelfTestCase("Application and document", "display units of the document", new SelfTestStep("Units.Current"))
        {
            Check = n => Text(n[0], "units"),
        });
        cases.Add(new SelfTestCase("Application and document", "list of unit names", new SelfTestStep("Units.All"))
        {
            Check = n => CountAtLeast(n[0], "names", 5),
        });
        cases.Add(new SelfTestCase("Application and document", "list of IFC class names", new SelfTestStep("Export.IfcClasses"))
        {
            Check = n => CountAtLeast(n[0], "classes", 5),
        });
        cases.Add(new SelfTestCase("Application and document", "current camera", new SelfTestStep("Camera.Current"))
        {
            Check = n => NotNull(n[0], "position"),
        });

        // ----- saved items and the selection (no model needed: the lists may be empty) ------------------------------------------
        cases.Add(new SelfTestCase("Saved items", "current selection", new SelfTestStep("Selection.Current"))
        {
            Check = n => NotNull(n[0], "items"),
        });
        cases.Add(new SelfTestCase("Saved items", "selection and search sets", new SelfTestStep("SelectionSets.All"))
        {
            Check = n => NotNull(n[0], "selectionSets"),
        });
        cases.Add(new SelfTestCase("Saved items", "saved viewpoints", new SelfTestStep("Viewpoints.All"))
        {
            Check = n => NotNull(n[0], "viewpoints"),
        });
        cases.Add(new SelfTestCase("Saved items", "TimeLiner tasks", new SelfTestStep("TimeLiner.Tasks"))
        {
            Check = n => NotNull(n[0], "tasks"),
        });
        cases.Add(new SelfTestCase("Saved items", "Clash Detective tests", new SelfTestStep("Clash.Tests"))
        {
            NeedsEdition = "Manage",
            Check = n => NotNull(n[0], "tests"),
        });
        cases.Add(new SelfTestCase("Saved items", "clash summary table", new SelfTestStep("Clash.Tests"), new SelfTestStep("Clash.SummaryTable").From("tests", 0, "tests"))
        {
            NeedsEdition = "Manage",
            Check = n => NotNull(n[1], "headers"),
        });

        // ----- the model --------------------------------------------------------------------------------------------------
        cases.Add(new SelfTestCase("Model", "models loaded in the document", new SelfTestStep("Document.Models"))
        {
            NeedsModel = true,
            Check = n => CountAtLeast(n[0], "models", 1),
        });
        cases.Add(new SelfTestCase("Model", "root item of every model", new SelfTestStep("Models.RootItems"))
        {
            NeedsModel = true,
            Check = n => CountAtLeast(n[0], "rootItems", 1),
        });
        cases.Add(new SelfTestCase("Model", "model information", new SelfTestStep("Document.Models"), new SelfTestStep("Model.Info").From("model", 0, "models"))
        {
            NeedsModel = true,
        });
        cases.Add(new SelfTestCase("Model", "search that finds nothing", new SelfTestStep("Search.ByProperty")
            .With("categoryName", "Item").With("propertyName", "Name").With("value", "zzz-dyncamelo-self-test-no-such-name-zzz"))
        {
            NeedsModel = true,
            Check = n => Count(n[0], "items") == 0 ? null : "a search for a name nobody has found " + Count(n[0], "items") + " item(s)",
        });

        // ----- model items: each node run on the root item of every model --------------------------------------------------
        cases.Add(OnRootItems("display names", "ModelItem.DisplayName", n => AllTextsNotEmpty(n[1], "name")));
        cases.Add(OnRootItems("class names", "ModelItem.ClassInfo", null));
        cases.Add(OnRootItems("name of the file they come from", "ModelItem.ModelName", null));
        cases.Add(OnRootItems("source file and type", "ModelItem.SourceInfo", null));
        cases.Add(OnRootItems("has geometry", "ModelItem.HasGeometry", null));
        cases.Add(OnRootItems("bounding boxes", "ModelItem.BoundingBox", n => NotNull(n[1], "boundingBox")));
        cases.Add(OnRootItems("children", "ModelItem.Children", null));
        cases.Add(OnRootItems("IFC GlobalId", "ModelItem.IfcGuid", null));

        // ----- properties ----------------------------------------------------------------------------------------------------
        cases.Add(OnRootItems("property categories", "Properties.Categories", n => NotNull(n[1], "categories")));
        cases.Add(OnRootItems("all properties as a dictionary", "Properties.AsDictionary", n => NotNull(n[1], "properties")));
        cases.Add(new SelfTestCase("Properties", "what properties the root items carry", new SelfTestStep("Models.RootItems"), new SelfTestStep("Properties.Discover").From("items", 0, "rootItems"))
        {
            NeedsModel = true,
            Check = n => NotNull(n[1], "table"),
        });
        cases.Add(new SelfTestCase("Properties", "names of the root items as a table", new SelfTestStep("Models.RootItems"),
            new SelfTestStep("Properties.ToTable").From("items", 0, "rootItems").With("properties", new List<object?> { "Item.Name" }))
        {
            NeedsModel = true,
            Check = n => NotNull(n[1], "table"),
        });

        return cases;
    }

    // A case that runs a node taking one item on the root item of every model (the node's own list handling does the rest).
    private static SelfTestCase OnRootItems(string title, string node, Func<IReadOnlyList<NodeModel>, string?>? check)
    {
        var area = node.StartsWith("Properties.", StringComparison.Ordinal) ? "Properties" : "Model items";
        return new SelfTestCase(area, title + " of the root items", new SelfTestStep("Models.RootItems"), new SelfTestStep(node).From("item", 0, "rootItems"))
        {
            NeedsModel = true,
            Check = check,
        };
    }

    // ----- checks ----------------------------------------------------------------------------------------------------------

    private static object? Value(NodeModel node, string port)
    {
        var found = node.OutPorts.FirstOrDefault(p => string.Equals(p.Name, port, StringComparison.Ordinal));
        return found?.Value;
    }

    private static string? NotNull(NodeModel node, string port) =>
        node.OutPorts.Any(p => string.Equals(p.Name, port, StringComparison.Ordinal))
            ? Value(node, port) != null ? null : "the output '" + port + "' is empty (null)"
            : "the node has no output called '" + port + "' (it changed since this check was written)";

    private static string? Text(NodeModel node, string port)
    {
        var problem = NotNull(node, port);
        if (problem != null)
        {
            return problem;
        }

        return Value(node, port) is string text && text.Trim().Length > 0 ? null : "the output '" + port + "' is not text, or is blank";
    }

    private static int Count(NodeModel node, string port)
    {
        var value = Value(node, port);
        return value is IEnumerable sequence && !(value is string) ? sequence.Cast<object?>().Count() : -1;
    }

    private static string? CountAtLeast(NodeModel node, string port, int minimum)
    {
        var problem = NotNull(node, port);
        if (problem != null)
        {
            return problem;
        }

        var count = Count(node, port);
        return count >= minimum ? null : "the output '" + port + "' holds " + (count < 0 ? "no list" : count + " item(s)") + ", expected at least " + minimum;
    }

    private static string? AllTextsNotEmpty(NodeModel node, string port)
    {
        var problem = NotNull(node, port);
        if (problem != null)
        {
            return problem;
        }

        if (!(Value(node, port) is IEnumerable sequence) || Value(node, port) is string)
        {
            return "the output '" + port + "' is not a list";
        }

        var empty = sequence.Cast<object?>().Count(v => !(v is string text) || text.Trim().Length == 0);
        return empty == 0 ? null : empty + " name(s) are blank";
    }
}
