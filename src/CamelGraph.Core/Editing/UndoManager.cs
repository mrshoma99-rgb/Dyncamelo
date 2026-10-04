using System;
using System.Collections.Generic;
using System.Linq;

namespace CamelGraph.Core.Editing;

/// <summary>One reversible edit.</summary>
public interface IUndoStep
{
    /// <summary>Human-readable name ("Delete node"), shown as "Undo Delete node".</summary>
    string Label { get; }

    /// <summary>Reverts the edit.</summary>
    void Undo();

    /// <summary>Re-applies the edit.</summary>
    void Redo();
}

/// <summary>A step that can absorb a following step of the same kind (e.g. successive drag positions).</summary>
public interface ICoalescingStep : IUndoStep
{
    /// <summary>Merges <paramref name="newer"/> into this step; false when they are unrelated.</summary>
    bool TryMerge(IUndoStep newer);
}

/// <summary>Several steps undone and redone as one.</summary>
public sealed class CompositeStep : IUndoStep
{
    private readonly IReadOnlyList<IUndoStep> _steps;

    /// <summary>Creates a composite.</summary>
    public CompositeStep(string label, IReadOnlyList<IUndoStep> steps)
    {
        Label = label;
        _steps = steps;
    }

    /// <inheritdoc />
    public string Label { get; }

    /// <summary>Number of contained steps.</summary>
    public int Count => _steps.Count;

    /// <inheritdoc />
    public void Undo()
    {
        for (var i = _steps.Count - 1; i >= 0; i--)
        {
            _steps[i].Undo();
        }
    }

    /// <inheritdoc />
    public void Redo()
    {
        for (var i = 0; i < _steps.Count; i++)
        {
            _steps[i].Redo();
        }
    }
}

/// <summary>An open group of edits that becomes one undo item when disposed.</summary>
public sealed class UndoTransaction : IDisposable
{
    private readonly UndoManager _owner;
    private bool _done;

    internal UndoTransaction(UndoManager owner) => _owner = owner;

    /// <summary>
    /// Reverts everything recorded in the transaction and discards it (for a
    /// gesture that fails half way). Only the outermost transaction can cancel.
    /// </summary>
    public void Cancel()
    {
        if (!_done)
        {
            _done = true;
            _owner.EndTransaction(cancel: true);
        }
    }

    /// <summary>Closes the transaction, recording the group as one undo item.</summary>
    public void Dispose()
    {
        if (!_done)
        {
            _done = true;
            _owner.EndTransaction(cancel: false);
        }
    }
}

/// <summary>
/// A bounded undo/redo journal of reversible steps. Edits are recorded as they
/// happen (see <see cref="GraphRecorder"/>) and grouped by transactions, so a
/// gesture that touches many things is one Ctrl+Z. The manager knows nothing
/// about Navisworks: it reverts the graph, never the document.
/// </summary>
public sealed class UndoManager
{
    private readonly List<IUndoStep> _undo = new List<IUndoStep>();
    private readonly List<IUndoStep> _redo = new List<IUndoStep>();
    private List<IUndoStep>? _transaction;
    private string _transactionLabel = string.Empty;
    private int _depth;
    private int _suspended;
    private DateTime _lastRecord = DateTime.MinValue;
    private int _capacity = 200;

    /// <summary>Raised whenever the stacks change (drives menu/toolbar state).</summary>
    public event EventHandler? Changed;

    /// <summary>Time source; tests replace it.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    /// <summary>Outside a transaction, consecutive same-kind steps closer together than this merge.</summary>
    public TimeSpan CoalesceWindow { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Maximum undo depth; the oldest step is dropped beyond it.</summary>
    public int Capacity
    {
        get => _capacity;
        set
        {
            _capacity = Math.Max(1, value);
            Trim();
        }
    }

    /// <summary>True while an undo or redo is being applied (recording is ignored).</summary>
    public bool IsReplaying { get; private set; }

    /// <summary>True while recording is suspended (e.g. during a graph run).</summary>
    public bool IsSuspended => _suspended > 0;

    /// <summary>True when there is something to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>True when there is something to redo.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Label of the step Undo would revert, or null.</summary>
    public string? UndoLabel => _undo.Count > 0 ? _undo[_undo.Count - 1].Label : null;

    /// <summary>Label of the step Redo would re-apply, or null.</summary>
    public string? RedoLabel => _redo.Count > 0 ? _redo[_redo.Count - 1].Label : null;

    /// <summary>Number of undoable items.</summary>
    public int UndoCount => _undo.Count;

    /// <summary>Number of redoable items.</summary>
    public int RedoCount => _redo.Count;

    /// <summary>True while a transaction is open.</summary>
    public bool InTransaction => _depth > 0;

    /// <summary>The labels of the undoable items, oldest first (the last one is what Undo would revert).</summary>
    public IReadOnlyList<string> UndoLabels => _undo.Select(step => step.Label).ToList();

    /// <summary>The labels of the redoable items in the order Redo would re-apply them (the next one first).</summary>
    public IReadOnlyList<string> RedoLabels => _redo.Select(step => step.Label).Reverse().ToList();

    /// <summary>
    /// Moves through the history until exactly <paramref name="undoCount"/> items are undoable: undoing when that is fewer than
    /// now, redoing when it is more. 0 is the state the history starts from.
    /// </summary>
    /// <param name="undoCount">The wanted <see cref="UndoCount"/>, from 0 to UndoCount + RedoCount.</param>
    /// <returns>How many items were undone (negative) or redone (positive).</returns>
    public int JumpTo(int undoCount)
    {
        undoCount = Math.Max(0, Math.Min(undoCount, _undo.Count + _redo.Count));
        var moved = 0;
        while (_undo.Count > undoCount && Undo() != null)
        {
            moved--;
        }

        while (_undo.Count < undoCount && Redo() != null)
        {
            moved++;
        }

        return moved;
    }

    /// <summary>
    /// Opens a transaction: everything recorded until it is disposed becomes one
    /// undo item named <paramref name="label"/>. Transactions nest; only the
    /// outermost label and boundary count.
    /// </summary>
    public UndoTransaction Begin(string label)
    {
        if (_depth == 0)
        {
            _transaction = new List<IUndoStep>();
            _transactionLabel = label;
        }

        _depth++;
        return new UndoTransaction(this);
    }

    /// <summary>Renames the open transaction (a drag that turned into an insertion is no longer just a "Move").</summary>
    /// <param name="label">The new name of the undo item.</param>
    public void RelabelTransaction(string label)
    {
        if (_depth > 0)
        {
            _transactionLabel = label;
        }
    }

    /// <summary>Suspends recording until the returned token is disposed (used around graph runs, whose property writes are not user edits).</summary>
    public IDisposable Suspend()
    {
        _suspended++;
        return new Resume(this);
    }

    /// <summary>Records a step (ignored while replaying or suspended).</summary>
    public void Record(IUndoStep step)
    {
        if (step == null)
        {
            throw new ArgumentNullException(nameof(step));
        }

        if (IsReplaying || _suspended > 0)
        {
            return;
        }

        if (_transaction != null)
        {
            if (_transaction.Count > 0 && _transaction[_transaction.Count - 1] is ICoalescingStep open && open.TryMerge(step))
            {
                return;
            }

            _transaction.Add(step);
            return;
        }

        _redo.Clear();
        var now = Clock();
        if (_undo.Count > 0 && now - _lastRecord <= CoalesceWindow &&
            _undo[_undo.Count - 1] is ICoalescingStep top && top.TryMerge(step))
        {
            _lastRecord = now;
            RaiseChanged();
            return;
        }

        _undo.Add(step);
        _lastRecord = now;
        Trim();
        RaiseChanged();
    }

    /// <summary>Reverts the most recent item.</summary>
    /// <returns>The label of the reverted item, or null when nothing was undone.</returns>
    public string? Undo()
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        var step = _undo[_undo.Count - 1];
        _undo.RemoveAt(_undo.Count - 1);
        Replay(() => step.Undo());
        _redo.Add(step);
        _lastRecord = DateTime.MinValue;
        RaiseChanged();
        return step.Label;
    }

    /// <summary>Re-applies the most recently undone item.</summary>
    /// <returns>The label of the re-applied item, or null when nothing was redone.</returns>
    public string? Redo()
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        var step = _redo[_redo.Count - 1];
        _redo.RemoveAt(_redo.Count - 1);
        Replay(() => step.Redo());
        _undo.Add(step);
        _lastRecord = DateTime.MinValue;
        RaiseChanged();
        return step.Label;
    }

    /// <summary>Forgets all history (new or opened graph).</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _transaction = null;
        _depth = 0;
        _lastRecord = DateTime.MinValue;
        RaiseChanged();
    }

    internal void EndTransaction(bool cancel)
    {
        if (_depth == 0)
        {
            return;
        }

        _depth--;
        if (_depth > 0)
        {
            return;
        }

        var steps = _transaction ?? new List<IUndoStep>();
        var label = _transactionLabel;
        _transaction = null;

        if (cancel)
        {
            Replay(() =>
            {
                for (var i = steps.Count - 1; i >= 0; i--)
                {
                    steps[i].Undo();
                }
            });
            return;
        }

        if (steps.Count == 0)
        {
            return;
        }

        _redo.Clear();
        _undo.Add(steps.Count == 1 && string.IsNullOrEmpty(label) ? steps[0] : new CompositeStep(string.IsNullOrEmpty(label) ? steps[0].Label : label, steps));
        _lastRecord = DateTime.MinValue;
        Trim();
        RaiseChanged();
    }

    private void Replay(Action action)
    {
        IsReplaying = true;
        try
        {
            action();
        }
        catch
        {
            // The graph may be half-reverted: history no longer describes it.
            _undo.Clear();
            _redo.Clear();
            RaiseChanged();
            throw;
        }
        finally
        {
            IsReplaying = false;
        }
    }

    private void Trim()
    {
        while (_undo.Count > _capacity)
        {
            _undo.RemoveAt(0);
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed class Resume : IDisposable
    {
        private UndoManager? _owner;

        public Resume(UndoManager owner) => _owner = owner;

        public void Dispose()
        {
            if (_owner != null)
            {
                _owner._suspended--;
                _owner = null;
            }
        }
    }
}
