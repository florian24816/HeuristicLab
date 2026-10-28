using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

/// <summary>
/// The second tab bar of the Results tab (in the main window and at the top of every results window). A click shows
/// a tab. A dragged tab disappears and one see-through copy of it (the ghost) is shown instead: along the bar in the
/// gap where it would land (the other tabs slide aside; let go there, it moves - "Main results" stays first), away
/// from the bar following the pointer (let go well above or below the bar: a window of its own; anywhere else: back
/// where it was). A results window moved over the main window's bar is shown the same way (ShowArriving).
/// The tabs themselves stay put while dragging (only drawn shifted): changing them during a pointer event mixed up
/// names and crashed; the change happens after the button is released.
/// </summary>
public partial class ResultsTabBar : UserControl {
  // a press becomes a drag after this many pixels; this far above or below the bar a release makes a window
  private const double DragDistance = 6, OutDistance = 24, Gap = 6;
  private ResultsWindowViewModel? pressed;
  private Point pressedAt, grabOffset;
  private bool dragging;
  private Border? ghost;
  private double ghostWidth;
  private int? landing;

  public ResultsTabBar() {
    InitializeComponent();
    LayoutUpdated += (_, _) => MarkOwn();
  }

  private MainViewModel? Main => DataContext as MainViewModel;

  /// <summary>In a results window: that window's tab (highlighted there instead of what the main window shows).</summary>
  public ResultsWindowViewModel? Own {
    get => own;
    set {
      own = value;
      Bar.Classes.Set("inwindow", value != null);
    }
  }
  private ResultsWindowViewModel? own;

  private void MarkOwn() {
    if (Own == null) return;
    foreach (var (_, tab) in Tabs()) tab.Classes.Set("own", ReferenceEquals(tab.DataContext, Own));
  }

  /// <summary>The further tabs: each tab's container (laid out in the bar's order) and the tab panel in it.</summary>
  private List<(Control Container, Panel Tab)> Tabs() =>
    TabList.GetRealizedContainers()
      .Select(c => (Container: c, Tab: c.GetVisualDescendants().OfType<Panel>().FirstOrDefault(p => p.Classes.Contains("resultstab"))))
      .Where(t => t.Tab != null).Select(t => (t.Container, t.Tab!))
      .OrderBy(t => t.Container.Bounds.X).ToList();

  /// <summary>The ghost shown (null when nothing is dragged).</summary>
  public Control? Ghost => ghost;

  /// <summary>Where the dragged tab would land now (index among all further tabs, before it is taken out), null: not on the bar.</summary>
  public int? Landing => landing;

  private bool OnBar(Point point) => point.Y >= -OutDistance && point.Y <= Bounds.Height + OutDistance;

  private void MainTapped(object? sender, TappedEventArgs e) => Main?.SelectResults(null);

  // ---- dragging a tab of this bar

  private void TabPressed(object? sender, PointerPressedEventArgs e) {
    if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || (sender as Control)?.DataContext is not ResultsWindowViewModel results) return;
    if (e.Source is Visual v && v.GetSelfAndVisualAncestors().OfType<Button>().Any()) return;  // the small cross
    pressed = results;
    pressedAt = e.GetPosition(this);
    grabOffset = e.GetPosition((Visual)sender!);
    dragging = false;
    e.Pointer.Capture((IInputElement)sender!);
  }

  private void TabMoved(object? sender, PointerEventArgs e) {
    if (pressed == null) return;
    var point = e.GetPosition(this);
    if (!dragging) {
      if (Math.Abs(point.X - pressedAt.X) < DragDistance && Math.Abs(point.Y - pressedAt.Y) < DragDistance) return;
      dragging = true;
    }
    Preview(pressed, point, follow: true);
  }

  private void TabReleased(object? sender, PointerReleasedEventArgs e) {
    var results = pressed;
    bool wasDragging = dragging;
    var point = e.GetPosition(this);
    int? at = landing;
    EndPress(e.Pointer);
    if (results == null || Main is not { } main) return;
    // after the pointer event: the tabs change only then
    if (!wasDragging) Dispatcher.UIThread.Post(() => main.SelectResults(results));
    else if (at is int index) Dispatcher.UIThread.Post(() => main.MoveResults(results, index));
    else if (!OnBar(point) && results.IsDocked) {
      var screen = this.PointToScreen(point);
      Dispatcher.UIThread.Post(() => main.Undock(results, screen));
    }
  }

  private void TabCaptureLost(object? sender, PointerCaptureLostEventArgs e) {
    if (pressed != null) EndPress(null);
  }

  private void EndPress(IPointer? pointer) {
    pressed = null;
    dragging = false;
    ClearPreview();
    pointer?.Capture(null);
  }

  // ---- the preview: the dragged tab hidden, the others shifted, one ghost

  /// <summary>
  /// Shows a tab being moved: on the bar the gap where it lands (the others shifted, the ghost in the gap), off the
  /// bar the ghost at the pointer (<paramref name="follow"/>) or nothing.
  /// </summary>
  private void Preview(ResultsWindowViewModel moving, Point point, bool follow) {
    var tabs = Tabs();
    var others = tabs.Where(t => !ReferenceEquals(t.Tab.DataContext, moving)).ToList();
    foreach (var (container, tab) in tabs) tab.Opacity = ReferenceEquals(tab.DataContext, moving) ? 0 : 1;
    EnsureGhost(moving, tabs.FirstOrDefault(t => ReferenceEquals(t.Tab.DataContext, moving)).Tab);
    if (!OnBar(point)) {
      landing = null;
      foreach (var (container, _) in tabs) container.RenderTransform = null;
      if (follow) PlaceGhost(point.X - grabOffset.X, point.Y - grabOffset.Y);
      else HideGhost();
      return;
    }
    // where it lands among the others: before the first whose middle is right of the pointer (tab positions are the
    // tab list's, not the bar's: "Main results" comes first)
    double listX = this.TranslatePoint(point, TabList)?.X ?? point.X;
    int slot = others.Count(t => t.Container.Bounds.X + t.Container.Bounds.Width / 2 < listX);
    double width = ghostWidth + Gap;
    double x = tabs.Count > 0 ? tabs.Min(t => t.Container.Bounds.X) : 0;
    double ghostX = x;
    for (int i = 0; i <= others.Count; i++) {
      if (i == slot) { ghostX = x; x += width; }
      if (i == others.Count) break;
      var c = others[i].Container;
      c.RenderTransform = new TranslateTransform(x - c.Bounds.X, 0);
      x += c.Bounds.Width;
    }
    foreach (var (container, tab) in tabs.Where(t => ReferenceEquals(t.Tab.DataContext, moving))) container.RenderTransform = null;
    var origin = TabList.TranslatePoint(default, this) ?? default;
    PlaceGhost(origin.X + ghostX, origin.Y);
    // the index among all further tabs before the moved one is taken out (MainViewModel.MoveResults)
    int before = slot < others.Count ? Main!.ResultsWindows.IndexOf((ResultsWindowViewModel)others[slot].Tab.DataContext!) : Main!.ResultsWindows.Count;
    landing = before < 0 ? Main.ResultsWindows.Count : before;
  }

  private void EnsureGhost(ResultsWindowViewModel moving, Panel? tab) {
    if (ghost != null || AdornerLayer.GetAdornerLayer(this) is not { } layer) return;
    var shape = new Border { Classes = { "shape" } };
    if (moving.IsDocked) shape.RenderTransform = new SkewTransform(-18, 0);
    // the bar's grey of the current theme (light / dark)
    shape.Background = this.TryFindResource("AddWindowBrush", ActualThemeVariant, out var brush) ? brush as IBrush : Brushes.LightGray;
    var panel = new Panel {
      Children = {
        shape,
        new TextBlock { Text = moving.Name, FontStyle = FontStyle.Italic, FontSize = 15, Margin = new(16, 5, 38, 5), VerticalAlignment = VerticalAlignment.Center }
      }
    };
    ghost = new Border { Child = panel, Opacity = 0.5, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    ghost.Classes.Add("ghost");
    if (tab != null) ghost.Height = tab.Bounds.Height;
    ghost.Measure(Size.Infinity);
    // its width once (its margin, which places it, would count into its desired size later)
    ghostWidth = tab?.Bounds.Width is > 0 and var w ? w : ghost.DesiredSize.Width;
    AdornerLayer.SetAdornedElement(ghost, this);
    AdornerLayer.SetIsClipEnabled(ghost, false);  // it follows the pointer below the bar too
    layer.Children.Add(ghost);
  }

  // placed by its margin within the bar's area (the adorner layer sets an adorner's RenderTransform itself)
  private void PlaceGhost(double x, double y) {
    if (ghost == null) return;
    ghost.IsVisible = true;
    ghost.Margin = new Thickness(x, y, 0, 0);
  }

  private void HideGhost() {
    if (ghost != null) ghost.IsVisible = false;
  }

  private void ClearPreview() {
    landing = null;
    foreach (var (container, tab) in Tabs()) {
      container.RenderTransform = null;
      tab.Opacity = 1;
    }
    if (ghost?.Parent is Panel layer) layer.Children.Remove(ghost);
    ghost = null;
  }

  // ---- a results window moved over this bar (main window)

  /// <summary>A results window is over the bar at this screen point: its tab's gap and ghost there; returns the landing index.</summary>
  public int? ShowArriving(ResultsWindowViewModel results, PixelPoint screen) {
    Preview(results, this.PointToClient(screen), follow: false);
    return landing;
  }

  /// <summary>The window moved away again (or was docked).</summary>
  public void HideArriving() => ClearPreview();

  private void CloseTab(object? sender, RoutedEventArgs e) {
    if ((sender as Control)?.DataContext is ResultsWindowViewModel results && Main is { } main)
      Dispatcher.UIThread.Post(() => main.CloseResults(results));
    e.Handled = true;
  }

  /// <summary>Whether a point on the screen lies on this bar (a results window moved there would dock back).</summary>
  public bool Covers(PixelPoint screen) {
    if (!IsEffectivelyVisible) return false;
    var topLeft = this.PointToScreen(new Point(0, 0));
    var bottomRight = this.PointToScreen(new Point(Bounds.Width, Bounds.Height));
    return screen.X >= topLeft.X && screen.X <= bottomRight.X && screen.Y >= topLeft.Y - 8 && screen.Y <= bottomRight.Y + 8;
  }
}
