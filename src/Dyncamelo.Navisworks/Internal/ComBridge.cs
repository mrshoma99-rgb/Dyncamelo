using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using ComApi = Autodesk.Navisworks.Api.Interop.ComApi;

namespace Dyncamelo.Navisworks.Internal;

/// <summary>
/// Safe wrappers over <see cref="ComApiBridge"/> for the COM-only surfaces of
/// the Navisworks 2024 API — user-defined property tabs above all (the .NET API
/// has no write surface for properties). Internal — never surfaced as nodes.
///
/// Invariants owned here:
/// - Every COM call runs on the Navisworks main thread (a v0.2 host invariant —
///   callers must not move these onto worker threads).
/// - Released-COM-object hygiene: every RCW acquired inside a helper is released
///   in a finally block via <see cref="Release"/>; helpers never leak RCWs to
///   callers except the primitives (<see cref="State"/>, <see cref="ToPath"/>,
///   <see cref="ToSelection"/>) whose result the caller owns and must release.
/// - User-tab indices for SetUserDefined/RemoveUserDefined are 1-based and count
///   only tabs with <c>InwGUIAttribute2.UserDefined == true</c>; index 0 creates
///   a new tab. (Overwrite-at-index semantics: RUNTIME-CHECK.)
/// - Edits set <c>Document.IsModified</c> and persist in NWF/NWD only — they are
///   never written back to source files.
/// </summary>
internal static class ComBridge
{
    /// <summary>
    /// The COM state object (<c>InwOpState10</c>) — the root of every COM-bridge
    /// call chain. Throws a node-friendly error when no live Navisworks session
    /// backs the bridge (e.g. running outside the host).
    /// </summary>
    internal static ComApi.InwOpState10 State()
    {
        ComApi.InwOpState10? state;
        try
        {
            state = ComApiBridge.State;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "The Navisworks COM bridge is unavailable (" + ex.Message +
                "). COM-based nodes only work inside a live Navisworks session.", ex);
        }

        return state ?? throw new InvalidOperationException(
            "The Navisworks COM bridge returned no state. COM-based nodes only work inside a live Navisworks session.");
    }

    /// <summary>Converts a model item to a COM path. Caller owns (and releases) the result.</summary>
    internal static ComApi.InwOaPath ToPath(ModelItem item)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item), "No model item provided.");
        }

        return ComApiBridge.ToInwOaPath(item);
    }

    /// <summary>
    /// Converts model items to a COM selection (for COM calls that take
    /// <c>InwOpSelection</c>, e.g. transform overrides). Caller owns (and
    /// releases) the result.
    /// </summary>
    internal static ComApi.InwOpSelection ToSelection(IEnumerable<ModelItem>? items)
    {
        var collection = NavisValues.ToItemCollection(items);
        if (collection.Count == 0)
        {
            throw new ArgumentException("No model items provided.", nameof(items));
        }

        return ComApiBridge.ToInwOpSelection(collection);
    }

    /// <summary>
    /// Releases COM runtime-callable wrappers (best effort; nulls and non-COM
    /// objects are ignored). Call from finally blocks after any COM chain.
    /// </summary>
    internal static void Release(params object?[] comObjects)
    {
        if (comObjects == null)
        {
            return;
        }

        foreach (var comObject in comObjects)
        {
            if (comObject == null)
            {
                continue;
            }

            try
            {
                if (Marshal.IsComObject(comObject))
                {
                    Marshal.ReleaseComObject(comObject);
                }
            }
            catch (Exception)
            {
                // Release is hygiene, never a failure mode for the node.
            }
        }
    }

    // -------------------------------------------------- User-defined tabs

    /// <summary>
    /// A stable identifier-style internal name derived from a display name
    /// (letters, digits and underscores only; e.g. "Dyncamelo Data" →
    /// "Dyncamelo_Data"). Search sets can target it via HasPropertyByName.
    /// </summary>
    internal static string DeriveInternalName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "DyncameloData";
        }

        var builder = new StringBuilder(displayName.Length);
        foreach (var ch in displayName)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
            else if (ch == ' ' || ch == '_' || ch == '-')
            {
                builder.Append('_');
            }
        }

        var name = builder.ToString().Trim('_');
        return name.Length == 0 ? "DyncameloData" : name;
    }

    /// <summary>
    /// Writes (creates or overwrites) a user-defined property tab on one item.
    /// With <paramref name="merge"/> true, existing properties of a same-named
    /// tab are kept and new values win on name collision; false replaces the
    /// tab's content entirely. Values become COM VARIANTs — string, double, int,
    /// bool and DateTime pass through; anything else is written as its
    /// invariant-culture string.
    /// </summary>
    /// <param name="item">The model item to stamp.</param>
    /// <param name="tabDisplayName">User-visible tab name (e.g. "Dyncamelo Data").</param>
    /// <param name="tabInternalName">
    /// Stable internal tab name, or null to derive one from the display name.
    /// </param>
    /// <param name="values">Property display name → value pairs (order preserved).</param>
    /// <param name="merge">Merge into an existing same-named tab instead of replacing it.</param>
    internal static void SetUserDefinedTab(
        ModelItem item,
        string tabDisplayName,
        string? tabInternalName,
        IEnumerable<KeyValuePair<string, object?>> values,
        bool merge)
    {
        if (string.IsNullOrWhiteSpace(tabDisplayName))
        {
            throw new ArgumentException("Tab name must not be empty.", nameof(tabDisplayName));
        }

        if (values == null)
        {
            throw new ArgumentNullException(nameof(values), "No property values provided.");
        }

        var pairs = MergePairs(
            merge ? TryReadUserTab(item, tabDisplayName) : null,
            values);

        var state = State();
        ComApi.InwOaPath? path = null;
        ComApi.InwGUIPropertyNode2? node = null;
        ComApi.InwOaPropertyVec? vec = null;
        try
        {
            path = ToPath(item);
            node = (ComApi.InwGUIPropertyNode2)state.GetGUIPropertyNode(path, true);
            vec = CreatePropertyVec(state, pairs);
            int index = FindUserTabIndex(node, tabDisplayName); // 0 = create new tab
            node.SetUserDefined(
                index,
                tabDisplayName,
                string.IsNullOrWhiteSpace(tabInternalName) ? DeriveInternalName(tabDisplayName) : tabInternalName,
                vec);
        }
        finally
        {
            Release(vec, node, path);
        }
    }

    /// <summary>
    /// Removes a user-defined tab by display name. Returns true when a tab was
    /// found and removed, false when the item has no such tab.
    /// </summary>
    internal static bool RemoveUserDefinedTab(ModelItem item, string tabDisplayName)
    {
        if (string.IsNullOrWhiteSpace(tabDisplayName))
        {
            throw new ArgumentException("Tab name must not be empty.", nameof(tabDisplayName));
        }

        var state = State();
        ComApi.InwOaPath? path = null;
        ComApi.InwGUIPropertyNode2? node = null;
        try
        {
            path = ToPath(item);
            node = (ComApi.InwGUIPropertyNode2)state.GetGUIPropertyNode(path, true);
            int index = FindUserTabIndex(node, tabDisplayName);
            if (index == 0)
            {
                return false;
            }

            node.RemoveUserDefined(index);
            return true;
        }
        finally
        {
            Release(node, path);
        }
    }

    /// <summary>
    /// Renames a user-defined tab in place: rewrites the same property vector at
    /// the same 1-based index under the new display name, keeping the existing
    /// internal name so search sets stay valid. Returns false when the item has
    /// no tab with the old name.
    /// </summary>
    internal static bool RenameUserDefinedTab(ModelItem item, string tabDisplayName, string newDisplayName)
    {
        if (string.IsNullOrWhiteSpace(newDisplayName))
        {
            throw new ArgumentException("New tab name must not be empty.", nameof(newDisplayName));
        }

        var state = State();
        ComApi.InwOaPath? path = null;
        ComApi.InwGUIPropertyNode2? node = null;
        ComApi.InwGUIAttributesColl? attributes = null;
        ComApi.InwOaPropertyVec? vec = null;
        try
        {
            path = ToPath(item);
            node = (ComApi.InwGUIPropertyNode2)state.GetGUIPropertyNode(path, true);
            attributes = node.GUIAttributes();

            int index = 0;
            string? internalName = null;
            List<KeyValuePair<string, object?>>? pairs = null;
            foreach (ComApi.InwGUIAttribute2 attribute in attributes)
            {
                try
                {
                    if (!attribute.UserDefined)
                    {
                        continue;
                    }

                    index++;
                    if (string.Equals(attribute.ClassUserName, tabDisplayName, StringComparison.Ordinal))
                    {
                        internalName = attribute.ClassName;
                        pairs = ReadAttributeProperties(attribute);
                        break;
                    }
                }
                finally
                {
                    Release(attribute);
                }
            }

            if (pairs == null)
            {
                return false;
            }

            vec = CreatePropertyVec(state, pairs);
            node.SetUserDefined(
                index,
                newDisplayName,
                string.IsNullOrEmpty(internalName) ? DeriveInternalName(newDisplayName) : internalName,
                vec);
            return true;
        }
        finally
        {
            Release(vec, attributes, node, path);
        }
    }

    /// <summary>Display names of the item's user-defined tabs, in tab order.</summary>
    internal static List<string> UserTabNames(ModelItem item)
    {
        var names = new List<string>();
        var state = State();
        ComApi.InwOaPath? path = null;
        ComApi.InwGUIPropertyNode2? node = null;
        ComApi.InwGUIAttributesColl? attributes = null;
        try
        {
            path = ToPath(item);
            node = (ComApi.InwGUIPropertyNode2)state.GetGUIPropertyNode(path, true);
            attributes = node.GUIAttributes();
            foreach (ComApi.InwGUIAttribute2 attribute in attributes)
            {
                try
                {
                    if (attribute.UserDefined)
                    {
                        names.Add(attribute.ClassUserName ?? string.Empty);
                    }
                }
                finally
                {
                    Release(attribute);
                }
            }
        }
        finally
        {
            Release(attributes, node, path);
        }

        return names;
    }

    /// <summary>
    /// Reads a user-defined tab's properties (display name → CLR value pairs, in
    /// tab order), or null when the item has no tab with that display name.
    /// </summary>
    internal static List<KeyValuePair<string, object?>>? TryReadUserTab(ModelItem item, string tabDisplayName)
    {
        var state = State();
        ComApi.InwOaPath? path = null;
        ComApi.InwGUIPropertyNode2? node = null;
        ComApi.InwGUIAttributesColl? attributes = null;
        try
        {
            path = ToPath(item);
            node = (ComApi.InwGUIPropertyNode2)state.GetGUIPropertyNode(path, true);
            attributes = node.GUIAttributes();
            foreach (ComApi.InwGUIAttribute2 attribute in attributes)
            {
                try
                {
                    if (attribute.UserDefined &&
                        string.Equals(attribute.ClassUserName, tabDisplayName, StringComparison.Ordinal))
                    {
                        return ReadAttributeProperties(attribute);
                    }
                }
                finally
                {
                    Release(attribute);
                }
            }
        }
        finally
        {
            Release(attributes, node, path);
        }

        return null;
    }

    /// <summary>
    /// The 1-based SetUserDefined/RemoveUserDefined index of the user tab with
    /// the given display name (counting only <c>UserDefined == true</c>
    /// attributes), or 0 when the item has no such tab (0 = "create new" for
    /// SetUserDefined).
    /// </summary>
    internal static int FindUserTabIndex(ComApi.InwGUIPropertyNode2 node, string tabDisplayName)
    {
        ComApi.InwGUIAttributesColl? attributes = null;
        try
        {
            attributes = node.GUIAttributes();
            int index = 0;
            foreach (ComApi.InwGUIAttribute2 attribute in attributes)
            {
                try
                {
                    if (!attribute.UserDefined)
                    {
                        continue;
                    }

                    index++;
                    if (string.Equals(attribute.ClassUserName, tabDisplayName, StringComparison.Ordinal))
                    {
                        return index;
                    }
                }
                finally
                {
                    Release(attribute);
                }
            }

            return 0;
        }
        finally
        {
            Release(attributes);
        }
    }

    /// <summary>
    /// Converts a Dyncamelo port value to a COM-VARIANT-friendly value for
    /// <c>InwOaProperty.value</c>: string/double/int/bool/DateTime pass through,
    /// other numerics widen losslessly (a long beyond Int32 range becomes a
    /// double), everything else becomes its invariant string.
    /// </summary>
    internal static object ToComValue(object? value)
    {
        switch (value)
        {
            case null: return string.Empty;
            case string text: return text;
            case bool flag: return flag;
            case int i: return i;
            // A long that fits Int32 stays integral; anything bigger widens to
            // double (never a raw OverflowException on e.g. timestamp ticks).
            case long l: return l >= int.MinValue && l <= int.MaxValue ? (int)l : (object)(double)l;
            case short s: return (int)s;
            case byte b: return (int)b;
            case double d: return d;
            case float f: return (double)f;
            case decimal m: return (double)m;
            case DateTime time: return time;
            default:
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }

    // ------------------------------------------------------------ privates

    /// <summary>Builds an <c>InwOaPropertyVec</c> from name/value pairs. Caller releases.</summary>
    private static ComApi.InwOaPropertyVec CreatePropertyVec(
        ComApi.InwOpState10 state, IReadOnlyList<KeyValuePair<string, object?>> pairs)
    {
        var vec = (ComApi.InwOaPropertyVec)state.ObjectFactory(
            ComApi.nwEObjectType.eObjectType_nwOaPropertyVec, null, null);
        ComApi.InwOaPropertyColl? collection = null;
        try
        {
            collection = vec.Properties();
            var usedInternalNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in pairs)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    throw new ArgumentException("Property names must not be empty.");
                }

                var property = (ComApi.InwOaProperty)state.ObjectFactory(
                    ComApi.nwEObjectType.eObjectType_nwOaProperty, null, null);
                try
                {
                    property.name = UniqueInternalName(DeriveInternalName(pair.Key), usedInternalNames);
                    property.UserName = pair.Key;
                    property.value = ToComValue(pair.Value);
                    collection.Add(property);
                }
                finally
                {
                    Release(property);
                }
            }
        }
        catch
        {
            Release(vec);
            throw;
        }
        finally
        {
            Release(collection);
        }

        return vec;
    }

    private static string UniqueInternalName(string baseName, HashSet<string> used)
    {
        var name = baseName;
        int suffix = 2;
        while (!used.Add(name))
        {
            name = baseName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
            suffix++;
        }

        return name;
    }

    /// <summary>Reads an attribute's properties as display-name → CLR value pairs.</summary>
    private static List<KeyValuePair<string, object?>> ReadAttributeProperties(ComApi.InwGUIAttribute2 attribute)
    {
        var pairs = new List<KeyValuePair<string, object?>>();
        ComApi.InwOaPropertyColl? properties = null;
        try
        {
            properties = attribute.Properties();
            foreach (ComApi.InwOaProperty property in properties)
            {
                try
                {
                    pairs.Add(new KeyValuePair<string, object?>(
                        property.UserName ?? property.name ?? string.Empty,
                        property.value));
                }
                finally
                {
                    Release(property);
                }
            }
        }
        finally
        {
            Release(properties);
        }

        return pairs;
    }

    /// <summary>
    /// Union of existing tab pairs and new pairs: existing order is kept, new
    /// values overwrite same-named existing properties, brand-new names append.
    /// </summary>
    private static List<KeyValuePair<string, object?>> MergePairs(
        List<KeyValuePair<string, object?>>? existing,
        IEnumerable<KeyValuePair<string, object?>> updates)
    {
        var result = existing == null
            ? new List<KeyValuePair<string, object?>>()
            : new List<KeyValuePair<string, object?>>(existing);
        var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < result.Count; i++)
        {
            if (!indexByName.ContainsKey(result[i].Key))
            {
                indexByName.Add(result[i].Key, i);
            }
        }

        foreach (var update in updates)
        {
            if (indexByName.TryGetValue(update.Key, out var at))
            {
                result[at] = update;
            }
            else
            {
                indexByName.Add(update.Key, result.Count);
                result.Add(update);
            }
        }

        return result;
    }
}
