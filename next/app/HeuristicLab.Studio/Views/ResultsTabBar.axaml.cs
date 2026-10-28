using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

/// <summary>
/// The second tab bar of the Results tab (in the main window and at the top of every results window): a click shows
/// a tab, pressing a tab and dragging it out of the bar makes it a window of its own.
/// </summary>
public partial class ResultsTabBar : UserControl {
  // a press becomes a drag after this many pixels; out of the bar by this many it becomes a window
  private const double DragDistance = 6, OutDistance = 24;
  private ResultsWindowViewModel? pressed;
  private Point pressedAt;
  private bool dragging;

  public ResultsTabBar() {
    InitializeComponent();
    LayoutUpdated += (_, _) => MarkOwn();
  }

  /// <summary>In a results window: that window's tab (highlighted there instead of what the main window shows).</summary>
  public ResultsWindowViewModel? Own {
    get => own;
    set {
      own = value;
      Bar.Classes.Set("inwindow", value != null);
    }
  }
  private ResultsWindowViewModel? own;

  // marks the window's own tab whenever the tabs are laid out (new tabs, renamed ones)
  private void MarkOwn() {
    if (Own == null) return;
    foreach (var tab in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this).OfType<Panel>().Where(p => p.Classes.Contains("resultstab") && !p.Classes.Contains("main")))
      tab.Classes.Set("own", ReferenceEquals(tab.DataContext, Own));
  }

  private MainViewModel? Main => DataContext as MainViewModel;

  private void MainTapped(object? sender, TappedEventArgs e) => Main?.SelectResults(null);

  private void TabPressed(object? sender, PointerPressedEventArgs e) {
    if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || (sender as Control)?.DataContext is not ResultsWindowViewModel results) return;
    if (e.Source is Visual v && IsInCloseButton(v)) return;
    pressed = results;
    pressedAt = e.GetPosition(this);
    dragging = false;
    e.Pointer.Capture(sender as IInputElement);
  }

  private void TabMoved(object? sender, PointerEventArgs e) {
    if (pressed == null) return;
    var point = e.GetPosition(this);
    if (!dragging && Math.Abs(point.X - pressedAt.X) < DragDistance && Math.Abs(point.Y - pressedAt.Y) < DragDistance) return;
    dragging = true;
    Cursor = new Cursor(StandardCursorType.DragMove);
    // out of the bar: the tab becomes a window where the pointer is
    if (point.Y < -OutDistance || point.Y > Bounds.Height + OutDistance) {
      var results = pressed;
      EndPress(e.Pointer);
      if (results.IsDocked) Main?.Undock(results, this.PointToScreen(point));
    }
  }

  private void TabReleased(object? sender, PointerReleasedEventArgs e) {
    var results = pressed;
    bool wasDragging = dragging;
    EndPress(e.Pointer);
    if (results != null && !wasDragging) Main?.SelectResults(results);
  }

  private void EndPress(IPointer pointer) {
    pressed = null;
    dragging = false;
    Cursor = Cursor.Default;
    pointer.Capture(null);
  }

  private static bool IsInCloseButton(Visual visual) {
    for (var v = visual; v != null; v = Avalonia.VisualTree.VisualExtensions.GetVisualParent(v))
      if (v is Button) return true;
    return false;
  }

  private void CloseTab(object? sender, RoutedEventArgs e) {
    if ((sender as Control)?.DataContext is ResultsWindowViewModel results) Main?.CloseResults(results);
    e.Handled = true;
  }

  /// <summary>Whether a point on the screen lies on this bar (a results window moved there docks back).</summary>
  public bool Covers(PixelPoint screen) {
    if (!IsEffectivelyVisible) return false;
    var topLeft = this.PointToScreen(new Point(0, 0));
    var bottomRight = this.PointToScreen(new Point(Bounds.Width, Bounds.Height));
    return screen.X >= topLeft.X && screen.X <= bottomRight.X && screen.Y >= topLeft.Y - 8 && screen.Y <= bottomRight.Y + 8;
  }
}
