using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Dyncamelo.Core.Graph;
using Dyncamelo.UI.Mvvm;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// Wraps a canvas <see cref="GroupModel"/>: a titled colored rectangle rendered
/// behind the nodes it spatially contains (Nodify's GroupingNode moves the
/// contained items when the group is dragged by its header).
/// </summary>
public class GroupViewModel : CanvasItemViewModel
{
    private readonly GraphEditorViewModel _owner;
    private Brush _headerBrush;
    private Brush _bodyBrush;

    /// <summary>Creates the wrapper and syncs position, size and color.</summary>
    /// <param name="owner">The editor that owns this group.</param>
    /// <param name="model">The wrapped group.</param>
    public GroupViewModel(GraphEditorViewModel owner, GroupModel model)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Model = model ?? throw new ArgumentNullException(nameof(model));

        UngroupCommand = new RelayCommand(() => _owner.Ungroup(this));
        SetColorCommand = new RelayCommand<string>(color =>
        {
            if (!string.IsNullOrEmpty(color))
            {
                Model.Color = color!;
            }
        });

        _headerBrush = Brushes.Transparent;
        _bodyBrush = Brushes.Transparent;
        UpdateBrushes();
        SetLocationFromModel(new Point(model.X, model.Y));
        model.PropertyChanged += OnModelPropertyChanged;
    }

    /// <summary>The wrapped Core group.</summary>
    public GroupModel Model { get; }

    /// <summary>Group title (editable in the header).</summary>
    public string Title
    {
        get => Model.Title;
        set => Model.Title = value;
    }

    /// <summary>
    /// Rectangle size; two-way bound to the Nodify GroupingNode's ActualSize so
    /// resizing by the thumb persists into the model.
    /// </summary>
    public Size GroupSize
    {
        get => new Size(
            Model.Width > 0 ? Model.Width : 200d,
            Model.Height > 0 ? Model.Height : 200d);
        set
        {
            Model.Width = value.Width;
            Model.Height = value.Height;
        }
    }

    /// <summary>Opaque brush used for the group header bar.</summary>
    public Brush HeaderBrush
    {
        get => _headerBrush;
        private set => SetProperty(ref _headerBrush, value);
    }

    /// <summary>Translucent brush used for the group body.</summary>
    public Brush BodyBrush
    {
        get => _bodyBrush;
        private set => SetProperty(ref _bodyBrush, value);
    }

    /// <summary>Removes the group rectangle, leaving its nodes untouched.</summary>
    public ICommand UngroupCommand { get; }

    /// <summary>Sets the group color; parameter is an ARGB hex string.</summary>
    public ICommand SetColorCommand { get; }

    /// <summary>Detaches model event handlers. Call when the group leaves the canvas.</summary>
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
            case nameof(GroupModel.Title):
                OnPropertyChanged(nameof(Title));
                break;
            case nameof(GroupModel.X):
            case nameof(GroupModel.Y):
                SetLocationFromModel(new Point(Model.X, Model.Y));
                break;
            case nameof(GroupModel.Width):
            case nameof(GroupModel.Height):
                OnPropertyChanged(nameof(GroupSize));
                break;
            case nameof(GroupModel.Color):
                UpdateBrushes();
                break;
        }
    }

    private void UpdateBrushes()
    {
        Color color;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(Model.Color);
        }
        catch (FormatException)
        {
            color = (Color)ColorConverter.ConvertFromString(GroupModel.DefaultColor);
        }

        var header = new SolidColorBrush(color);
        header.Freeze();
        var body = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B));
        body.Freeze();
        HeaderBrush = header;
        BodyBrush = body;
    }
}
