using System.Globalization;
using HeuristicLab.Analysis;
using HeuristicLab.Next.Runtime.Visuals;

namespace HeuristicLab.Next.Runtime.Runs;

/// <summary>
/// Charts of runs, one color per group (HeuristicLab's bubble chart, box plot and chart
/// aggregation views, plus histograms and cumulative distributions): every group is a legend entry
/// that can be hidden.
/// </summary>
public static class RunCharts {
  private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
  /// <summary>X axis choice that numbers the runs (1, 2, ...) in table order.</summary>
  public const string RunIndex = "#run";

  private static string Name(RunTable table, string key) => key == RunIndex ? "Run" : table.Column(key)?.Name ?? key;

  /// <summary>One point per run: x and y are numeric columns (or the run's number for x).</summary>
  public static ChartVisual Scatter(RunTable table, IReadOnlyList<RunGroup> groups, string x, string y) {
    var index = table.Rows.Select((r, i) => (r, i)).ToDictionary(p => p.r, p => (double)(p.i + 1));
    var series = groups.Select((g, i) => new PlotSeries(g.Name, SeriesKind.Points,
      g.Rows.Select(r => (X: x == RunIndex ? index[r] : r.Number(x), Y: r.Number(y)))
        .Where(p => p.X.HasValue && p.Y.HasValue).Select(p => (p.X!.Value, p.Y!.Value)).ToList(), Rgb.Palette(i))).ToList();
    return new ChartVisual($"{Name(table, y)} over {Name(table, x)}", series, Name(table, x), Name(table, y)) {
      Notes = [$"{series.Sum(s => s.Points.Count)} runs in {groups.Count} groups"]
    };
  }

  /// <summary>A group's box: quartiles, median, mean, whiskers (most extreme runs within 1.5 box lengths of the box) and the outliers beyond them.</summary>
  public sealed record BoxStats(string Group, int Count, double Min, double WhiskerLow, double Q1, double Median, double Mean,
                                double Q3, double WhiskerHigh, double Max, IReadOnlyList<double> Outliers);

  /// <summary>The boxes of a box plot, one per group with values (in group order).</summary>
  public static IReadOnlyList<BoxStats> BoxStatistics(IReadOnlyList<RunGroup> groups, string y) =>
    groups.Select(g => (g.Name, Values: g.Rows.Select(r => r.Number(y)).OfType<double>().Where(double.IsFinite).Order().ToArray()))
      .Where(d => d.Values.Length > 0).Select(d => {
        var s = RunStatistics.Summary(d.Name, d.Values);
        double iqr = s.Q3 - s.Q1, lowFence = s.Q1 - 1.5 * iqr, highFence = s.Q3 + 1.5 * iqr;
        return new BoxStats(d.Name, s.Count, s.Min, d.Values.First(v => v >= lowFence), s.Q1, s.Median, s.Mean, s.Q3,
          d.Values.Last(v => v <= highFence), s.Max, d.Values.Where(v => v < lowFence || v > highFence).ToList());
      }).ToList();

  /// <summary>Distribution per group: box from the lower to the upper quartile, median line, mean diamond, whiskers to the most extreme runs within 1.5 box lengths, outliers as points.</summary>
  public static Visual BoxPlot(RunTable table, IReadOnlyList<RunGroup> groups, string y) {
    var shapes = new List<Shape>();
    var boxes = BoxStatistics(groups, y);
    if (boxes.Count == 0) return new TextVisual("Box plot", $"No numeric values of {Name(table, y)}.");
    double low = boxes.Min(b => b.Min), high = boxes.Max(b => b.Max), span = Math.Max(high - low, 1e-12);
    var names = groups.Select(g => g.Name).ToList();
    foreach (var box in boxes) {
      int i = names.IndexOf(box.Group);
      var color = Rgb.Palette(i);
      var name = box.Group;
      double iqr = box.Q3 - box.Q1;
      shapes.Add(new LineShape(new Point2(i, box.WhiskerLow), new Point2(i, box.Q1), color, 1.5) { Group = name });
      shapes.Add(new LineShape(new Point2(i, box.Q3), new Point2(i, box.WhiskerHigh), color, 1.5) { Group = name });
      shapes.Add(new LineShape(new Point2(i - 0.12, box.WhiskerLow), new Point2(i + 0.12, box.WhiskerLow), color, 1.5) { Group = name });
      shapes.Add(new LineShape(new Point2(i - 0.12, box.WhiskerHigh), new Point2(i + 0.12, box.WhiskerHigh), color, 1.5) { Group = name });
      shapes.Add(new RectShape(i - 0.3, box.Q1, 0.6, Math.Max(iqr, span * 1e-4), color.WithAlpha(90), color) { Group = name, StrokeThickness = 1.5 });
      shapes.Add(new LineShape(new Point2(i - 0.3, box.Median), new Point2(i + 0.3, box.Median), color, 2.5) { Group = name });
      shapes.Add(new MarkerShape(new Point2(i, box.Mean), color, 8, MarkerKind.Diamond) { Group = name });
      foreach (var outlier in box.Outliers) shapes.Add(new MarkerShape(new Point2(i, outlier), color, 5) { Group = name });
    }
    return new SceneVisual($"{Name(table, y)} by group", shapes, YUp: true, Uniform: false) {
      Axes = true, XAxisScale = false, YAxisTitle = Name(table, y),
      Legend = names.Select((n, i) => new LegendEntry(n, Rgb.Palette(i))).ToList(),
      // the group names under the axis; half a slot of room left and right, a little above and below
      XCategories = names.Select((n, i) => new AxisCategory(i, n, n)).ToList(),
      XRange = (-0.5, names.Count - 0.5), YRange = (low - span * 0.06, high + span * 0.06),
      // the numbers of each box are in the table below the chart (BoxStatistics)
      Notes = ["Box: quartiles · line: median · diamond: mean · whiskers: most extreme runs within 1.5 box lengths · dots: outliers"]
    };
  }

  /// <summary>Histograms of the groups over shared bins.</summary>
  public static ChartVisual Histogram(RunTable table, IReadOnlyList<RunGroup> groups, string y, int bins = 20) {
    var values = groups.Select(g => g.Rows.Select(r => r.Number(y)).OfType<double>().Where(double.IsFinite).ToList()).ToList();
    var shared = Charts.HistogramBins(values.SelectMany(v => v), bins, exact: false);
    var series = shared is { } b
      ? groups.Select((g, i) => new PlotSeries(g.Name, SeriesKind.Histogram, Charts.Histogram(values[i], b), Rgb.Palette(i), Width: b.Width)).ToList()
      : [];
    return new ChartVisual($"Histogram of {Name(table, y)}", series, Name(table, y), "Runs");
  }

  /// <summary>Empirical cumulative distribution per group: the share of runs with at most a value.</summary>
  public static ChartVisual Cumulative(RunTable table, IReadOnlyList<RunGroup> groups, string y) {
    var series = groups.Select((g, i) => {
      var sorted = g.Rows.Select(r => r.Number(y)).OfType<double>().Where(double.IsFinite).Order().ToList();
      var points = sorted.Select((v, k) => (v, (k + 1.0) / sorted.Count)).ToList();
      if (points.Count > 0) points.Insert(0, (sorted[0], 0.0));
      return new PlotSeries(g.Name, SeriesKind.StepLine, points, Rgb.Palette(i));
    }).ToList();
    return new ChartVisual($"Cumulative distribution of {Name(table, y)}", series, Name(table, y), "Share of runs");
  }

  /// <summary>
  /// A table row (e.g. BestQuality of Qualities) of every run, one color per group (gaps between
  /// runs), optionally only the group mean (HeuristicLab's chart aggregation view).
  /// </summary>
  public static ChartVisual Curves(IReadOnlyList<RunGroup> groups, string tableName, string rowName, bool runs = true, bool mean = true) {
    var series = new List<PlotSeries>();
    string xTitle = "Step (generation / iteration)";
    for (int i = 0; i < groups.Count; i++) {
      var curves = new List<double[]>();
      foreach (var row in groups[i].Rows) {
        if (row.Run?.Results.TryGetValue(tableName, out var item) != true || item is not DataTable dataTable) continue;
        if (!string.IsNullOrEmpty(dataTable.VisualProperties.XAxisTitle)) xTitle = dataTable.VisualProperties.XAxisTitle;
        if (dataTable.Rows.TryGetValue(rowName, out var dataRow)) curves.Add(dataRow.Values.ToArray());
      }
      if (curves.Count == 0) continue;
      if (runs) {
        var points = new List<(double, double)>();
        foreach (var curve in curves) {
          if (points.Count > 0) points.Add((double.NaN, double.NaN));  // gap between runs
          points.AddRange(curve.Select((v, x) => ((double)x, v)));
        }
        series.Add(new PlotSeries(mean ? $"{groups[i].Name} (runs)" : groups[i].Name, SeriesKind.Line, points, Rgb.Palette(i).WithAlpha(mean ? (byte)110 : (byte)255)));
      }
      if (mean) {
        int length = curves.Max(c => c.Length);
        var average = Enumerable.Range(0, length).Select(x => {
          var at = curves.Where(c => x < c.Length && double.IsFinite(c[x])).Select(c => c[x]).ToList();
          return ((double)x, at.Count > 0 ? at.Average() : double.NaN);
        }).ToList();
        series.Add(new PlotSeries($"{groups[i].Name} (mean of {curves.Count})", SeriesKind.Line, average, Rgb.Palette(i)));
      }
    }
    return new ChartVisual($"{rowName} ({tableName})", series, xTitle, rowName);
  }
}
