using System.ComponentModel;
using System.Windows;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// Wraps a canvas <see cref="NoteModel"/> (free-floating text, no execution semantics).
/// </summary>
public class NoteViewModel : CanvasItemViewModel
{
    /// <summary>Creates the wrapper and syncs the initial position.</summary>
    /// <param name="model">The wrapped note.</param>
    public NoteViewModel(NoteModel model)
    {
        Model = model;
        SetLocationFromModel(new Point(model.X, model.Y));
        model.PropertyChanged += OnModelPropertyChanged;
    }

    /// <summary>The wrapped Core note.</summary>
    public NoteModel Model { get; }

    /// <summary>Note text (two-way editable on the canvas).</summary>
    public string Text
    {
        get => Model.Text;
        set => Model.Text = value;
    }

    /// <summary>Detaches model event handlers. Call when the note leaves the canvas.</summary>
    public void Detach()
    {
        Model.PropertyChanged -= OnModelPropertyChanged;
    }

    /// <inheritdoc />
    protected override void OnLocationChanged(Point location)
    {
        Model.X = location.X;
        Model.Y = location.Y;
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(NoteModel.Text):
                OnPropertyChanged(nameof(Text));
                break;
            case nameof(NoteModel.X):
            case nameof(NoteModel.Y):
                SetLocationFromModel(new Point(Model.X, Model.Y));
                break;
        }
    }
}
