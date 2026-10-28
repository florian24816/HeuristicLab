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
using Avalonia.VisualTree;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

/// <summary>
/// The second tab bar of the Results tab (in the main window and at the top of every results window). A click shows
/// a tab. A pressed tab can be dragged: a see-through copy follows the pointer while the tab stays where it is;
/// along the bar a faded tab shows where it would land (released there: it moves - "Main results" stays first),
/// released well outside the bar it becomes a window of its own; released anywhere else nothing happens.
/// </summary>
public partial class ResultsTabBar : UserControl {
  // a press becomes a drag after this many pixels; this far above or below the bar a release makes a window
  private const double DragDistance = 6, OutDistance = 24;
  private ResultsWindowViewModel? pressed;
  private Point pressedAt, grabOffset;
  private bool dragging;
  private Border? floating;

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

  // marks the window's own tab whenever the tabs are laid out (new tabs, renamed ones)
  private void MarkOwn() {
    if (Own == null) return;
    foreach (var tab in Tabs()) tab.Classes.Set("own", ReferenceEquals(tab.DataContext, Own));
  }

  /// <summary>The further tabs shown (no ghost), left to right.</summary>
  private List<Panel> Tabs() =>
    this.GetVisualDescendants().OfType<Panel>()
      .Where(p => p.Classes.Contains("resultstab") && !p.Classes.Contains("main") && p.DataContext is ResultsWindowViewModel)
      .OrderBy(p => p.TranslatePoint(default, this)?.X ?? 0).ToList();

  /// <summary>Where a tab dropped at this point of the bar lands: the number of further tabs left of it.</summary>
  public int IndexAt(Point point) =>
    Tabs().Count(t => t.TranslatePoint(new Point(t.Bounds.Width / 2, 0), this) is { } center && center.X < point.X);

  /// <summary>Like <see cref="IndexAt(Point)"/> for a point on the screen.</summary>
  public int IndexAt(PixelPoint screen) => IndexAt(this.PointToClient(screen));

  private bool OnBar(Point point) => point.Y >= -OutDistance && point.Y <= Bounds.Height + OutDistance;

  private void MainTapped(object? sender, TappedEventArgs e) => Main?.SelectResults(null);

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
      ShowFloating(pressed, (Control)sender!);
    }
    MoveFloating(point);
    Main?.PreviewTab(OnBar(point) && Main.ResultsWindows.Contains(pressed) ? pressed : null, IndexAt(point));
  }

  private void TabReleased(object? sender, PointerReleasedEventArgs e) {
    var results = pressed;
    bool wasDragging = dragging;
    var point = e.GetPosition(this);
    EndPress(e.Pointer);
    if (results == null || Main is not { } main) return;
    if (!wasDragging) { main.SelectResults(results); return; }
    if (OnBar(point)) main.MoveResults(results, IndexAt(point));
    else if (results.IsDocked) main.Undock(results, this.PointToScreen(point));
  }

  private void TabCaptureLost(object? sender, PointerCaptureLostEventArgs e) {
    if (pressed != null) EndPress(null);
  }

  private void EndPress(IPointer? pointer) {
    pressed = null;
    dragging = false;
    HideFloating();
    Main?.PreviewTab(null, 0);
    pointer?.Capture(null);
  }

  // ---- the see-through copy following the pointer (drawn above the window's content)

  private void ShowFloating(ResultsWindowViewModel results, Control tab) {
    if (AdornerLayer.GetAdornerLayer(this) is not { } layer) return;
    floating = new Border {
      Width = tab.Bounds.Width, Height = tab.Bounds.Height, Opacity = 0.55, IsHitTestVisible = false,
      Background = (results.IsSelected && Own == null) || ReferenceEquals(results, Own) ? new SolidColorBrush(Color.Parse("#F7931D")) : (IBrush?)this.FindResource("AddWindowBrush"),
      Child = new TextBlock { Text = results.Name, FontStyle = FontStyle.Italic, FontSize = 15, Margin = new(16, 5, 10, 5), VerticalAlignment = VerticalAlignment.Center },
      HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
    };
    floating.Classes.Add("floatingtab");
    AdornerLayer.SetAdornedElement(floating, this);
    layer.Children.Add(floating);
  }

  private void MoveFloating(Point point) {
    if (floating != null) floating.RenderTransform = new TranslateTransform(point.X - grabOffset.X, point.Y - grabOffset.Y);
  }

  private void HideFloating() {
    if (floating?.Parent is Panel layer) layer.Children.Remove(floating);
    floating = null;
  }

  /// <summary>The copy following the pointer while a tab is dragged (null otherwise).</summary>
  public Control? Floating => floating;

  private void CloseTab(object? sender, RoutedEventArgs e) {
    if ((sender as Control)?.DataContext is ResultsWindowViewModel results) Main?.CloseResults(results);
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
