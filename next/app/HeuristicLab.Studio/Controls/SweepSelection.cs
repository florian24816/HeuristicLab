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

namespace HeuristicLab.Studio.Controls;

/// <summary>
/// Selecting several rows of a list: a click selects one row, Ctrl+click adds or removes a row,
/// Shift+click selects the rows from the last one clicked, and pressing and dragging (on a row or on empty
/// space) draws a rectangle - as in the Experiments tab: the rows whose height it reaches get a blue frame meanwhile and are
/// selected (inverted) when let go (Ctrl: added to those selected). Rows are the visible controls with the class
/// "selectrow" whose DataContext is IPickable; tick boxes and expander arrows inside rows keep working as before.
/// </summary>
public sealed class SweepSelection {
  /// <summary>The rectangle's blue (also the frame of the rows it touches; App.axaml "bandcovered").</summary>
  public static readonly Color BandBlue = Color.Parse("#2E6FD8");

  private readonly Control root;
  private readonly Func<IEnumerable<IPickable>> all;
  private readonly string rowClass;
  private readonly Func<bool> enabled;
  private IPickable? anchor;
  private Point? start;
  private bool adds;
  private IPickable? pressed;
  private KeyModifiers keys;
  private Border? band;

  /// <param name="all">Every row's item, also those not shown now (e.g. in collapsed branches): a plain click clears them.</param>
  private SweepSelection(Control root, Func<IEnumerable<IPickable>> all, string rowClass, Func<bool>? enabled) {
    this.root = root;
    this.all = all;
    this.rowClass = rowClass;
    this.enabled = enabled ?? (() => true);
    root.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    root.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel, handledEventsToo: true);
    root.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel, handledEventsToo: true);
    // the list losing the pointer ends the rectangle (a row inside losing it to the list does not)
    root.AddHandler(InputElement.PointerCaptureLostEvent, (_, e) => { if (ReferenceEquals(e.Source, root)) Cancel(); }, RoutingStrategies.Bubble, handledEventsToo: true);
  }

  /// <param name="rowClass">The class marking rows ("selectrow": inverted when selected; a table's "row" draws its own).</param>
  /// <param name="enabled">Whether it selects now (a table that selects one row at a time: no).</param>
  public static SweepSelection Attach(Control root, Func<IEnumerable<IPickable>> all, string rowClass = "selectrow", Func<bool>? enabled = null) =>
    new(root, all, rowClass, enabled);

  /// <summary>The rows shown, top to bottom, with their place in <see cref="root"/>.</summary>
  private List<(Control Row, IPickable Item, Rect Bounds)> Rows() =>
    root.GetVisualDescendants().OfType<Control>()
      .Where(c => c.Classes.Contains(rowClass) && c.IsEffectivelyVisible && c.DataContext is IPickable)
      .Select(c => (Row: c, Item: (IPickable)c.DataContext!, Bounds: new Rect(c.TranslatePoint(default, root) ?? default, c.Bounds.Size)))
      .OrderBy(r => r.Bounds.Y).ToList();

  private IPickable? ItemAt(Point point) {
    foreach (var r in Rows()) if (r.Bounds.Contains(point)) return r.Item;
    return null;
  }

  private void Pressed(object? sender, PointerPressedEventArgs e) {
    if (!e.GetCurrentPoint(root).Properties.IsLeftButtonPressed || !enabled()) return;
    // a tick box, an expander arrow, a scroll bar or a table column's right edge does its own thing
    for (var v = e.Source as Visual; v != null && v != root; v = v.GetVisualParent())
      if (v is ToggleButton or ScrollBar or Button || v is StyledElement { Classes: var classes } && classes.Contains("columnedge")) return;
    start = e.GetPosition(root);
    pressed = ItemAt(start.Value);
    keys = e.KeyModifiers;
    adds = keys.HasFlag(KeyModifiers.Control) || keys.HasFlag(KeyModifiers.Meta);
  }

  /// <summary>A row counts as touched when the rectangle reaches its height (rows of a tree are indented, of any width).</summary>
  private static bool Touches(Rect row, Rect rect) => row.Bottom > rect.Top && row.Top < rect.Bottom;

  private static bool IsClick(Rect rect) => rect.Width <= 3 && rect.Height <= 3;

  private void Moved(object? sender, PointerEventArgs e) {
    if (start is not { } from) return;
    if (!e.GetCurrentPoint(root).Properties.IsLeftButtonPressed) { Cancel(); return; }
    var rect = new Rect(from, e.GetPosition(root)).Normalize();
    if (IsClick(rect) && band == null) return;
    if (band == null) {
      e.Pointer.Capture(root);  // the rectangle follows the pointer also outside the list
      band = ShowBand();
    }
    if (band != null) {
      band.Margin = new Thickness(rect.X, rect.Y, 0, 0);
      band.Width = rect.Width;
      band.Height = rect.Height;
    }
    foreach (var r in Rows()) r.Row.Classes.Set("bandcovered", Touches(r.Bounds, rect));
  }

  private void Released(object? sender, PointerReleasedEventArgs e) {
    if (start is not { } from) return;
    var rect = new Rect(from, e.GetPosition(root)).Normalize();
    bool swept = band != null;
    var covered = swept ? Rows().Where(r => Touches(r.Bounds, rect)).Select(r => r.Item).ToHashSet() : null;
    var item = pressed;
    Cancel();
    if (covered != null) {
      foreach (var one in all()) {
        bool on = covered.Contains(one) || adds && one.IsSelected;
        if (one.IsSelected != on) one.IsSelected = on;
      }
      if (covered.Count > 0) anchor = Rows().First(r => covered.Contains(r.Item)).Item;
      if (ReferenceEquals(e.Pointer.Captured, root)) e.Pointer.Capture(null);
      return;
    }
    // a click
    if (item == null) {
      if (!adds) foreach (var one in all()) if (one.IsSelected) one.IsSelected = false;
    } else if (adds) {
      item.IsSelected = !item.IsSelected;
      anchor = item;
    } else if (keys.HasFlag(KeyModifiers.Shift) && anchor != null) {
      SelectRange(anchor, item);
    } else {
      SelectOnly(item);
      anchor = item;
    }
  }

  /// <summary>Ends a rectangle: no frames, no rectangle.</summary>
  private void Cancel() {
    start = null;
    pressed = null;
    foreach (var r in Rows()) r.Row.Classes.Set("bandcovered", false);
    if (band != null && band.Parent is AdornerLayer layer) layer.Children.Remove(band);
    band = null;
  }

  /// <summary>The see-through blue rectangle over the list (adorner layer: placed by its margin, clipped to the list).</summary>
  private Border? ShowBand() {
    if (AdornerLayer.GetAdornerLayer(root) is not { } layer) return null;
    var border = new Border {
      Background = new SolidColorBrush(BandBlue, 0.2), BorderBrush = new SolidColorBrush(BandBlue), BorderThickness = new Thickness(1),
      IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
    };
    AdornerLayer.SetAdornedElement(border, root);
    layer.Children.Add(border);
    return border;
  }

  private void SelectOnly(IPickable item) {
    foreach (var other in all()) if (!ReferenceEquals(other, item) && other.IsSelected) other.IsSelected = false;
    item.IsSelected = true;
  }

  /// <summary>Exactly the shown rows from one to the other (both included).</summary>
  private void SelectRange(IPickable from, IPickable to) {
    var rows = Rows().Select(r => r.Item).ToList();
    int a = rows.IndexOf(from), b = rows.IndexOf(to);
    if (a < 0 || b < 0) { SelectOnly(to); return; }
    var range = rows.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1).ToHashSet();
    foreach (var item in all()) {
      bool on = range.Contains(item);
      if (item.IsSelected != on) item.IsSelected = on;
    }
  }
}
