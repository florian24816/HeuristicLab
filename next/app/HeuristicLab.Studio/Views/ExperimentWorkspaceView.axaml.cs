using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

public partial class ExperimentWorkspaceView : UserControl {
  // a press becomes a drag after moving this far (pixels)
  private const double DragDistance = 6, ScrollMargin = 32, ScrollStep = 12;
  private BlockViewModel? pressed;
  private Point pressedAt;
  private bool dragging;
  // picking blocks with a rectangle: where it started (in the band layer), whether Ctrl adds to the picked ones
  private Point? bandStart;
  private bool bandAdds;

  public ExperimentWorkspaceView() {
    InitializeComponent();
    AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    AddHandler(PointerCaptureLostEvent, (_, _) => CancelDrag(), RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);
  }

  // Escape cancels a drag wherever the keyboard focus is: listened to on the window while dragging
  private void EscapePressed(object? sender, KeyEventArgs e) {
    if (e.Key == Key.Escape && dragging) {
      CancelDrag();
      e.Handled = true;
    }
  }

  private void ListenForEscape(bool listen) {
    if (TopLevel.GetTopLevel(this) is not { } top) return;
    top.RemoveHandler(KeyDownEvent, EscapePressed);
    if (listen) top.AddHandler(KeyDownEvent, EscapePressed, RoutingStrategies.Tunnel);
  }

  private ExperimentWorkspaceViewModel? Workspace => DataContext as ExperimentWorkspaceViewModel;
  private ExperimentWorkspaceViewModel? subscribed;

  protected override void OnDataContextChanged(EventArgs e) {
    base.OnDataContextChanged(e);
    if (subscribed != null) subscribed.FocusMultiFieldRequested -= FocusMultiField;
    subscribed = Workspace;
    if (subscribed != null) subscribed.FocusMultiFieldRequested += FocusMultiField;
  }

  // ---- editing a field across algorithms: the keyboard goes to the purple field

  /// <summary>Focuses the purple field once it is shown: a text is selected for typing over, a choice's list opens (arrows + Enter, or click).</summary>
  private void FocusMultiField() => FocusMultiField(attempts: 6);

  private void FocusMultiField(int attempts) =>
    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
      var field = this.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Classes.Contains("multi") && c.Classes.Contains("field") && c.IsEffectivelyVisible);
      switch (field) {
        case TextBox text:
          text.Focus(NavigationMethod.Tab);
          text.SelectAll();
          break;
        case ComboBox choice:
          choice.Focus(NavigationMethod.Tab);
          choice.IsDropDownOpen = true;
          break;
        case Control other:
          other.Focus(NavigationMethod.Tab);
          break;
        case null when attempts > 0:
          FocusMultiField(attempts - 1);  // not laid out yet
          break;
      }
    }, Avalonia.Threading.DispatcherPriority.Background);

  // ---- drag and drop: press a block, move, release where the faded preview shows it

  private void BlockPressed(object? sender, PointerPressedEventArgs e) {
    if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
    // Shift+click on a purple algorithm: stop editing a field across algorithms
    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && (sender as Control)?.DataContext is BlockViewModel { IsMultiMember: true }) {
      Workspace?.ExitMultiEdit();
      e.Handled = true;
      return;
    }
    if ((sender as Control)?.DataContext is BlockViewModel { IsEditingName: false } block) {
      pressed = block;
      pressedAt = e.GetPosition(this);
    }
  }

  // ---- picking several blocks: Ctrl+click, or press on empty space and sweep a rectangle

  private void TreePressed(object? sender, PointerPressedEventArgs e) {
    if (pressed != null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;  // a block: dragging
    for (var v = e.Source as Visual; v != null && v != sender; v = v.GetVisualParent())
      if (v is Button or ToggleButton or TextBox) return;
    var layer = this.FindControl<Canvas>("BandLayer")!;
    bandStart = e.GetPosition(layer);
    bandAdds = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
    e.Pointer.Capture(this);
  }

  private void UpdateBand(Point at) {
    var layer = this.FindControl<Canvas>("BandLayer")!;
    var band = this.FindControl<Border>("Band")!;
    if (bandStart is not { } start) return;
    var rect = new Rect(start, at).Normalize();
    Canvas.SetLeft(band, rect.X);
    Canvas.SetTop(band, rect.Y);
    band.Width = rect.Width;
    band.Height = rect.Height;
    band.IsVisible = rect.Width > 3 || rect.Height > 3;
  }

  private void EndBand(Point at) {
    var layer = this.FindControl<Canvas>("BandLayer")!;
    var band = this.FindControl<Border>("Band")!;
    if (bandStart is not { } start || Workspace is not { } workspace) return;
    var rect = new Rect(start, at).Normalize();
    bandStart = null;
    band.IsVisible = false;
    if (rect.Width <= 3 && rect.Height <= 3) {
      if (!bandAdds) workspace.ClearPicks();  // a click on empty space
      return;
    }
    var covered = this.GetVisualDescendants().OfType<Border>()
      .Where(b => b.Classes.Contains("block") && b.IsEffectivelyVisible && b.DataContext is BlockViewModel)
      .Where(b => b.TranslatePoint(default, layer) is { } p && new Rect(p, b.Bounds.Size).Intersects(rect))
      .Select(b => (BlockViewModel)b.DataContext!);
    workspace.Pick(covered, bandAdds);
  }

  private void OnPointerMoved(object? sender, PointerEventArgs e) {
    if (bandStart != null) {
      UpdateBand(e.GetPosition(this.FindControl<Canvas>("BandLayer")!));
      return;
    }
    if (pressed == null || Workspace is not { } workspace) return;
    var point = e.GetPosition(this);
    if (!dragging) {
      if (Math.Abs(point.X - pressedAt.X) < DragDistance && Math.Abs(point.Y - pressedAt.Y) < DragDistance) return;
      if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !workspace.BeginDrag(pressed)) { pressed = null; return; }
      dragging = true;
      ListenForEscape(true);
      e.Pointer.Capture(this);
      Cursor = new Cursor(StandardCursorType.DragMove);
    }
    AutoScroll(point);
    workspace.PreviewDrop(TargetAt(point, workspace));
  }

  private void OnPointerReleased(object? sender, PointerReleasedEventArgs e) {
    if (bandStart != null) {
      EndBand(e.GetPosition(this.FindControl<Canvas>("BandLayer")!));
      e.Pointer.Capture(null);
      return;
    }
    if (dragging) {
      dragging = false;
      ListenForEscape(false);
      Cursor = Cursor.Default;
      Workspace?.EndDrag(drop: true);
      e.Pointer.Capture(null);
      e.Handled = true;
    }
    pressed = null;
  }

  private void CancelDrag() {
    if (!dragging) return;
    dragging = false;
    ListenForEscape(false);
    pressed = null;
    Cursor = Cursor.Default;
    Workspace?.EndDrag(drop: false);
  }

  /// <summary>
  /// The drop target under the pointer: a row (algorithm, batch / time-limit run header, experiment
  /// header) by its upper or lower half, a container's end (its add button or the experiment's
  /// border); over the preview itself or a gap the shown preview stays; outside the tree, none.
  /// </summary>
  private DropTarget? TargetAt(Point point, ExperimentWorkspaceViewModel workspace) {
    var tree = this.FindControl<ScrollViewer>("Tree");
    if (tree?.TranslatePoint(new Point(0, 0), this) is { } origin && !new Rect(origin, tree.Bounds.Size).Contains(point)) return null;
    for (var visual = this.InputHitTest(point) as Visual; visual != null; visual = visual.GetVisualParent()) {
      if (visual is not Control control) continue;
      if (control.Classes.Contains("ghost")) return workspace.DropPreview;
      if (control.Classes.Contains("droprow") && control.DataContext is BlockViewModel block
          && this.TranslatePoint(point, control) is { } local)
        return workspace.TargetAt(block, local.Y < control.Bounds.Height / 2);
      if (control.Classes.Contains("dropend") && control.DataContext is ContainerBlockViewModel container)
        return new DropTarget(container, container.Children.Count);
    }
    return workspace.DropPreview;
  }

  private void AutoScroll(Point point) {
    if (this.FindControl<ScrollViewer>("Tree") is not { } tree || tree.TranslatePoint(point, tree) is not { }) return;
    var local = this.TranslatePoint(point, tree)!.Value;
    double dy = local.Y < ScrollMargin ? -ScrollStep : local.Y > tree.Bounds.Height - ScrollMargin ? ScrollStep : 0;
    if (dy != 0) tree.Offset = new Vector(tree.Offset.X, Math.Clamp(tree.Offset.Y + dy, 0, Math.Max(0, tree.Extent.Height - tree.Viewport.Height)));
  }

  private void BlockTapped(object? sender, TappedEventArgs e) {
    if ((sender as Control)?.DataContext is not BlockViewModel block) return;
    // Ctrl+click: picked among several (to move together); a plain click picks nothing more
    if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)) {
      Workspace?.TogglePick(block);
      e.Handled = true;
      return;
    }
    if (ExperimentWorkspaceViewModel.Movable(block) is not { IsPicked: true }) Workspace?.ClearPicks();
    block.SelectCommand.Execute(null);
  }

  // an unsaved experiment's disk: save (or "Save as..." if it has no file yet)
  private async void StateTapped(object? sender, TappedEventArgs e) {
    if ((sender as Control)?.DataContext is not BlockViewModel { IsStateClickable: true } block) return;
    e.Handled = true;
    await block.ClickStateAsync();
  }

  // a quick second click on the disk is not a rename
  private void StateDoubleTapped(object? sender, TappedEventArgs e) {
    if ((sender as Control)?.DataContext is ExperimentBlockViewModel) e.Handled = true;
  }

  private void BlockDoubleTapped(object? sender, TappedEventArgs e) {
    if ((sender as Control)?.DataContext is BlockViewModel block) block.RenameCommand.Execute(null);
  }

  private void NameKeyDown(object? sender, KeyEventArgs e) {
    if (e.Key is Key.Enter or Key.Escape && (sender as Control)?.DataContext is BlockViewModel block) {
      block.IsEditingName = false;
      e.Handled = true;
    }
  }

  private void NameLostFocus(object? sender, RoutedEventArgs e) {
    if ((sender as Control)?.DataContext is BlockViewModel block) block.IsEditingName = false;
  }

  // focus the inline name box when renaming starts
  private void NameBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e) {
    if (e.Property == IsVisibleProperty && e.NewValue is true && sender is TextBox box) {
      box.Focus();
      box.SelectAll();
    }
  }
}
