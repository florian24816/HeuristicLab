using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace HeuristicLab.Studio.Controls;

/// <summary>
/// The mouse pointer over a table column's right edge: "&lt;-|-&gt;" (a click expands it) or "-&gt;|&lt;-" (a click
/// makes it regular again). Drawn once, large (48 px; the 32 px one was hard to see): black with a white outline, the
/// hot spot on the line.
/// </summary>
public static class ColumnCursors {
  private const int Size = 48;

  public static Cursor Expand { get; } = Make(outward: true);
  public static Cursor Shrink { get; } = Make(outward: false);

  private static Cursor Make(bool outward) {
    try {
      var bitmap = new RenderTargetBitmap(new PixelSize(Size, Size));
      using (var dc = bitmap.CreateDrawingContext()) {
        const double mid = Size / 2.0;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open()) {
          // the line
          g.BeginFigure(new Point(mid - 2, 8), true); g.LineTo(new Point(mid + 2, 8)); g.LineTo(new Point(mid + 2, 40)); g.LineTo(new Point(mid - 2, 40)); g.EndFigure(true);
          // an arrow on each side: away from the line (expand) or towards it (shrink)
          foreach (var side in new[] { -1, 1 }) {
            double near = mid + side * 5, far = mid + side * 22;
            double tip = outward ? far : near, back = outward ? near : far;
            double head = outward ? tip - side * 9 : tip + side * 9;
            g.BeginFigure(new Point(back, mid - 2.5), true);
            g.LineTo(new Point(head, mid - 2.5)); g.LineTo(new Point(head, mid - 8)); g.LineTo(new Point(tip, mid));
            g.LineTo(new Point(head, mid + 8)); g.LineTo(new Point(head, mid + 2.5)); g.LineTo(new Point(back, mid + 2.5));
            g.EndFigure(true);
          }
        }
        dc.DrawGeometry(Brushes.Black, new Pen(Brushes.White, 2), geometry);
      }
      return new Cursor(bitmap, new PixelPoint(Size / 2, Size / 2));
    } catch {
      return new Cursor(StandardCursorType.SizeWestEast);  // no custom cursors on this platform
    }
  }
}
