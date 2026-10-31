using System.Globalization;
using HeuristicLab.Analysis;
using HeuristicLab.Next.Runtime.Visuals;

namespace HeuristicLab.Next.Runtime.Runs;

/// <summary>
/// Charts of runs, one color per group (HeuristicLab's bubble chart, box plot and chart
/// aggregation views, plus histograms and cumulative distributions): every group is a legend entry
/// that can be hidden. Marked runs ("Mark best/worst", <see cref="MarkedRuns"/>) get one legend entry per place, its
/// text in the place's color: a large dot of that color behind their point (scatter, box plot, cumulative
/// distribution) or a thick line of it (curves).
/// </summary>
public static class RunCharts {
  private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
  /// <summary>X axis choice that numbers the runs (1, 2, ...) in table order.</summary>
  public const string RunIndex = "#run";

  private static string Name(RunTable table, string key) => key == RunIndex ? "Run" : table.Column(key)?.Name ?? key;

  /// <summary>One point per run: x and y are numeric columns (or the run's number for x).</summary>
  public static ChartVisual Scatter(RunTable table, IReadOnlyList<RunGroup> groups, string x, string y, MarkedRuns? marks = null) {
    var index = table.Rows.Select((r, i) => (r, i)).ToDictionary(p => p.r, p => (double)(p.i + 1));
    IEnumerable<(double, double)> At(IEnumerable<RunRow> rows) => rows.Select(r => (X: x == RunIndex ? index.GetValueOrDefault(r) : r.Number(x), Y: r.Number(y)))
      .Where(p => p.X.HasValue && p.Y.HasValue).Select(p => (p.X!.Value, p.Y!.Value));
    var series = groups.Select((g, i) => new PlotSeries(g.Name, SeriesKind.Points, At(g.Rows).ToList(), Rgb.Palette(i))).ToList();
    int count = series.Sum(s => s.Points.Count);
    if (marks != null)
      series.AddRange(marks.Taken.Select(place => Halo(marks, place, At(marks.At(place)).ToList())));
    return new ChartVisual($"{Name(table, y)} over {Name(table, x)}", series, Name(table, x), Name(table, y)) {
      Notes = [$"{count} runs in {groups.Count} groups"]
    };
  }

  /// <summary>Large dots of a place's color behind the marked runs' points.</summary>
  private static PlotSeries Halo(MarkedRuns marks, int place, IReadOnlyList<(double, double)> points) =>
    new(marks.Label(place), SeriesKind.Points, points, marks.Color(place), Size: 8, LabelColor: marks.Color(place), Layer: -1);

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
  public static Visual BoxPlot(RunTable table, IReadOnlyList<RunGroup> groups, string y, MarkedRuns? marks = null) {
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
    // marked runs: a large dot of their place's color, their group's color in front, at their value in their group -
    // spread over the box's width by place (1st left), so places with near values do not hide each other
    var legend = names.Select((n, i) => new LegendEntry(n, Rgb.Palette(i))).ToList();
    if (marks != null) {
      foreach (int place in marks.Taken) {
        string label = marks.Label(place);
        foreach (var row in marks.At(place)) {
          int i = groups.ToList().FindIndex(g => g.Rows.Contains(row));
          if (i < 0 || row.Number(y) is not double v || !double.IsFinite(v)) continue;
          double x = i - 0.25 + 0.5 * (place - 1) / Math.Max(1, marks.Marking.Top - 1);
          shapes.Add(new MarkerShape(new Point2(x, v), marks.Color(place), 16) { Group = label });
          shapes.Add(new MarkerShape(new Point2(x, v), Rgb.Palette(i), 6) { Group = label });
        }
        legend.Add(new LegendEntry(label, marks.Color(place), marks.Color(place)));
      }
    }
    return new SceneVisual($"{Name(table, y)} by group", shapes, YUp: true, Uniform: false) {
      Axes = true, XAxisScale = false, YAxisTitle = Name(table, y),
      Legend = legend,
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
  public static ChartVisual Cumulative(RunTable table, IReadOnlyList<RunGroup> groups, string y, MarkedRuns? marks = null) {
    // each run's point on its group's curve: its value and the share of the group's runs up to it
    var at = new Dictionary<RunRow, (double, double)>();
    var series = groups.Select((g, i) => {
      var sorted = g.Rows.Select(r => (Row: r, Value: r.Number(y))).Where(p => p.Value is double v && double.IsFinite(v))
        .Select(p => (p.Row, Value: p.Value!.Value)).OrderBy(p => p.Value).ToList();
      var points = sorted.Select((p, k) => (p.Value, (k + 1.0) / sorted.Count)).ToList();
      // equal values: the curve's top of that step
      for (int k = 0; k < sorted.Count; k++) at[sorted[k].Row] = points[sorted.FindLastIndex(p => p.Value == sorted[k].Value)];
      if (points.Count > 0) points.Insert(0, (sorted[0].Value, 0.0));
      return new PlotSeries(g.Name, SeriesKind.StepLine, points, Rgb.Palette(i));
    }).ToList();
    if (marks != null)
      series.AddRange(marks.Taken.Select(place => Halo(marks, place, marks.At(place).Where(at.ContainsKey).Select(r => at[r]).ToList())));
    return new ChartVisual($"Cumulative distribution of {Name(table, y)}", series, Name(table, y), "Share of runs");
  }

  /// <summary>
  /// A table row (e.g. BestQuality of Qualities) of every run, one color per group (gaps between
  /// runs), optionally only the group mean (HeuristicLab's chart aggregation view).
  /// </summary>
  public static ChartVisual Curves(IReadOnlyList<RunGroup> groups, string tableName, string rowName, bool runs = true, bool mean = true, MarkedRuns? marks = null) {
    var series = new List<PlotSeries>();
    string xTitle = "Step (generation / iteration)";
    double[]? CurveOf(RunRow row) {
      if (row.Run?.Results.TryGetValue(tableName, out var item) != true || item is not DataTable dataTable) return null;
      if (!string.IsNullOrEmpty(dataTable.VisualProperties.XAxisTitle)) xTitle = dataTable.VisualProperties.XAxisTitle;
      return dataTable.Rows.TryGetValue(rowName, out var dataRow) ? dataRow.Values.ToArray() : null;
    }
    static List<(double, double)> Joined(IEnumerable<double[]> curves) {
      var points = new List<(double, double)>();
      foreach (var curve in curves) {
        if (points.Count > 0) points.Add((double.NaN, double.NaN));  // gap between runs
        points.AddRange(curve.Select((v, x) => ((double)x, v)));
      }
      return points;
    }
    for (int i = 0; i < groups.Count; i++) {
      var curves = groups[i].Rows.Select(CurveOf).OfType<double[]>().ToList();
      if (curves.Count == 0) continue;
      if (runs) {
        var points = Joined(curves);
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
    // marked runs: their curves thick in their place's color, on top of the others (the first place on top of all)
    if (marks != null)
      foreach (int place in marks.Taken) {
        var points = Joined(marks.At(place).Select(CurveOf).OfType<double[]>());
        if (points.Count > 0)
          series.Add(new PlotSeries(marks.Label(place), SeriesKind.Line, points, marks.Color(place), Size: 4, LabelColor: marks.Color(place),
            Layer: 1 + marks.Marking.Top - place));
      }
    return new ChartVisual($"{rowName} ({tableName})", series, xTitle, rowName);
  }
}
