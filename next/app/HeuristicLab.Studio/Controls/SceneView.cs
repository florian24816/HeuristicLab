using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HeuristicLab.Next.Runtime.Visuals;

namespace HeuristicLab.Studio.Controls;

/// <summary>
/// Draws a <see cref="SceneVisual"/> (tours, routes, packings, trees, grids, Gantt charts,
/// landscapes): fitted to the control, uniformly or stretched, y up or down. Wheel zooms around
/// the pointer, dragging moves, double-click resets; the view is kept while the scene is updated
/// live and reset when another picture is shown.
/// </summary>
public class SceneView : Control {
  public static readonly StyledProperty<SceneVisual?> SceneProperty =
    AvaloniaProperty.Register<SceneView, SceneVisual?>(nameof(Scene));

  public static readonly StyledProperty<IBrush?> ForegroundProperty =
    AvaloniaProperty.Register<SceneView, IBrush?>(nameof(Foreground), Brushes.Gray);

  /// <summary>False: no legend above the drawing (it is shown elsewhere, e.g. in a floating panel in fullscreen).</summary>
  public static readonly StyledProperty<bool> ShowLegendProperty = AvaloniaProperty.Register<SceneView, bool>(nameof(ShowLegend), true);
  /// <summary>Changes when the shared hidden set was changed from outside: draw again.</summary>
  public static readonly StyledProperty<int> HiddenVersionProperty = AvaloniaProperty.Register<SceneView, int>(nameof(HiddenVersion));

  public bool ShowLegend { get => GetValue(ShowLegendProperty); set => SetValue(ShowLegendProperty, value); }
  public int HiddenVersion { get => GetValue(HiddenVersionProperty); set => SetValue(HiddenVersionProperty, value); }

  static SceneView() {
    AffectsRender<SceneView>(SceneProperty, ForegroundProperty, ShowLegendProperty, HiddenVersionProperty);
    SceneProperty.Changed.AddClassHandler<SceneView>((view, e) => {
      if ((e.OldValue as SceneVisual)?.Title != (e.NewValue as SceneVisual)?.Title) view.ResetView();
    });
  }

  public SceneVisual? Scene { get => GetValue(SceneProperty); set => SetValue(SceneProperty, value); }
  public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

  private double zoom = 1;
  private Vector pan;
  private Point? dragStart;
  private Vector panAtDragStart;

  public SceneView() {
    ClipToBounds = true;
    Background = Brushes.Transparent;
  }

  /// <summary>Hit testing needs a background.</summary>
  public IBrush? Background { get; set; }

  private void ResetView() {
    zoom = 1;
    pan = default;
    InvalidateVisual();
  }

  protected override void OnPointerWheelChanged(PointerWheelEventArgs e) {
    base.OnPointerWheelChanged(e);
    var at = e.GetPosition(this);
    double factor = e.Delta.Y > 0 ? 1.2 : 1 / 1.2;
    double newZoom = Math.Clamp(zoom * factor, 0.2, 200);
    factor = newZoom / zoom;
    // keep the point under the pointer in place
    var center = new Point(Bounds.Width / 2, Bounds.Height / 2) + pan;
    pan += (at - center) * (1 - factor);
    zoom = newZoom;
    InvalidateVisual();
    e.Handled = true;
  }

  // ---- legend: every group of shapes can be hidden and shown again by clicking its entry

  private readonly HashSet<string> ownHidden = [];

  /// <summary>Optional set of hidden entries shared with others (e.g. the same picture in fullscreen); else the control keeps its own.</summary>
  public static readonly StyledProperty<HashSet<string>?> HiddenSetProperty =
    AvaloniaProperty.Register<SceneView, HashSet<string>?>(nameof(HiddenSet));

  public HashSet<string>? HiddenSet { get => GetValue(HiddenSetProperty); set => SetValue(HiddenSetProperty, value); }

  private HashSet<string> hidden => HiddenSet ?? ownHidden;
  private readonly List<(Rect Area, string Group)> legendAreas = [];

  /// <summary>Groups hidden by the user (kept while the picture updates).</summary>
  public IReadOnlyCollection<string> HiddenGroups => hidden;

  public void Toggle(string group) {
    if (!hidden.Remove(group)) hidden.Add(group);
    InvalidateVisual();
  }

  /// <summary>The picture's legend, plus an entry for every other group it contains.</summary>
  public static IReadOnlyList<LegendEntry> Entries(SceneVisual scene) {
    var entries = scene.Legend.ToList();
    foreach (var group in scene.Shapes.GroupBy(s => s.GroupName)) {
      if (entries.Any(e => e.Label == group.Key)) continue;
      var color = group.Select(ShapeColor).FirstOrDefault(c => c != null) ?? Rgb.Gray;
      entries.Add(new LegendEntry(group.Key, color));
    }
    return entries;
  }

  private static Rgb? ShapeColor(Shape shape) => shape switch {
    PathShape p => p.Color, LineShape l => l.Color, MarkerShape m => m.Color,
    RectShape r => r.Fill ?? r.Stroke, EllipseShape e => e.Fill ?? e.Stroke, TextShape t => t.Color, _ => null
  };

  private List<(Rect Area, LegendEntry Entry)> LayoutLegend(SceneVisual scene, IBrush foreground) {
    var result = new List<(Rect, LegendEntry)>();
    double x = 16, y = 6, max = Math.Max(120, Bounds.Width - 16);
    foreach (var entry in Entries(scene)) {
      double w = 18 + Text(entry.Label, foreground, UiSize).Width + 18;
      if (x + w > max && x > 16) { x = 16; y += 22; }
      result.Add((new Rect(x, y, w, 20), entry));
      x += w;
    }
    return result;
  }

  protected override void OnPointerPressed(PointerPressedEventArgs e) {
    base.OnPointerPressed(e);
    var at = e.GetPosition(this);
    foreach (var (area, group) in legendAreas)
      if (area.Contains(at)) { Toggle(group); e.Handled = true; return; }
    if (e.ClickCount == 2) { ResetView(); return; }
    dragStart = e.GetPosition(this);
    panAtDragStart = pan;
    e.Pointer.Capture(this);
  }

  protected override void OnPointerMoved(PointerEventArgs e) {
    base.OnPointerMoved(e);
    if (dragStart is not { } start) return;
    pan = panAtDragStart + (e.GetPosition(this) - start);
    InvalidateVisual();
  }

  protected override void OnPointerReleased(PointerReleasedEventArgs e) {
    base.OnPointerReleased(e);
    dragStart = null;
    e.Pointer.Capture(null);
  }

  private static readonly Typeface Font = new(FontFamily.Default);

  /// <summary>Legend, scales and axis titles (labels inside a picture keep their own size).</summary>
  private const double UiSize = 13;
  private static readonly Typeface TitleFont = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

  private static FormattedText Text(string text, IBrush brush, double size = 11) =>
    new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, size, brush);

  private static FormattedText Title(string text, IBrush brush) =>
    new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, TitleFont, UiSize, brush);

  /// <summary>A category's name, wrapped to its slot and centered in it.</summary>
  private static FormattedText Category(string text, IBrush brush, double width) =>
    new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, UiSize, brush) {
      MaxTextWidth = Math.Max(20, width), TextAlignment = TextAlignment.Center, MaxLineCount = 3, Trimming = TextTrimming.CharacterEllipsis
    };

  private static Color ColorOf(Rgb c) => Color.FromArgb(c.A, c.R, c.G, c.B);

  /// <summary>Black text on light fills, white text on dark ones.</summary>
  private static IBrush LabelBrush(Rgb? fill) =>
    fill is { } f && f.A > 100 && 0.299 * f.R + 0.587 * f.G + 0.114 * f.B < 140 ? Brushes.White : Brushes.Black;

  private readonly ConditionalWeakTable<Rgb[], WriteableBitmap> bitmaps = new();

  private WriteableBitmap Bitmap(RasterShape raster) => bitmaps.GetValue(raster.Pixels, pixels => {
    var bitmap = new WriteableBitmap(new PixelSize(raster.Columns, raster.Rows), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
    using var buffer = bitmap.Lock();
    var row = new byte[raster.Columns * 4];
    for (int r = 0; r < raster.Rows; r++) {
      for (int c = 0; c < raster.Columns; c++) {
        var p = pixels[r * raster.Columns + c];
        row[c * 4] = p.B; row[c * 4 + 1] = p.G; row[c * 4 + 2] = p.R; row[c * 4 + 3] = p.A;
      }
      System.Runtime.InteropServices.Marshal.Copy(row, 0, buffer.Address + r * buffer.RowBytes, row.Length);
    }
    return bitmap;
  });

  private static (double MinX, double MinY, double MaxX, double MaxY)? WorldBounds(SceneVisual scene) {
    double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
    void Add(double x, double y) {
      if (!double.IsFinite(x) || !double.IsFinite(y)) return;
      minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
    }
    if (scene.Shapes.Count == 0) return null;
    foreach (var shape in scene.Shapes) {
      switch (shape) {
        case LineShape l: Add(l.From.X, l.From.Y); Add(l.To.X, l.To.Y); break;
        case PathShape p: foreach (var q in p.Points) Add(q.X, q.Y); break;
        case MarkerShape m: Add(m.At.X, m.At.Y); break;
        case RectShape r: Add(r.X, r.Y); Add(r.X + r.Width, r.Y + r.Height); break;
        case EllipseShape e: Add(e.X, e.Y); Add(e.X + e.Width, e.Y + e.Height); break;
        case RasterShape r: Add(r.X, r.Y); Add(r.X + r.Width, r.Y + r.Height); break;
        // labels placed in scene coordinates (row and column names) must stay inside the view
        case TextShape t: Add(t.At.X, t.At.Y); break;
      }
    }
    if (minX > maxX) return null;
    if (scene.XRange is { } xr) { minX = Math.Min(minX, xr.Min); maxX = Math.Max(maxX, xr.Max); }
    if (scene.YRange is { } yr) { minY = Math.Min(minY, yr.Min); maxY = Math.Max(maxY, yr.Max); }
    return (minX, minY, maxX, maxY);
  }

  public override void Render(DrawingContext context) {
    var foreground = Foreground ?? Brushes.Gray;
    if (Background != null) context.FillRectangle(Background, new Rect(Bounds.Size));
    var scene = Scene;
    if (scene == null || WorldBounds(scene) is not { } world) {
      var empty = Text("Nothing to draw yet.", foreground, UiSize);
      context.DrawText(empty, new Point((Bounds.Width - empty.Width) / 2, (Bounds.Height - empty.Height) / 2));
      return;
    }

    // room for labels outside the drawing (row names, axis ticks) and for the legend
    double rightAnchored = scene.Shapes.OfType<TextShape>().Where(t => t.Anchor == TextAnchor.Right)
      .Select(t => Text(t.Text, foreground, t.Size).Width).DefaultIfEmpty(0).Max();
    bool yScale = scene.Axes && !scene.Shapes.OfType<TextShape>().Any(t => t.Anchor == TextAnchor.Right);
    // the scale's labels and the vertical axis title left of the drawing
    double yLabels = yScale ? LineChart.NiceTicks(world.MinY, world.MaxY, 5).Select(v => Text(LineChart.Label(v), foreground, UiSize).Width).Max() + 8 : 0;
    double yTitle = scene.Axes && !string.IsNullOrEmpty(scene.YAxisTitle) ? Title(scene.YAxisTitle, foreground).Height + 6 : 0;
    double left = 16 + Math.Max(rightAnchored, yLabels + yTitle);
    var legend = ShowLegend ? LayoutLegend(scene, foreground) : [];
    double legendBottom = legend.Count > 0 ? legend.Max(l => l.Area.Bottom) + 4 : 0;
    double top = 16 + legendBottom + (scene.Shapes.OfType<TextShape>().Any(t => !hidden.Contains(t.GroupName)) ? 12 : 0);
    double right = 24;
    double plotWidth = Math.Max(1, Bounds.Width - left - right);
    // under the drawing: the scale or the categories' names (wrapped to their slots), then the axis title
    double bottom = 16;
    if (scene.Axes) {
      double xLabels = scene.XCategories.Count > 0
        ? scene.XCategories.Select(c => Category(c.Label, foreground, CategorySlot(scene, world, plotWidth)).Height).Max() + 10
        : scene.XAxisScale ? Text("0", foreground, UiSize).Height + 8 : 4;
      bottom = xLabels + (string.IsNullOrEmpty(scene.XAxisTitle) ? 0 : Title(scene.XAxisTitle, foreground).Height + 6) + 10;
    }
    var plot = new Rect(left, top, plotWidth, Math.Max(1, Bounds.Height - top - bottom));

    double width = Math.Max(world.MaxX - world.MinX, 1e-9), height = Math.Max(world.MaxY - world.MinY, 1e-9);
    if (world.MaxX - world.MinX < 1e-9) width = Math.Max(1, height);
    if (world.MaxY - world.MinY < 1e-9) height = Math.Max(1, width);
    double sx = plot.Width / width, sy = plot.Height / height;
    if (scene.Uniform) sx = sy = Math.Min(sx, sy);
    sx *= zoom; sy *= zoom;
    double cx = (world.MinX + world.MaxX) / 2, cy = (world.MinY + world.MaxY) / 2;
    var screenCenter = plot.Center + pan;
    Point Map(double x, double y) => new(screenCenter.X + (x - cx) * sx, scene.YUp ? screenCenter.Y - (y - cy) * sy : screenCenter.Y + (y - cy) * sy);
    Point MapP(Point2 p) => Map(p.X, p.Y);
    Rect MapRect(double x, double y, double w, double h) => new Rect(Map(x, y), Map(x + w, y + h)).Normalize();

    using (context.PushClip(new Rect(Bounds.Size))) {
      foreach (var shape in scene.Shapes) {
        if (hidden.Contains(shape.GroupName)) continue;
        switch (shape) {
          case RasterShape raster:
            // cells stay crisp: a lawn tile or cost cell is one color, not a blur
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
              context.DrawImage(Bitmap(raster), new Rect(0, 0, raster.Columns, raster.Rows), MapRect(raster.X, raster.Y, raster.Width, raster.Height));
            break;
          case RectShape r: {
              var rect = MapRect(r.X, r.Y, r.Width, r.Height);
              context.DrawRectangle(r.Fill is { } fill ? new SolidColorBrush(ColorOf(fill)) : null,
                r.Stroke is { } stroke ? new Pen(new SolidColorBrush(ColorOf(stroke)), r.StrokeThickness) : null, rect);
              if (!string.IsNullOrEmpty(r.Label)) {
                var label = Text(r.Label, LabelBrush(r.Fill));
                if (label.Width <= rect.Width - 2 && label.Height <= rect.Height + 2)
                  context.DrawText(label, new Point(rect.Center.X - label.Width / 2, rect.Center.Y - label.Height / 2));
              }
              break;
            }
          case EllipseShape e: {
              var rect = MapRect(e.X, e.Y, e.Width, e.Height);
              context.DrawEllipse(e.Fill is { } fill ? new SolidColorBrush(ColorOf(fill)) : null,
                e.Stroke is { } stroke ? new Pen(new SolidColorBrush(ColorOf(stroke)), 1) : null, rect.Center, rect.Width / 2, rect.Height / 2);
              break;
            }
          case LineShape l:
            context.DrawLine(new Pen(new SolidColorBrush(ColorOf(l.Color)), l.Thickness), MapP(l.From), MapP(l.To));
            break;
          case PathShape p when p.Points.Count > 1: {
              var geometry = new StreamGeometry();
              using (var g = geometry.Open()) {
                g.BeginFigure(MapP(p.Points[0]), p.Fill != null);
                foreach (var q in p.Points.Skip(1)) g.LineTo(MapP(q));
                g.EndFigure(p.Closed);
              }
              context.DrawGeometry(p.Fill is { } fill ? new SolidColorBrush(ColorOf(fill)) : null,
                new Pen(new SolidColorBrush(ColorOf(p.Color)), p.Thickness, lineJoin: PenLineJoin.Round), geometry);
              break;
            }
          case MarkerShape m: {
              var at = MapP(m.At);
              var brush = new SolidColorBrush(ColorOf(m.Color));
              double h = m.Size / 2;
              switch (m.Kind) {
                case MarkerKind.Square: context.FillRectangle(brush, new Rect(at.X - h, at.Y - h, m.Size, m.Size)); break;
                case MarkerKind.Diamond: {
                    var diamond = new StreamGeometry();
                    using (var g = diamond.Open()) {
                      g.BeginFigure(new Point(at.X, at.Y - h), true);
                      g.LineTo(new Point(at.X + h, at.Y)); g.LineTo(new Point(at.X, at.Y + h)); g.LineTo(new Point(at.X - h, at.Y));
                      g.EndFigure(true);
                    }
                    context.DrawGeometry(brush, new Pen(Brushes.White, 1), diamond);
                    break;
                  }
                default: context.DrawEllipse(brush, new Pen(Brushes.White, 1), at, h, h); break;
              }
              if (!string.IsNullOrEmpty(m.Label)) {
                var label = Text(m.Label, foreground);
                context.DrawText(label, new Point(at.X + h + 3, at.Y - label.Height / 2));
              }
              break;
            }
          case TextShape t: {
              var at = MapP(t.At);
              var label = Text(t.Text, t.Color == Rgb.Black ? foreground : new SolidColorBrush(ColorOf(t.Color)), t.Size);
              double x = t.Anchor switch { TextAnchor.Right => at.X - label.Width, TextAnchor.Left => at.X, _ => at.X - label.Width / 2 };
              context.DrawText(label, new Point(x, at.Y - label.Height / 2));
              break;
            }
        }
      }
    }

    if (scene.Axes) DrawAxes(context, scene, foreground, plot, Map, sx, sy, screenCenter, cx, cy, yScale, yLabels);

    legendAreas.Clear();
    foreach (var (area, entry) in legend) {
      bool off = hidden.Contains(entry.Label);
      var box = new Rect(area.X, area.Y + 4, 12, 12);
      // a filled box: shown; an empty one: hidden (click toggles)
      context.DrawRectangle(off ? null : new SolidColorBrush(ColorOf(entry.Color)), new Pen(new SolidColorBrush(ColorOf(entry.Color)), 1.5), box);
      var text = Text(entry.Label, off ? new SolidColorBrush(Colors.Gray, 0.6) : foreground, UiSize);
      context.DrawText(text, new Point(area.X + 18, area.Y + 10 - text.Height / 2));
      legendAreas.Add((area, entry.Label));
    }
  }

  /// <summary>Width a category's name may take: the distance to its neighbor (the whole width for one).</summary>
  private static double CategorySlot(SceneVisual scene, (double MinX, double MinY, double MaxX, double MaxY) world, double plotWidth) {
    var xs = scene.XCategories.Select(c => c.X).Distinct().Order().ToList();
    double spacing = xs.Count > 1 ? Enumerable.Range(1, xs.Count - 1).Min(i => xs[i] - xs[i - 1]) : Math.Max(world.MaxX - world.MinX, 1e-9);
    return plotWidth * spacing / Math.Max(world.MaxX - world.MinX, 1e-9) - 8;
  }

  /// <summary>Scales (or category names) along the bottom and left edges for the part of the scene that is visible, and the axis titles.</summary>
  private void DrawAxes(DrawingContext context, SceneVisual scene, IBrush foreground, Rect plot, Func<double, double, Point> map,
                        double sx, double sy, Point screenCenter, double cx, double cy, bool yScale, double yLabels) {
    double WorldX(double px) => cx + (px - screenCenter.X) / sx;
    double WorldY(double py) => scene.YUp ? cy - (py - screenCenter.Y) / sy : cy + (py - screenCenter.Y) / sy;
    var pen = new Pen(foreground, 1);
    var grid = new Pen(new SolidColorBrush(Colors.Gray, 0.25), 1);
    context.DrawLine(pen, plot.BottomLeft, plot.BottomRight);
    context.DrawLine(pen, plot.BottomLeft, plot.TopLeft);
    double xBottom = plot.Bottom;
    if (scene.XCategories.Count > 0) {
      double slot = Math.Abs(sx) * (scene.XCategories.Count > 1
        ? scene.XCategories.Select(c => c.X).Distinct().Order().Zip(scene.XCategories.Select(c => c.X).Distinct().Order().Skip(1), (a, b) => b - a).Min()
        : (plot.Width / Math.Abs(sx))) - 8;
      foreach (var category in scene.XCategories) {
        double px = map(category.X, 0).X;
        if (px < plot.Left - 0.5 || px > plot.Right + 0.5) continue;
        context.DrawLine(pen, new Point(px, plot.Bottom), new Point(px, plot.Bottom + 4));
        bool off = category.Group != null && hidden.Contains(category.Group);
        var label = Category(category.Label, off ? new SolidColorBrush(Colors.Gray, 0.6) : foreground, slot);
        context.DrawText(label, new Point(px - label.MaxTextWidth / 2, plot.Bottom + 8));
        xBottom = Math.Max(xBottom, plot.Bottom + 8 + label.Height);
      }
    } else if (scene.XAxisScale) {
      double x0 = WorldX(plot.Left), x1 = WorldX(plot.Right);
      foreach (var x in LineChart.NiceTicks(Math.Min(x0, x1), Math.Max(x0, x1), 6)) {
        double px = map(x, 0).X;
        if (px < plot.Left - 0.5 || px > plot.Right + 0.5) continue;
        context.DrawLine(grid, new Point(px, plot.Top), new Point(px, plot.Bottom));
        var label = Text(LineChart.Label(x), foreground, UiSize);
        context.DrawText(label, new Point(px - label.Width / 2, plot.Bottom + 6));
        xBottom = Math.Max(xBottom, plot.Bottom + 6 + label.Height);
      }
    }
    if (yScale) {
      double y0 = WorldY(plot.Bottom), y1 = WorldY(plot.Top);
      foreach (var y in LineChart.NiceTicks(Math.Min(y0, y1), Math.Max(y0, y1), 5)) {
        double py = map(0, y).Y;
        if (py < plot.Top - 0.5 || py > plot.Bottom + 0.5) continue;
        context.DrawLine(grid, new Point(plot.Left, py), new Point(plot.Right, py));
        var label = Text(LineChart.Label(y), foreground, UiSize);
        context.DrawText(label, new Point(plot.Left - label.Width - 8, py - label.Height / 2));
      }
    }
    if (!string.IsNullOrEmpty(scene.XAxisTitle)) {
      var title = Title(scene.XAxisTitle, foreground);
      context.DrawText(title, new Point(plot.Center.X - title.Width / 2, xBottom + 6));
    }
    if (!string.IsNullOrEmpty(scene.YAxisTitle)) {
      // vertical, left of the scale, centered on the drawing
      var title = Title(scene.YAxisTitle, foreground);
      double x = plot.Left - yLabels - 6 - title.Height / 2;
      using (context.PushTransform(Matrix.CreateTranslation(-title.Width / 2, -title.Height / 2) * Matrix.CreateRotation(-Math.PI / 2)
                                   * Matrix.CreateTranslation(Math.Max(title.Height / 2 + 2, x), plot.Center.Y)))
        context.DrawText(title, new Point(0, 0));
    }
  }
}
