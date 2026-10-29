using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Next.Runtime.Runs;

namespace HeuristicLab.Studio.ViewModels;

/// <summary>
/// The three shades of a run window's color (palest for the source, strongest for the run - as the table's orange):
/// the run's Source / Origin / Run cells in the Results table wear them while its window is open.
/// </summary>
public sealed record RunPalette(string Name, Color Source, Color Origin, Color Run) {
  public IReadOnlyList<Color> Shades => [Source, Origin, Run];

  /// <summary>Colors other than the logo orange (and the warnings' yellow), taken in this order.</summary>
  public static readonly IReadOnlyList<RunPalette> All = [
    new("cyan", Color.Parse("#C8F2FB"), Color.Parse("#93E3F5"), Color.Parse("#5CD2EE")),
    new("green", Color.Parse("#D4F2C8"), Color.Parse("#A9E496"), Color.Parse("#7CD463")),
    new("purple", Color.Parse("#E6D6F8"), Color.Parse("#CDADF1"), Color.Parse("#B384E9")),
    new("pink", Color.Parse("#F9D3E6"), Color.Parse("#F2A8CD"), Color.Parse("#EB7CB4")),
    new("blue", Color.Parse("#D3E0F8"), Color.Parse("#A9C2F1"), Color.Parse("#7EA3EA")),
    new("teal", Color.Parse("#CDEEE6"), Color.Parse("#9DDDCD"), Color.Parse("#6BCBB3")),
  ];
}

/// <summary>A line of a run window's table: the name on the window's color, the value on grey (both alternating per row).</summary>
public sealed record RunDetailLine(string Name, string Value, IBrush NameBackground, IBrush ValueBackground);

/// <summary>
/// One run of the Results table in a window of its own (right-click "Open run in window"): its columns as a table
/// in the window's color, its results and the selected result's picture.
/// </summary>
public sealed partial class RunWindowViewModel : ViewModelBase {
  public RunWindowViewModel(AnalysisRow row, IReadOnlyList<ColumnChoice> columns, RunPalette palette) {
    Row = row.Row;
    Palette = palette;
    Title = $"Run: {Row.Text("run")} ({Row.Text("source")})";
    Lines = columns.Select((c, i) => new RunDetailLine(c.Display, Row.Text(c.Key),
      TableGridViewModel.Brush(i % 2 == 0 ? palette.Origin : palette.Source, 1),
      TableGridViewModel.Brush(i % 2 == 0 ? TableGridViewModel.Grey : TableGridViewModel.LightGrey, 1))).ToList();
    Results = Row.Run is { } run
      ? run.Results.Select(r => new ResultEntryViewModel(r.Key, r.Value, ItemInspector.Summary(r.Value))).ToList()
      : [];
    SelectedResult = Results.FirstOrDefault(r => HeuristicLab.Next.Runtime.Visuals.Visualizations.For(r.Value, r.Name).Count > 0 && r.Name.StartsWith("Best", StringComparison.Ordinal))
                     ?? Results.FirstOrDefault();
  }

  public RunRow Row { get; }
  public RunPalette Palette { get; }
  public string Title { get; }
  public IReadOnlyList<RunDetailLine> Lines { get; }
  public IReadOnlyList<ResultEntryViewModel> Results { get; }

  [ObservableProperty]
  public partial ResultEntryViewModel? SelectedResult { get; set; }

  [ObservableProperty]
  public partial ResultDetailViewModel? ResultDetail { get; set; }

  partial void OnSelectedResultChanged(ResultEntryViewModel? value) =>
    ResultDetail = value == null ? null : new ResultDetailViewModel(value.Name, value.Value, new EditContext(_ => { }, () => false));

  /// <summary>Asks the window to close (its view closes it; closing it tells the Results tab).</summary>
  public event Action? CloseRequested;

  public void RequestClose() => CloseRequested?.Invoke();

  /// <summary>Asks the window to come to the front (opened again from the table).</summary>
  public event Action? ActivateRequested;

  public void RequestActivate() => ActivateRequested?.Invoke();
}
