using System.Globalization;
using HeuristicLab.Next.Runtime.Visuals;

namespace HeuristicLab.Next.Runtime.Runs;

/// <summary>
/// "Mark best/worst": the runs ranked by a numeric column, ascending (smallest first) or descending; values within
/// <see cref="Tolerance"/> of a place's first value share that place (0: only equal values); the first <see cref="Top"/>
/// places are marked.
/// </summary>
public sealed record RunMarking(string Key, bool Descending, int Top, double Tolerance) {
  public const int MinTop = 6, MaxTop = 100;
}

/// <summary>The marked runs and their places (1 = first).</summary>
public sealed class MarkedRuns {
  private readonly Dictionary<RunRow, int> places;

  private MarkedRuns(RunMarking marking, Dictionary<RunRow, int> places) {
    Marking = marking;
    this.places = places;
  }

  public RunMarking Marking { get; }
  public IReadOnlyDictionary<RunRow, int> Places => places;

  /// <summary>The places that are taken, in order (fewer than Top when there are fewer distinct values).</summary>
  public IReadOnlyList<int> Taken => places.Values.Distinct().Order().ToList();

  public int? PlaceOf(RunRow row) => places.TryGetValue(row, out int place) ? place : null;

  public IReadOnlyList<RunRow> At(int place) => places.Where(p => p.Value == place).Select(p => p.Key).ToList();

  public Rgb Color(int place) => RunMarks.PlaceColor(place, Marking.Top);

  /// <summary>A place in a legend: "1st: Run 5 (6110)", several runs sharing it named together.</summary>
  public string Label(int place) {
    var rows = At(place);
    var names = rows.Take(3).Select(r => r.Text("run") is { Length: > 0 } name ? name : "run").ToList();
    if (rows.Count > 3) names.Add($"+{rows.Count - 3}");
    var values = rows.Select(r => r.Number(Marking.Key)).OfType<double>().ToList();
    string value = values.Count == 0 ? "" : values.Min() == values.Max() ? G(values[0]) : $"{G(values.Min())} – {G(values.Max())}";
    return $"{RunMarks.Ordinal(place)}: {string.Join(", ", names)} ({value})";
  }

  private static string G(double v) => v.ToString("G6", CultureInfo.InvariantCulture);

  /// <summary>Ranks the rows (those with a finite value of the column) and keeps the first Top places.</summary>
  public static MarkedRuns Rank(IEnumerable<RunRow> rows, RunMarking marking) {
    var values = rows.Select(r => (Row: r, Value: r.Number(marking.Key))).Where(p => p.Value is double v && double.IsFinite(v))
      .Select(p => (p.Row, Value: p.Value!.Value));
    var sorted = marking.Descending ? values.OrderByDescending(p => p.Value) : values.OrderBy(p => p.Value);
    var places = new Dictionary<RunRow, int>();
    int place = 0;
    double first = 0;
    foreach (var (row, value) in sorted) {
      // a new place when the value is further from the place's first (best) value than the tolerance
      if (place == 0 || Math.Abs(value - first) > marking.Tolerance) {
        if (++place > marking.Top) break;
        first = value;
      }
      places[row] = place;
    }
    return new MarkedRuns(marking, places);
  }
}

public static class RunMarks {
  public static readonly Rgb Gold = new(0xD4, 0xAF, 0x37), Silver = new(0xA8, 0xA9, 0xAD), Bronze = new(0xCD, 0x7F, 0x32);
  public static readonly Rgb Green = new(0x2E, 0xA0, 0x43), Yellow = new(0xF2, 0xC9, 0x1B), Red = new(0xD6, 0x2D, 0x2D);

  /// <summary>Gold, silver, bronze; from the 4th place on a gradient from green over yellow to red at the last place.</summary>
  public static Rgb PlaceColor(int place, int top) {
    if (place <= 1) return Gold;
    if (place == 2) return Silver;
    if (place == 3) return Bronze;
    double t = top <= 4 ? 0 : Math.Clamp((place - 4.0) / (top - 4.0), 0, 1);
    return t <= 0.5 ? Mix(Green, Yellow, t * 2) : Mix(Yellow, Red, (t - 0.5) * 2);
  }

  private static Rgb Mix(Rgb a, Rgb b, double t) {
    byte M(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
    return new Rgb(M(a.R, b.R), M(a.G, b.G), M(a.B, b.B));
  }

  public static string Ordinal(int n) => n.ToString(CultureInfo.InvariantCulture) + ((n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch {
    1 => "st", 2 => "nd", 3 => "rd", _ => "th"
  });
}
