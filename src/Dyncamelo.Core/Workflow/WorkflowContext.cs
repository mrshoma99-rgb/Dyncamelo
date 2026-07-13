using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Dyncamelo.Core.Workflow;

/// <summary>
/// The ambient state one iteration of a workflow loop runs against: the item the
/// iteration is bound to, its position in the list, a name for templating, a
/// cancellation token, and a collector for the iteration's result. A single
/// context instance is reused across iterations — the loop node re-binds
/// <see cref="CurrentItem"/> / <see cref="Index"/> and clears the collector
/// before running the action sequence for each item.
/// </summary>
public sealed class WorkflowContext
{
    private static readonly Dictionary<Type, PropertyInfo?> NameProperties =
        new Dictionary<Type, PropertyInfo?>();

    private readonly List<object?> _collected = new List<object?>();

    /// <summary>Creates a context.</summary>
    /// <param name="cancellationToken">Token the loop honors between items.</param>
    public WorkflowContext(CancellationToken cancellationToken = default)
    {
        CancellationToken = cancellationToken;
    }

    /// <summary>The item this iteration is bound to (may be a single element or a group).</summary>
    public object? CurrentItem { get; internal set; }

    /// <summary>Zero-based position of the current item in the list.</summary>
    public int Index { get; internal set; }

    /// <summary>One-based position of the current item (for human-facing names).</summary>
    public int Index1 => Index + 1;

    /// <summary>Total number of items being iterated.</summary>
    public int Count { get; internal set; }

    /// <summary>Token the loop honors; actions may observe it for long operations.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Values the current iteration's actions collected, in order.</summary>
    public IReadOnlyList<object?> Collected => _collected;

    /// <summary>
    /// A name for the current item, resolved without any host dependency: a
    /// string item is itself; otherwise a public <c>DisplayName</c> or <c>Name</c>
    /// property is used (this duck-types Navisworks <c>ModelItem</c>/<c>SavedItem</c>
    /// and most host types); otherwise <see cref="object.ToString"/>. Never null.
    /// </summary>
    public string ItemName => ResolveName(CurrentItem);

    /// <summary>Records a per-iteration result (e.g. a created viewpoint) for the loop to output.</summary>
    /// <param name="value">The value to collect.</param>
    public void Collect(object? value) => _collected.Add(value);

    /// <summary>
    /// Expands a name template against the current iteration. Recognized tokens
    /// (case-insensitive): <c>{name}</c>/<c>{item}</c> → <see cref="ItemName"/>,
    /// <c>{index}</c> → zero-based index, <c>{index1}</c>/<c>{n}</c> → one-based
    /// index, <c>{count}</c> → total. An empty or null template yields the item's
    /// name, so <c>Action.SaveViewpoint()</c> defaults to naming by item.
    /// </summary>
    /// <param name="template">The template string.</param>
    /// <returns>The expanded string.</returns>
    public string Expand(string? template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return ItemName;
        }

        var result = new StringBuilder(template!.Length + 16);
        for (int i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c != '{')
            {
                result.Append(c);
                continue;
            }

            int close = template.IndexOf('}', i + 1);
            if (close < 0)
            {
                // Unterminated brace: emit the rest verbatim.
                result.Append(template, i, template.Length - i);
                break;
            }

            var token = template.Substring(i + 1, close - i - 1).Trim();
            result.Append(ExpandToken(token));
            i = close;
        }

        return result.ToString();
    }

    /// <summary>
    /// Re-binds the context to a new iteration and clears the per-iteration
    /// collector. Called by the loop node before running the action sequence for
    /// each item; the individual properties are otherwise read-only, so this is
    /// the only way to advance the context.
    /// </summary>
    /// <param name="item">The item to bind.</param>
    /// <param name="index">Its zero-based index.</param>
    /// <param name="count">The total item count.</param>
    public void Bind(object? item, int index, int count)
    {
        CurrentItem = item;
        Index = index;
        Count = count;
        _collected.Clear();
    }

    private string ExpandToken(string token)
    {
        if (token.Equals("name", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("item", StringComparison.OrdinalIgnoreCase))
        {
            return ItemName;
        }

        if (token.Equals("index", StringComparison.OrdinalIgnoreCase))
        {
            return Index.ToString(CultureInfo.InvariantCulture);
        }

        if (token.Equals("index1", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("n", StringComparison.OrdinalIgnoreCase))
        {
            return Index1.ToString(CultureInfo.InvariantCulture);
        }

        if (token.Equals("count", StringComparison.OrdinalIgnoreCase))
        {
            return Count.ToString(CultureInfo.InvariantCulture);
        }

        // Unknown token: keep it literal so typos are visible, not silently dropped.
        return "{" + token + "}";
    }

    private static string ResolveName(object? item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        if (item is string s)
        {
            return s;
        }

        var property = GetNameProperty(item.GetType());
        if (property != null)
        {
            var value = property.GetValue(item) as string;
            if (!string.IsNullOrEmpty(value))
            {
                return value!;
            }
        }

        return item.ToString() ?? string.Empty;
    }

    private static PropertyInfo? GetNameProperty(Type type)
    {
        lock (NameProperties)
        {
            if (NameProperties.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var property = FindStringProperty(type, "DisplayName") ?? FindStringProperty(type, "Name");
            NameProperties[type] = property;
            return property;
        }
    }

    private static PropertyInfo? FindStringProperty(Type type, string name)
    {
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        return property != null && property.CanRead && property.PropertyType == typeof(string) &&
               property.GetIndexParameters().Length == 0
            ? property
            : null;
    }
}
