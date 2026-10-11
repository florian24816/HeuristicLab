using System;
using Avalonia;
using Avalonia.Controls;
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

  // ---- drag and drop: press a block, move, release where the faded preview shows it

  private void BlockPressed(object? sender, PointerPressedEventArgs e) {
    if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
    if ((sender as Control)?.DataContext is BlockViewModel { IsEditingName: false } block) {
      pressed = block;
      pressedAt = e.GetPosition(this);
    }
  }

  private void OnPointerMoved(object? sender, PointerEventArgs e) {
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
    if ((sender as Control)?.DataContext is BlockViewModel block) block.SelectCommand.Execute(null);
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
