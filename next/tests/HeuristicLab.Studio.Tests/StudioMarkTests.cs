using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Next.Runtime.Runs;
using HeuristicLab.Next.Runtime.Visuals;
using HeuristicLab.Optimization;
using HeuristicLab.Studio.Controls;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>
/// "Mark best/worst" in the Results tab: the runs ranked by a number (ascending or descending), values within ± of a
/// place's first value sharing it, the first places marked - gold, silver, bronze, then green over yellow to red: a
/// light layer over their rows (the first three bold), a large dot of their color behind their point (scatter, box plot,
/// cumulative distribution), their curves thick in it, their legend text in it.
/// </summary>
[TestClass]
public class StudioMarkTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  [TestMethod]
  [Timeout(600_000)]
  public void PlacesShareValuesWithinTheTolerance() {
    RunRow Row(string name, double value) => new("s", new Dictionary<string, object> { ["run"] = name, ["result:Q"] = value }, null);
    var rows = new[] { Row("a", 5), Row("b", 1), Row("c", 1), Row("d", 2), Row("e", 2.5), Row("f", 9), Row("g", double.NaN) };
    var exact = MarkedRuns.Rank(rows, new RunMarking("result:Q", false, 6, 0));
    CollectionAssert.AreEqual(new[] { 1, 1, 2, 3, 4, 5 }, new[] { "b", "c", "d", "e", "a", "f" }.Select(n => exact.PlaceOf(rows.Single(r => r.Text("run") == n))!.Value).ToArray());
    Assert.IsNull(exact.PlaceOf(rows[^1]), "no value: not ranked");
    // ± 1: 1, 1, 2 share the first place (within 1 of 1), 2.5 starts the second
    var near = MarkedRuns.Rank(rows, new RunMarking("result:Q", false, 6, 1));
    Assert.AreEqual(3, near.At(1).Count);
    Assert.AreEqual(2, near.PlaceOf(rows[4]));
    // descending: the largest first
    var down = MarkedRuns.Rank(rows, new RunMarking("result:Q", true, 6, 0));
    Assert.AreEqual(1, down.PlaceOf(rows[5]));
    // colors: gold, silver, bronze, then green ... red at the last place
    Assert.AreEqual(RunMarks.Gold, RunMarks.PlaceColor(1, 10));
    Assert.AreEqual(RunMarks.Silver, RunMarks.PlaceColor(2, 10));
    Assert.AreEqual(RunMarks.Bronze, RunMarks.PlaceColor(3, 10));
    Assert.AreEqual(RunMarks.Green, RunMarks.PlaceColor(4, 10));
    Assert.AreEqual(RunMarks.Yellow, RunMarks.PlaceColor(7, 10));
    Assert.AreEqual(RunMarks.Red, RunMarks.PlaceColor(10, 10));
    Assert.AreEqual("1st: b, c (1)", exact.Label(1));
    Assert.AreEqual("12th", RunMarks.Ordinal(12));
    Assert.AreEqual("22nd", RunMarks.Ordinal(22));
  }

  [TestMethod]
  [Timeout(600_000)]
  public async Task MarkedRunsAreColoredInTheTableAndTheCharts() {
    await session.Dispatch(async () => {
      var vm = new MainViewModel(null, null);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1500, Height = 960 };
      window.Show();
      var experiment = ws.NewExperiment();
      var ga = ws.AddAlgorithm(experiment, Catalog.Find(Catalog.Algorithms(), "GeneticAlgorithm")!);
      ws.SetProblem(ga, (IProblem)Setups.Create(new SetupRequest("GeneticAlgorithm") { Problem = "TravelingSalesmanProblem", Instance = "ch130" }).Algorithm.Problem!.Clone());
      ParameterEditor.Apply(ga.Algorithm, ["PopulationSize=10", "MaximumGenerations=5"]);
      await ws.StartAllCommand.ExecuteAsync(null);
      // 12 copies of the run (its own quality, tens of thousands, comes last); BestQuality 1000, 1100, 1100, 1300, ...
      var run = ga.Algorithm.Runs.Single();
      for (int i = 1; i <= 12; i++) {
        var copy = (IRun)run.Clone();
        copy.Name = $"Copy {i}";
        copy.Results["BestQuality"] = new HeuristicLab.Data.DoubleValue(i == 3 ? 1100 : 1000 + 100 * (i - 1));
        ga.Algorithm.Runs.Add(copy);
      }
      vm.SelectedTab = MainViewModel.ResultsTab;
      var analysis = vm.RunAnalysis;
      analysis.RefreshSources();
      analysis.Sources.First().IsChecked = true;
      analysis.SelectedView = RunAnalysisViewModel.TableView;
      Pump();

      Button Shown(Func<Button, bool> which) => window.GetVisualDescendants().OfType<Button>().Single(b => which(b) && b.IsEffectivelyVisible);
      void Click(Button b) {
        var at = b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Pump();
      }

      // the button right after "+ Filter"; pressed, the fields instead of it
      var filter = Shown(b => b.Content as string == "+ Filter");
      var mark = Shown(b => b.Content as string == "Mark best/worst");
      Assert.AreSame(filter.Parent, mark.Parent, "next to + Filter");
      var siblings = ((Panel)filter.Parent!).Children.ToList();
      Assert.AreEqual(siblings.IndexOf(filter) + 1, siblings.IndexOf(mark), "right after it");
      Click(mark);
      Assert.IsTrue(analysis.IsMarking);
      Assert.IsFalse(mark.IsEffectivelyVisible, "the fields instead of the button");
      Assert.AreEqual("result:BestQuality", analysis.MarkColumn?.Key, "the chart's value at first");
      Assert.IsTrue(analysis.NumericColumns.All(c => analysis.Table.Column(c.Key)!.IsNumeric), "only numbers to rank by");
      var top = window.GetVisualDescendants().OfType<NumericUpDown>().First(n => n.IsEffectivelyVisible && n.Minimum == 6);
      Assert.AreEqual(100, top.Maximum);
      analysis.MarkTop = 8;
      window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(AppContext.BaseDirectory, "studio-mark-fields.png"));
      // the cross: nothing marked, the button again
      Click(Shown(b => b.Content as string == "✕" && b.Command == analysis.CancelMarkingCommand));
      Assert.IsFalse(analysis.IsMarking);
      Assert.IsNull(analysis.Marking);
      Assert.IsTrue(mark.IsEffectivelyVisible);
      // the tick: marked
      Click(mark);
      analysis.MarkTop = 8;
      Click(Shown(b => b.Classes.Contains("markapply")));
      Assert.AreEqual(new RunMarking("result:BestQuality", false, 8, 0), analysis.Marking);
      Assert.AreEqual("Smallest 8 of BestQuality (result)", analysis.MarkingText);

      // the table: the rows' layer in their place's color, the first three bold; 1100 twice = one place
      var grid = analysis.Grid!;
      GridRow RowOf(double quality) => grid.Rows.First(r => ((AnalysisRow)r.Item).Row.Number("result:BestQuality") == quality);
      Color Layer(GridRow row) => ((ISolidColorBrush)row.MarkBrush!).Color;
      Color Expect(Rgb c) => Color.FromArgb(RunAnalysisViewModel.MarkAlpha, c.R, c.G, c.B);
      Assert.AreEqual(Expect(RunMarks.Gold), Layer(RowOf(1000)));
      Assert.IsTrue(grid.Rows.Where(r => ((AnalysisRow)r.Item).Row.Number("result:BestQuality") == 1100).All(r => Layer(r) == Expect(RunMarks.Silver)), "equal values share a place");
      Assert.AreEqual(Expect(RunMarks.Bronze), Layer(RowOf(1300)));
      Assert.AreEqual(Expect(RunMarks.Red), Layer(RowOf(1800)), "the 8th place red");
      Assert.IsNull(RowOf(1900).MarkBrush, "beyond the 8th place: not marked");
      Assert.IsTrue(RowOf(1300).IsBold && !RowOf(1400).IsBold, "the first three bold");
      var view = window.GetVisualDescendants().OfType<TableGridView>().First(v => v.IsEffectivelyVisible);
      Assert.IsTrue(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "1000" && t.FontWeight == FontWeight.Bold), "bold text in the table");
      window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(AppContext.BaseDirectory, "studio-mark-table.png"));
      // sorted again, the marks stay
      analysis.SortCommand.Execute("result:BestQuality");
      Assert.AreEqual(Expect(RunMarks.Gold), Layer(analysis.Grid!.Rows[0]));

      // charts: one legend entry per place, its text in its color
      analysis.SelectedView = RunAnalysisViewModel.ChartsView;
      void Shot(string name) {
        Pump();
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(AppContext.BaseDirectory, $"studio-mark-{name}.png"));
      }
      analysis.ChartKind = "Scatter";
      var scatter = (ChartPanel)analysis.Chart.Current!;
      var gold = scatter.Series.Single(s => s.Name.StartsWith("1st:"));
      Assert.AreEqual(Color.FromRgb(RunMarks.Gold.R, RunMarks.Gold.G, RunMarks.Gold.B), gold.LabelColor);
      Assert.IsTrue(gold.Size > 3.5 && gold.Layer < 0, "a large dot behind the run's point");
      Assert.AreEqual(2, scatter.Series.Single(s => s.Name.StartsWith("2nd:")).Points.Count, "both runs of the shared place");
      Assert.AreEqual(8, scatter.Series.Count(s => s.LabelColor != null));
      Assert.IsTrue(analysis.Chart.LegendItems.Single(l => l.Name.StartsWith("1st:")).HasInk);
      Shot("scatter");
      analysis.ChartKind = "Box plot";
      var box = (SceneVisual)analysis.Chart.Current!;
      Assert.AreEqual(RunMarks.Gold, box.Legend.Single(l => l.Label.StartsWith("1st:")).LabelColor);
      Assert.IsTrue(box.Shapes.OfType<MarkerShape>().Any(m => m.Color == RunMarks.Gold && m.Group!.StartsWith("1st:")));
      Shot("boxplot");
      analysis.ChartKind = "Cumulative distribution";
      Assert.IsTrue(((ChartPanel)analysis.Chart.Current!).Series.Any(s => s.Name.StartsWith("1st:") && s.Points.Count == 1));
      Shot("cumulative");
      analysis.ChartKind = "Curves";
      var curves = (ChartPanel)analysis.Chart.Current!;
      var first = curves.Series.Single(s => s.Name.StartsWith("1st:"));
      Assert.IsTrue(first.Size > 2 && first.Layer > curves.Series.Single(s => s.Name.StartsWith("8th:")).Layer, "thick, the first place on top");
      Shot("curves");

      // the marking's ✕: nothing marked
      analysis.RemoveMarkingCommand.Execute(null);
      Assert.IsNull(analysis.Marking);
      Assert.IsTrue(analysis.Grid!.Rows.All(r => r.MarkBrush == null && !r.IsBold));
      Assert.IsFalse(((ChartPanel)analysis.Chart.Current!).Series.Any(s => s.LabelColor != null));
      window.Close();
      return true;
    }, CancellationToken.None);
  }
}
