using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CamelGraph.Core.Editing;
using CamelGraph.UI.Services;

namespace CamelGraph.UI.Views;

/// <summary>
/// Blender-style number field. The whole field is one element that draws
/// itself (rounded background, optional slider fill, label on the left, value
/// on the right, step arrows on hover, a dot when the value is pinned) — a
/// canvas full of them costs one visual per field, not a control tree each.
/// </summary>
/// <remarks>
/// Press and drag to scrub (Shift = fine, Ctrl = snap to the step); click the
/// left/right ends to step; click the body — or Tab into it — to type (numbers
/// and arithmetic such as <c>2*3+1</c>; Enter commits, Esc reverts, Tab moves
/// on). While hovering: Ctrl+C / Ctrl+V copy and paste the value, Backspace
/// resets it, <c>-</c> negates it, Ctrl+wheel steps it. A scrub commits when
/// released (one undo step and one re-run); set <see cref="LiveCommit"/> to
/// also commit while dragging. All arithmetic lives in
/// <see cref="ScrubMath"/> (unit-tested in Core).
/// </remarks>
public sealed class ScrubNumberBox : Decorator
{
    /// <summary>The committed value (two-way bindable).</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ScrubNumberBox),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender, OnValueChanged));

    /// <summary>Hard minimum.</summary>
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(ScrubNumberBox), new FrameworkPropertyMetadata(double.MinValue, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Hard maximum.</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(ScrubNumberBox), new FrameworkPropertyMetadata(double.MaxValue, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Slider extent minimum (NaN = no fill bar).</summary>
    public static readonly DependencyProperty SoftMinimumProperty = DependencyProperty.Register(
        nameof(SoftMinimum), typeof(double), typeof(ScrubNumberBox), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Slider extent maximum (NaN = no fill bar).</summary>
    public static readonly DependencyProperty SoftMaximumProperty = DependencyProperty.Register(
        nameof(SoftMaximum), typeof(double), typeof(ScrubNumberBox), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Step for arrows, snapping and wheel.</summary>
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(ScrubNumberBox), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>True for whole numbers only.</summary>
    public static readonly DependencyProperty IsIntegerProperty = DependencyProperty.Register(
        nameof(IsInteger), typeof(bool), typeof(ScrubNumberBox), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Unit suffix shown after the value.</summary>
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(ScrubNumberBox), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Label drawn on the left inside the field.</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(ScrubNumberBox), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>True when the value is pinned (a dot marks the field).</summary>
    public static readonly DependencyProperty IsModifiedProperty = DependencyProperty.Register(
        nameof(IsModified), typeof(bool), typeof(ScrubNumberBox), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>True when there is no value or default yet (the placeholder is drawn).</summary>
    public static readonly DependencyProperty IsUnsetProperty = DependencyProperty.Register(
        nameof(IsUnset), typeof(bool), typeof(ScrubNumberBox), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Text drawn while <see cref="IsUnset"/>.</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(ScrubNumberBox), new FrameworkPropertyMetadata("—", FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Also commit while dragging (every ~150 ms) instead of only on release.</summary>
    public static readonly DependencyProperty LiveCommitProperty = DependencyProperty.Register(
        nameof(LiveCommit), typeof(bool), typeof(ScrubNumberBox), new FrameworkPropertyMetadata(false));

    /// <summary>Pixels of mouse travel per step while dragging (smaller = faster); set from the scrub-speed preference.</summary>
    public static readonly DependencyProperty PixelsPerStepProperty = DependencyProperty.Register(
        nameof(PixelsPerStep), typeof(double), typeof(ScrubNumberBox), new FrameworkPropertyMetadata(ScrubMath.PixelsPerStep));

    /// <summary>Command that returns the value to its default (Backspace while hovering).</summary>
    public static readonly DependencyProperty ResetCommandProperty = DependencyProperty.Register(
        nameof(ResetCommand), typeof(ICommand), typeof(ScrubNumberBox));

    /// <summary>Field background.</summary>
    public static readonly DependencyProperty FieldBrushProperty = BrushProperty(nameof(FieldBrush), Brushes.DimGray);

    /// <summary>Field background while hovered or dragged.</summary>
    public static readonly DependencyProperty HoverBrushProperty = BrushProperty(nameof(HoverBrush), Brushes.Gray);

    /// <summary>Slider fill colour.</summary>
    public static readonly DependencyProperty FillBrushProperty = BrushProperty(nameof(FillBrush), Brushes.SteelBlue);

    /// <summary>Value text colour.</summary>
    public static readonly DependencyProperty TextBrushProperty = BrushProperty(nameof(TextBrush), Brushes.White);

    /// <summary>Label and arrow colour.</summary>
    public static readonly DependencyProperty SubtleBrushProperty = BrushProperty(nameof(SubtleBrush), Brushes.LightGray);

    /// <summary>Accent (pinned dot, invalid-input outline uses <see cref="ErrorBrush"/>).</summary>
    public static readonly DependencyProperty AccentBrushProperty = BrushProperty(nameof(AccentBrush), Brushes.DodgerBlue);

    /// <summary>Outline drawn after rejected input.</summary>
    public static readonly DependencyProperty ErrorBrushProperty = BrushProperty(nameof(ErrorBrush), Brushes.IndianRed);

    /// <summary>Height of the field: 20 in a node, more where there is room (the Script Player).</summary>
    public static readonly DependencyProperty FieldHeightProperty = DependencyProperty.Register(
        nameof(FieldHeight), typeof(double), typeof(ScrubNumberBox),
        new FrameworkPropertyMetadata(20d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private const double ArrowZone = 14d;
    private const double DragThreshold = 3d;
    private static bool _tabbedIn;

    private enum Mode { Idle, Pressed, Dragging, Editing }

    private Mode _mode;
    private double _live;
    private double _pressValue;
    private Point _pressPoint;

    // Dragging past the edge of the screen moves the pointer to the other side; these carry the distance it jumped so the
    // value carries on smoothly, and skip the mouse events still in flight from before the jump.
    private double _wrapOffset;
    private bool _warping;
    private double _warpFromX;
    private double _warpJump;

    /// <summary>True (default) to move the pointer to the opposite screen edge when a drag reaches one, so a number can be dragged any distance.</summary>
    public static bool WrapPointerAtScreenEdge { get; set; } = true;
    private DateTime _lastLiveCommit = DateTime.MinValue;
    private bool _invalid;
    private TextBox? _editor;
    private bool _committing;

    static ScrubNumberBox()
    {
        FocusableProperty.OverrideMetadata(typeof(ScrubNumberBox), new FrameworkPropertyMetadata(true));
        CursorProperty.OverrideMetadata(typeof(ScrubNumberBox), new FrameworkPropertyMetadata(Cursors.SizeWE));
    }

    /// <summary>Creates the field.</summary>
    public ScrubNumberBox()
    {
        SnapsToDevicePixels = true;
        _live = Value;
    }

    /// <summary>The field the pointer is currently over (target of the hover shortcuts).</summary>
    public static ScrubNumberBox? Hovered { get; private set; }

    /// <inheritdoc cref="ValueProperty" />
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <inheritdoc cref="MinimumProperty" />
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    /// <inheritdoc cref="MaximumProperty" />
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    /// <inheritdoc cref="SoftMinimumProperty" />
    public double SoftMinimum { get => (double)GetValue(SoftMinimumProperty); set => SetValue(SoftMinimumProperty, value); }

    /// <inheritdoc cref="SoftMaximumProperty" />
    public double SoftMaximum { get => (double)GetValue(SoftMaximumProperty); set => SetValue(SoftMaximumProperty, value); }

    /// <inheritdoc cref="StepProperty" />
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }

    /// <inheritdoc cref="IsIntegerProperty" />
    public bool IsInteger { get => (bool)GetValue(IsIntegerProperty); set => SetValue(IsIntegerProperty, value); }

    /// <inheritdoc cref="UnitProperty" />
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    /// <inheritdoc cref="LabelProperty" />
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <inheritdoc cref="IsModifiedProperty" />
    public bool IsModified { get => (bool)GetValue(IsModifiedProperty); set => SetValue(IsModifiedProperty, value); }

    /// <inheritdoc cref="IsUnsetProperty" />
    public bool IsUnset { get => (bool)GetValue(IsUnsetProperty); set => SetValue(IsUnsetProperty, value); }

    /// <inheritdoc cref="PlaceholderProperty" />
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    /// <inheritdoc cref="PixelsPerStepProperty" />
    public double PixelsPerStep { get => (double)GetValue(PixelsPerStepProperty); set => SetValue(PixelsPerStepProperty, value); }

    /// <inheritdoc cref="FieldHeightProperty" />
    public double FieldHeight { get => (double)GetValue(FieldHeightProperty); set => SetValue(FieldHeightProperty, value); }

    /// <inheritdoc cref="LiveCommitProperty" />
    public bool LiveCommit { get => (bool)GetValue(LiveCommitProperty); set => SetValue(LiveCommitProperty, value); }

    /// <inheritdoc cref="ResetCommandProperty" />
    public ICommand? ResetCommand { get => (ICommand?)GetValue(ResetCommandProperty); set => SetValue(ResetCommandProperty, value); }

    /// <inheritdoc cref="FieldBrushProperty" />
    public Brush FieldBrush { get => (Brush)GetValue(FieldBrushProperty); set => SetValue(FieldBrushProperty, value); }

    /// <inheritdoc cref="HoverBrushProperty" />
    public Brush HoverBrush { get => (Brush)GetValue(HoverBrushProperty); set => SetValue(HoverBrushProperty, value); }

    /// <inheritdoc cref="FillBrushProperty" />
    public Brush FillBrush { get => (Brush)GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }

    /// <inheritdoc cref="TextBrushProperty" />
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    /// <inheritdoc cref="SubtleBrushProperty" />
    public Brush SubtleBrush { get => (Brush)GetValue(SubtleBrushProperty); set => SetValue(SubtleBrushProperty, value); }

    /// <inheritdoc cref="AccentBrushProperty" />
    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    /// <inheritdoc cref="ErrorBrushProperty" />
    public Brush ErrorBrush { get => (Brush)GetValue(ErrorBrushProperty); set => SetValue(ErrorBrushProperty, value); }

    /// <summary>True while the user is typing into the field.</summary>
    public bool IsEditing => _mode == Mode.Editing;

    /// <summary>True while a scrub drag is in progress.</summary>
    public bool IsDragging => _mode == Mode.Dragging;

    /// <summary>The value being displayed (differs from <see cref="Value"/> during an uncommitted drag).</summary>
    public double LiveValue => _live;

    private NumberEditSpec Spec => new NumberEditSpec
    {
        Min = Minimum,
        Max = Maximum,
        SoftMin = SoftMinimum,
        SoftMax = SoftMaximum,
        Step = Step > 0d ? Step : 1d,
        IsInteger = IsInteger,
        Unit = Unit ?? string.Empty,
    };

    private static DependencyProperty BrushProperty(string name, Brush fallback) =>
        DependencyProperty.Register(name, typeof(Brush), typeof(ScrubNumberBox),
            new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (ScrubNumberBox)d;
        if (box._mode != Mode.Dragging && box._mode != Mode.Editing)
        {
            box._live = (double)e.NewValue;
        }
    }

    // ----- layout & drawing ------------------------------------------------------

    /// <inheritdoc />
    protected override Size MeasureOverride(Size constraint)
    {
        var width = double.IsInfinity(constraint.Width) ? 120d : constraint.Width;
        var height = FieldHeight;
        Child?.Measure(new Size(width, height));
        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Child?.Arrange(new Rect(0, 0, arrangeSize.Width, arrangeSize.Height));
        return arrangeSize;
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 4d || h < 4d)
        {
            return;
        }

        var rect = new Rect(0, 0, w, h);
        var hot = IsMouseOver || _mode == Mode.Dragging || _mode == Mode.Editing;
        var pen = _invalid ? new Pen(ErrorBrush, 1.5) : null;
        dc.DrawRoundedRectangle(hot ? HoverBrush : FieldBrush, pen, rect, 4, 4);

        var spec = Spec;
        if (spec.HasSoftRange && !IsUnset)
        {
            var fraction = Math.Max(0d, Math.Min(1d, (_live - spec.SoftMin) / (spec.SoftMax - spec.SoftMin)));
            if (fraction > 0d)
            {
                dc.PushClip(new RectangleGeometry(rect, 4, 4));
                dc.PushOpacity(0.45);
                dc.DrawRectangle(FillBrush, null, new Rect(0, 0, w * fraction, h));
                dc.Pop();
                dc.Pop();
            }
        }

        if (_mode == Mode.Editing)
        {
            return; // the TextBox draws the text
        }

        if (hot)
        {
            DrawArrow(dc, "‹", 3d, h);
            DrawArrow(dc, "›", w - 11d, h);
        }

        var left = 14d;
        if (IsModified)
        {
            dc.DrawEllipse(AccentBrush, null, new Point(7d, h / 2d), 2.2, 2.2);
        }

        var value = IsUnset && _mode != Mode.Dragging
            ? Placeholder
            : NumberFormat.Format(_live, spec.Step, spec.Unit);
        var valueText = Text(value, IsUnset ? SubtleBrush : TextBrush, false);
        var hasLabel = !string.IsNullOrEmpty(Label);
        if (hasLabel)
        {
            var labelText = Text(Label, SubtleBrush, false);
            labelText.MaxTextWidth = Math.Max(10d, w - left - valueText.Width - 26d);
            labelText.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(labelText, new Point(left, (h - labelText.Height) / 2d));
            dc.DrawText(valueText, new Point(w - 14d - valueText.Width, (h - valueText.Height) / 2d));
        }
        else
        {
            dc.DrawText(valueText, new Point((w - valueText.Width) / 2d, (h - valueText.Height) / 2d));
        }
    }

    private void DrawArrow(DrawingContext dc, string glyph, double x, double height)
    {
        var text = Text(glyph, SubtleBrush, false);
        dc.DrawText(text, new Point(x, (height - text.Height) / 2d - 1d));
    }

    private FormattedText Text(string value, Brush brush, bool bold)
    {
        var typeface = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
        return new FormattedText(
            value ?? string.Empty,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            11d,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    // ----- mouse: scrub, step, type --------------------------------------------------

    /// <inheritdoc />
    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        Hovered = this;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (ReferenceEquals(Hovered, this))
        {
            Hovered = null;
        }

        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_mode == Mode.Editing)
        {
            return;
        }

        base.OnMouseLeftButtonDown(e);
        _mode = Mode.Pressed;
        _pressPoint = e.GetPosition(this);
        _wrapOffset = 0d;
        _warping = false;
        _pressValue = _live;
        _invalid = false;
        CaptureMouse();
        e.Handled = true; // keep the node from starting a drag
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_mode != Mode.Pressed && _mode != Mode.Dragging)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (_warping)
        {
            // Events queued before the pointer jumped still report the old side; wait for one from the new side.
            if (Math.Abs(position.X - _warpFromX) < Math.Abs(_warpJump) / 2d)
            {
                return;
            }

            _warping = false;
        }

        var delta = position.X - _pressPoint.X + _wrapOffset;
        if (_mode == Mode.Pressed)
        {
            if (Math.Abs(delta) < DragThreshold)
            {
                return;
            }

            _mode = Mode.Dragging;
        }

        var modifiers = Keyboard.Modifiers;
        _live = ScrubMath.Scrub(
            _pressValue,
            delta,
            Spec,
            ActualWidth,
            fine: (modifiers & ModifierKeys.Shift) != 0,
            snap: (modifiers & ModifierKeys.Control) != 0,
            pixelsPerStep: PixelsPerStep);
        if (LiveCommit && (DateTime.UtcNow - _lastLiveCommit).TotalMilliseconds >= 150d)
        {
            _lastLiveCommit = DateTime.UtcNow;
            CommitValue(_live);
        }

        InvalidateVisual();
        e.Handled = true;
        if (WrapPointerAtScreenEdge)
        {
            WrapPointer(position);
        }
    }

    // At the edge of the screen: put the pointer just inside the opposite edge and remember how far it jumped.
    private void WrapPointer(Point position)
    {
        PointerWrap.ScreenSpan(out var left, out var width);
        var onScreen = PointToScreen(position);
        if (!ScrubMath.TryWrap(onScreen.X, left, width, 2d, out var newX))
        {
            return;
        }

        PointerWrap.MoveTo((int)Math.Round(newX), (int)Math.Round(onScreen.Y));
        var jump = (newX - onScreen.X) / VisualTreeHelper.GetDpi(this).DpiScaleX;
        _wrapOffset -= jump;
        _warpFromX = position.X;
        _warpJump = jump;
        _warping = true;
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_mode != Mode.Pressed && _mode != Mode.Dragging)
        {
            return;
        }

        var wasDragging = _mode == Mode.Dragging;
        _mode = Mode.Idle;
        ReleaseMouseCapture();
        e.Handled = true;

        if (wasDragging)
        {
            CommitValue(_live);
        }
        else
        {
            var x = e.GetPosition(this).X;
            if (x <= ArrowZone)
            {
                StepBy(-1, fine: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            }
            else if (x >= ActualWidth - ArrowZone)
            {
                StepBy(1, fine: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            }
            else
            {
                BeginEdit();
            }
        }

        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_mode == Mode.Dragging)
        {
            // Capture stolen mid-drag: keep what the user dragged to.
            _mode = Mode.Idle;
            CommitValue(_live);
            InvalidateVisual();
        }
        else if (_mode == Mode.Pressed)
        {
            _mode = Mode.Idle;
        }
    }

    /// <inheritdoc />
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        // Plain wheel zooms the canvas; only Ctrl+wheel steps the value.
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && _mode == Mode.Idle)
        {
            StepBy(e.Delta > 0 ? 1 : -1, fine: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            e.Handled = true;
            return;
        }

        base.OnMouseWheel(e);
    }

    /// <inheritdoc />
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (_tabbedIn && _mode == Mode.Idle && e.OriginalSource == this)
        {
            _tabbedIn = false;
            BeginEdit();
        }
    }

    // ----- hover shortcuts (routed here by the editor) ------------------------------

    /// <summary>
    /// Handles Ctrl+C, Ctrl+V, Backspace/Delete and <c>-</c> for the hovered field.
    /// Returns true when the key was consumed.
    /// </summary>
    public bool HandleHoverKey(KeyEventArgs e)
    {
        if (_mode != Mode.Idle)
        {
            return false;
        }

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (ctrl && e.Key == Key.C)
        {
            try
            {
                Clipboard.SetText(NumberFormat.Format(_live, Math.Min(Step, 0.000001d)));
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // clipboard busy: ignore
            }

            return true;
        }

        if (ctrl && e.Key == Key.V)
        {
            string? text = null;
            try
            {
                text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // clipboard busy: ignore
            }

            // "1, 2, 3" fills this field and the ones after it (a coordinate into x, y, z).
            if (DataContext is ViewModels.ConnectorViewModel connector && connector.TryPasteNumbers(text))
            {
                return true;
            }

            if (ScrubMath.TryParse(text, Spec, out var pasted))
            {
                CommitValue(pasted);
            }

            return true;
        }

        if (!ctrl && (e.Key == Key.Back || e.Key == Key.Delete))
        {
            var reset = ResetCommand;
            if (reset != null && reset.CanExecute(null))
            {
                reset.Execute(null);
            }

            return true;
        }

        if (!ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract))
        {
            CommitValue(-_live);
            return true;
        }

        return false;
    }

    // ----- typing ---------------------------------------------------------------------

    /// <summary>Starts typing: swaps in a text box holding the full-precision value.</summary>
    public void BeginEdit()
    {
        if (_mode == Mode.Editing)
        {
            return;
        }

        _mode = Mode.Editing;
        _invalid = false;
        var editor = new TextBox
        {
            Text = IsUnset ? string.Empty : NumberFormat.Format(_live, Math.Min(Spec.Step, 0.000001d)),
            FontSize = 11,
            Padding = new Thickness(6, 0, 6, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = TextBrush,
            CaretBrush = TextBrush,
            SelectionBrush = AccentBrush,
            Focusable = true,
        };
        editor.PreviewKeyDown += OnEditorKeyDown;
        editor.LostKeyboardFocus += OnEditorLostFocus;
        _editor = editor;
        Child = editor;
        InvalidateVisual();
        editor.Focus();
        editor.SelectAll();
    }

    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                EndEdit(commit: true);
                break;
            case Key.Escape:
                e.Handled = true;
                EndEdit(commit: false);
                break;
            case Key.Tab:
                e.Handled = true;
                var forward = (Keyboard.Modifiers & ModifierKeys.Shift) == 0;
                if (EndEdit(commit: true))
                {
                    _tabbedIn = true;
                    MoveFocus(new TraversalRequest(forward ? FocusNavigationDirection.Next : FocusNavigationDirection.Previous));
                    _tabbedIn = false;
                }

                break;
        }
    }

    private void OnEditorLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_mode == Mode.Editing && !_committing)
        {
            EndEdit(commit: true);
        }
    }

    /// <summary>Finishes typing; returns false when the text was rejected (the field flags it and reverts).</summary>
    private bool EndEdit(bool commit)
    {
        if (_mode != Mode.Editing || _committing)
        {
            return true;
        }

        _committing = true;
        var ok = true;
        try
        {
            var text = _editor?.Text;
            if (_editor != null)
            {
                _editor.PreviewKeyDown -= OnEditorKeyDown;
                _editor.LostKeyboardFocus -= OnEditorLostFocus;
            }

            _mode = Mode.Idle;
            Child = null;
            _editor = null;

            if (commit && !CommitText(text))
            {
                ok = false;
            }

            if (IsKeyboardFocusWithin || IsKeyboardFocused)
            {
                Focus();
            }
        }
        finally
        {
            _committing = false;
            InvalidateVisual();
        }

        return ok;
    }

    /// <summary>
    /// Applies typed text (numbers or arithmetic). Empty text is ignored (true);
    /// unparseable text flags the field with the error outline and returns false.
    /// </summary>
    public bool CommitText(string? text)
    {
        if (ScrubMath.TryParse(text, Spec, out var parsed))
        {
            _invalid = false;
            CommitValue(parsed);
            return true;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        _invalid = true;
        InvalidateVisual();
        return false;
    }

    /// <summary>True after rejected input, until the next successful edit.</summary>
    public bool IsInvalid => _invalid;

    /// <summary>Steps the value one increment up (<paramref name="direction"/> &gt; 0) or down; <paramref name="fine"/> uses a tenth of the step.</summary>
    public void StepValue(int direction, bool fine = false) => StepBy(direction, fine);

    private void StepBy(int direction, bool fine)
    {
        CommitValue(ScrubMath.StepBy(_live, direction, Spec, fine));
    }

    private void CommitValue(double value)
    {
        _live = value;
        SetCurrentValue(ValueProperty, value);
        InvalidateVisual();
    }
}
