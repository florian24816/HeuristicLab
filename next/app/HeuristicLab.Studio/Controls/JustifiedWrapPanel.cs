using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace HeuristicLab.Studio.Controls;

/// <summary>
/// A wrap panel whose rows all reach from the left to the right edge: as many children per row as fit at their own
/// width, the space left over shared out among them (the Results tab's selection state buttons).
/// </summary>
public sealed class JustifiedWrapPanel : Panel {
  public static readonly StyledProperty<double> ItemSpacingProperty = AvaloniaProperty.Register<JustifiedWrapPanel, double>(nameof(ItemSpacing));
  public static readonly StyledProperty<double> LineSpacingProperty = AvaloniaProperty.Register<JustifiedWrapPanel, double>(nameof(LineSpacing));

  static JustifiedWrapPanel() => AffectsMeasure<JustifiedWrapPanel>(ItemSpacingProperty, LineSpacingProperty);

  public double ItemSpacing { get => GetValue(ItemSpacingProperty); set => SetValue(ItemSpacingProperty, value); }
  public double LineSpacing { get => GetValue(LineSpacingProperty); set => SetValue(LineSpacingProperty, value); }

  /// <summary>The visible children in rows that fit <paramref name="width"/> (a child wider than it gets a row alone).</summary>
  private List<List<Control>> Rows(double width) {
    var rows = new List<List<Control>>();
    List<Control>? row = null;
    double used = 0;
    foreach (var child in Children) {
      if (!child.IsVisible) continue;
      double w = child.DesiredSize.Width;
      if (row == null || used + ItemSpacing + w > width) {
        row = [child];
        rows.Add(row);
        used = w;
      } else {
        row.Add(child);
        used += ItemSpacing + w;
      }
    }
    return rows;
  }

  protected override Size MeasureOverride(Size availableSize) {
    foreach (var child in Children) child.Measure(Size.Infinity);
    var rows = Rows(availableSize.Width);
    double height = rows.Sum(r => r.Max(c => c.DesiredSize.Height)) + LineSpacing * Math.Max(0, rows.Count - 1);
    double natural = rows.Count == 0 ? 0 : rows.Max(r => r.Sum(c => c.DesiredSize.Width) + ItemSpacing * (r.Count - 1));
    return new Size(double.IsInfinity(availableSize.Width) ? natural : availableSize.Width, height);
  }

  protected override Size ArrangeOverride(Size finalSize) {
    double y = 0;
    foreach (var row in Rows(finalSize.Width)) {
      double height = row.Max(c => c.DesiredSize.Height);
      double extra = Math.Max(0, finalSize.Width - row.Sum(c => c.DesiredSize.Width) - ItemSpacing * (row.Count - 1)) / row.Count;
      double x = 0;
      foreach (var child in row) {
        // a child alone in its row takes the row, also when it is wider than that (very narrow: its text is cut)
        double w = row.Count == 1 ? finalSize.Width : child.DesiredSize.Width + extra;
        child.Arrange(new Rect(x, y, w, height));
        x += w + ItemSpacing;
      }
      y += height + LineSpacing;
    }
    foreach (var child in Children) if (!child.IsVisible) child.Arrange(default);
    return finalSize;
  }
}
