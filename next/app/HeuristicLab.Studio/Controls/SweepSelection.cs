using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Controls;

/// <summary>
/// Selecting several rows of a list: a click selects one row, Ctrl+click adds or removes a row,
/// Shift+click selects the rows from the last one clicked, and pressing and sweeping over rows selects
/// them all. Rows are the visible controls with the class "selectrow" whose DataContext is IPickable;
/// tick boxes and expander arrows inside rows keep working as before.
/// </summary>
public sealed class SweepSelection {
  private readonly Control root;
  private readonly Func<IEnumerable<IPickable>> all;
  private IPickable? anchor;
  private bool sweeping;

  /// <param name="all">Every row's item, also those not shown now (e.g. in collapsed branches): a plain click clears them.</param>
  private SweepSelection(Control root, Func<IEnumerable<IPickable>> all) {
    this.root = root;
    this.all = all;
    root.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    root.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel, handledEventsToo: true);
    root.AddHandler(InputElement.PointerReleasedEvent, (_, _) => sweeping = false, RoutingStrategies.Tunnel, handledEventsToo: true);
  }

  public static SweepSelection Attach(Control root, Func<IEnumerable<IPickable>> all) => new(root, all);

  /// <summary>The rows shown, top to bottom.</summary>
  private List<(Control Row, IPickable Item, double Top, double Bottom)> Rows() =>
    root.GetVisualDescendants().OfType<Control>()
      .Where(c => c.Classes.Contains("selectrow") && c.IsEffectivelyVisible && c.DataContext is IPickable)
      .Select(c => (Row: c, Item: (IPickable)c.DataContext!, Top: c.TranslatePoint(default, root)?.Y ?? 0))
      .Select(r => (r.Row, r.Item, r.Top, r.Top + r.Row.Bounds.Height))
      .OrderBy(r => r.Item3).ToList();

  /// <summary>The row at this height; above the first or below the last row: that one (while sweeping).</summary>
  private IPickable? ItemAt(Point point, bool nearest) {
    var rows = Rows();
    foreach (var r in rows) if (point.Y >= r.Top && point.Y < r.Bottom) return r.Item;
    if (!nearest || rows.Count == 0) return null;
    return point.Y < rows[0].Top ? rows[0].Item : point.Y >= rows[^1].Bottom ? rows[^1].Item : null;
  }

  private void Pressed(object? sender, PointerPressedEventArgs e) {
    if (!e.GetCurrentPoint(root).Properties.IsLeftButtonPressed) return;
    // a tick box or an expander arrow does its own thing
    for (var v = e.Source as Visual; v != null && v != root; v = v.GetVisualParent())
      if (v is ToggleButton) return;
    var point = e.GetPosition(root);
    if (ItemAt(point, nearest: false) is not { } item) return;
    var keys = e.KeyModifiers;
    if (keys.HasFlag(KeyModifiers.Control) || keys.HasFlag(KeyModifiers.Meta)) {
      item.IsSelected = !item.IsSelected;
      anchor = item;
    } else if (keys.HasFlag(KeyModifiers.Shift) && anchor != null) {
      SelectRange(anchor, item);
    } else {
      SelectOnly(item);
      anchor = item;
      sweeping = true;
    }
  }

  private void Moved(object? sender, PointerEventArgs e) {
    if (!sweeping || anchor == null) return;
    if (!e.GetCurrentPoint(root).Properties.IsLeftButtonPressed) { sweeping = false; return; }
    if (ItemAt(e.GetPosition(root), nearest: true) is { } item) SelectRange(anchor, item);
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
