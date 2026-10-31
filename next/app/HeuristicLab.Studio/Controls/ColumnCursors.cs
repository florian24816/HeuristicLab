using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace HeuristicLab.Studio.Controls;

/// <summary>
/// The mouse pointer over a table column's right edge: "&lt;-|-&gt;" (a click expands it) or "-&gt;|&lt;-" (a click
/// makes it regular again). Drawn once: black with a white outline, the hot spot on the line.
/// </summary>
public static class ColumnCursors {
  private const int Size = 32;

  public static Cursor Expand { get; } = Make(outward: true);
  public static Cursor Shrink { get; } = Make(outward: false);

  private static Cursor Make(bool outward) {
    try {
      var bitmap = new RenderTargetBitmap(new PixelSize(Size, Size));
      using (var dc = bitmap.CreateDrawingContext()) {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open()) {
          // the line
          g.BeginFigure(new Point(15, 6), true); g.LineTo(new Point(17, 6)); g.LineTo(new Point(17, 26)); g.LineTo(new Point(15, 26)); g.EndFigure(true);
          // an arrow on each side: away from the line (expand) or towards it (shrink)
          foreach (var side in new[] { -1, 1 }) {
            double near = 16 + side * 3, far = 16 + side * 14;
            double tip = outward ? far : near, back = outward ? near : far;
            double head = outward ? tip - side * 6 : tip + side * 6;
            g.BeginFigure(new Point(back, 15), true);
            g.LineTo(new Point(head, 15)); g.LineTo(new Point(head, 11)); g.LineTo(new Point(tip, 16));
            g.LineTo(new Point(head, 21)); g.LineTo(new Point(head, 17)); g.LineTo(new Point(back, 17));
            g.EndFigure(true);
          }
        }
        dc.DrawGeometry(Brushes.Black, new Pen(Brushes.White, 1.5), geometry);
      }
      return new Cursor(bitmap, new PixelPoint(16, 16));
    } catch {
      return new Cursor(StandardCursorType.SizeWestEast);  // no custom cursors on this platform
    }
  }
}
