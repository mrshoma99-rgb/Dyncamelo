using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Dyncamelo.Core.Graph;

/// <summary>
/// A typed connection point on a node. Output ports additionally cache the
/// last computed value (<see cref="Value"/>), which is the only data channel
/// between nodes.
/// </summary>
public class PortModel : INotifyPropertyChanged
{
    private bool _usingDefaultValue;
    private object? _value;
    private object? _userValue;
    private bool _hasUserValue;

    /// <summary>Creates a port.</summary>
    /// <param name="owner">The node the port belongs to.</param>
    /// <param name="name">Port name (unique per direction within the node).</param>
    /// <param name="declaredType">Declared CLR type of the port; drives coercion and replication rank.</param>
    /// <param name="direction">Input or output.</param>
    public PortModel(NodeModel owner, string name, Type declaredType, PortDirection direction)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DeclaredType = declaredType ?? throw new ArgumentNullException(nameof(declaredType));
        Direction = direction;
        Id = Guid.NewGuid();
        Level = -1;
    }

    /// <summary>Stable identifier of the port.</summary>
    public Guid Id { get; internal set; }

    /// <summary>The node this port belongs to.</summary>
    public NodeModel Owner { get; }

    /// <summary>Port name; also the serialized identity of the port within its node.</summary>
    public string Name { get; }

    /// <summary>Description/tooltip for the port.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Declared CLR type. Scalar types replicate when they receive lists (see engine docs).</summary>
    public Type DeclaredType { get; }

    /// <summary>Whether this is an input or output port.</summary>
    public PortDirection Direction { get; }

    /// <summary>True when the port has a default value usable while unconnected.</summary>
    public bool HasDefault { get; internal set; }

    /// <summary>Default value used when the port is unconnected and <see cref="UsingDefaultValue"/> is true.</summary>
    public object? DefaultValue { get; internal set; }

    /// <summary>
    /// True when an unconnected defaulted input should supply <see cref="DefaultValue"/>.
    /// The UI may toggle this off to make the port behave as required.
    /// </summary>
    public bool UsingDefaultValue
    {
        get => _usingDefaultValue;
        set
        {
            if (_usingDefaultValue == value)
            {
                return;
            }

            _usingDefaultValue = value;
            OnPropertyChanged();
            Owner.MarkDirty();
        }
    }

    /// <summary>
    /// Allowed values for a choice input (from a <c>[NodeChoices]</c> parameter),
    /// or <c>null</c> for a free port. When present, the editor offers a dropdown
    /// and the chosen value is stored in <see cref="UserValue"/>.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<string>? Choices { get; internal set; }

    /// <summary>
    /// True when the user has pinned an inline value on this unconnected input
    /// (e.g. picked a choice from the dropdown). Takes precedence over
    /// <see cref="DefaultValue"/> when the port has no incoming connection.
    /// </summary>
    public bool HasUserValue => _hasUserValue;

    /// <summary>The user-pinned inline value; only meaningful when <see cref="HasUserValue"/> is true.</summary>
    public object? UserValue => _userValue;

    /// <summary>
    /// Pins an inline value on this input (used by the choice dropdown). Marks
    /// the owning node dirty so the graph re-evaluates.
    /// </summary>
    /// <param name="value">The value to feed the port while it is unconnected.</param>
    public void SetUserValue(object? value)
    {
        if (_hasUserValue && Equals(_userValue, value))
        {
            return;
        }

        _hasUserValue = true;
        _userValue = value;
        OnPropertyChanged(nameof(UserValue));
        OnPropertyChanged(nameof(HasUserValue));
        Owner.MarkDirty();
    }

    /// <summary>Clears any pinned inline value, reverting to the port default. Marks the node dirty.</summary>
    public void ClearUserValue()
    {
        if (!_hasUserValue)
        {
            return;
        }

        _hasUserValue = false;
        _userValue = null;
        OnPropertyChanged(nameof(UserValue));
        OnPropertyChanged(nameof(HasUserValue));
        Owner.MarkDirty();
    }

    /// <summary>Reserved for List@Level support (-1 = off). Persisted, not yet interpreted.</summary>
    public int Level { get; set; }

    /// <summary>Reserved for List@Level support. Persisted, not yet interpreted.</summary>
    public bool UseLevels { get; set; }

    /// <summary>Reserved for List@Level support. Persisted, not yet interpreted.</summary>
    public bool KeepListStructure { get; set; }

    /// <summary>
    /// Cached value. For output ports this is the last computed result and the
    /// value downstream nodes read; it survives clean (skipped) runs.
    /// </summary>
    public object? Value
    {
        get => _value;
        internal set
        {
            _value = value;
            OnPropertyChanged();
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises <see cref="PropertyChanged"/>.</summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <inheritdoc />
    public override string ToString() => Owner.Name + "." + Name + " (" + Direction + ")";
}
