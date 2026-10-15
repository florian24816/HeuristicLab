using Avalonia.Headless;
using Avalonia.VisualTree;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Next.Runtime.Visuals;
using HeuristicLab.Optimization;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>The Results tab: runs of the workspace's experiments as a table, grouped, charted and tested.</summary>
[TestClass]
public class StudioRunAnalysisTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static IProblem Ch130() =>
    (IProblem)Setups.Create(new SetupRequest("GeneticAlgorithm") { Problem = "TravelingSalesmanProblem", Instance = "ch130" }).Algorithm.Problem!.Clone();

  private static void AssertReadable(TableGridViewModel grid) {
    var plain = Enumerable.Range(0, grid.Columns.Count).Where(i => grid.Columns[i].Fixed == null).Take(2).ToList();
    Assert.AreEqual(2, plain.Count);
    var first = grid.Rows[0].Cells;
    Assert.AreSame(TableGridViewModel.Brush(TableGridViewModel.Grey, 1), first[plain[0]].Background);
    Assert.AreSame(TableGridViewModel.Brush(TableGridViewModel.LightGrey, 1), first[plain[1]].Background);
    Assert.AreSame(TableGridViewModel.Brush(TableGridViewModel.Grey, TableGridViewModel.WeakRow), grid.Rows[1].Cells[plain[0]].Background);
    Assert.AreSame(first[plain[0]].Background, grid.Rows[2].Cells[plain[0]].Background);
    // the header one shade darker than the rows
    Assert.AreSame(TableGridViewModel.Brush(TableGridViewModel.Darker(TableGridViewModel.Grey), 1), grid.Header[plain[0]].Background);
  }

  /// <summary>Scrolls the shown table sideways: its fixed cells move along, so they stay at the left edge.</summary>
  private static void AssertFixedColumnsStay(MainWindow window, string shot) {
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    var view = window.GetVisualDescendants().OfType<TableGridView>().First(v => v.IsEffectivelyVisible);
    var scroll = view.GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>().First();
    Assert.IsTrue(scroll.Extent.Width > scroll.Viewport.Width, "the table is wider than its space");
    scroll.Offset = new Avalonia.Vector(250, 0);
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    var fixedCells = view.GetVisualDescendants().OfType<HeuristicLab.Studio.Controls.Sticky>().Where(c => c.StickX).ToList();
    Assert.IsTrue(fixedCells.Count > 0);
    foreach (var cell in fixedCells)
      Assert.AreEqual(scroll.Offset.X, ((Avalonia.Media.TranslateTransform)cell.RenderTransform!).X, 0.01);
    window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, shot));
    scroll.Offset = default;
  }

  /// <summary>A time-limit run takes its algorithm's runs over: they still belong to the algorithm's box.</summary>
  [TestMethod]
  [Timeout(300_000)]
  public async Task TimeLimitRunsBelongToTheirAlgorithmsBox() {
    await session.Dispatch(async () => {
      var vm = new MainViewModel(null, null);
      var ws = vm.Workspace;
      var experiment = ws.NewExperiment();
      var limit = ws.AddContainer(experiment, ExperimentTree.ContainerTypes().Single(e => e.Type == typeof(TimeLimitRun)));
      limit.TimeLimitText = "00:00:02";
      var ts = ws.AddAlgorithm(limit, Catalog.Find(Catalog.Algorithms(), "TabuSearch")!);
      ws.SetProblem(ts, Ch130());
      ParameterEditor.Apply(ts.Algorithm, ["MaximumIterations=5"]);
      var ls = ws.AddAlgorithm(experiment, Catalog.Find(Catalog.Algorithms(), "LocalSearch")!);
      ws.SetProblem(ls, Ch130());
      ParameterEditor.Apply(ls.Algorithm, ["MaximumIterations=5"]);
      await ws.StartAllCommand.ExecuteAsync(null);

      var analysis = vm.RunAnalysis;
      analysis.RefreshSources();
      var root = analysis.Sources.First();
      root.IsChecked = true;
      Assert.AreEqual(2, analysis.Table.Rows.Count, analysis.Status);
      var limitBox = root.Children.Single(c => c.Kind == "Time-limit run");
      limitBox.Children.Single().IsChecked = false;
      Assert.AreEqual(1, analysis.Table.Rows.Count, "unticking the algorithm in the time-limit run leaves its run out");
      Assert.AreEqual(false, limitBox.IsChecked);
      Assert.IsNull(root.IsChecked);
      return true;
    }, CancellationToken.None);
  }

  /// <summary>
  /// Export to Python: &lt;experiment&gt;_structure.py (blocks as attributes, typed values), _data.json (the
  /// values) and _analysis.py (examples); loaded with Python itself (if installed). A second export keeps
  /// the analysis, which is the user's.
  /// </summary>
  private static void AssertPythonExport(RunAnalysisViewModel analysis, string experimentName) {
    var folder = Path.Combine(AppContext.BaseDirectory, "python-export");
    if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    Directory.CreateDirectory(folder);
    var module = HeuristicLab.Next.Runtime.Runs.PythonExport.ModuleName(experimentName);
    var written = analysis.ExportPython(Path.Combine(folder, module + "_analysis.py"));
    CollectionAssert.AreEqual(new[] { module + "_structure.py", module + "_data.json", module + "_analysis.py" },
      written.Select(Path.GetFileName).ToList(), "named after the experiment, importable");
    var structure = File.ReadAllText(written[0]);
    StringAssert.Contains(structure, "self.batch_run_1 = self._add(");
    StringAssert.Contains(structure, "self.batch_run_2 = self._add(");
    StringAssert.Contains(structure, "population_size: Optional[int] = None", "whole numbers are ints");
    Assert.IsFalse(structure.Contains("6110"), "no values in the structure");
    var script = File.ReadAllText(written[2]);
    StringAssert.Contains(script, $"from {module}_structure import");
    StringAssert.Contains(script, "sns.boxplot(data, x=\"group\", y=\"best_quality\"");
    StringAssert.Contains(script, "compare_curves(\"qualities\", \"best_quality\", *groups)");
    StringAssert.Contains(script, "run.params.algorithm_name", "a parameter every algorithm has");

    // exported again: structure and data anew, the analysis (perhaps changed by the user) kept
    File.AppendAllText(written[2], "# mine\n");
    var again = analysis.ExportPython(Path.Combine(folder, module + "_structure.py"));
    Assert.AreEqual(2, again.Count);
    StringAssert.EndsWith(File.ReadAllText(written[2]), "# mine\n");

    var check = $$"""
      import sys
      sys.path.insert(0, {{PythonString(folder)}})
      from {{module}}_structure import exp, Run, Block, PARAMETERS, RESULTS
      assert exp.name == {{PythonString(experimentName)}}, exp.name
      assert len(exp.runs) == 11, len(exp.runs)
      assert [b.kind for b in exp.blocks] == ["Batch run", "Batch run", "Algorithm"], [b.kind for b in exp.blocks]
      ga20 = exp.batch_run_1.genetic_algorithm_ga
      assert len(ga20.runs) == 5 and len(exp.batch_run_1.runs) == 5 and not exp.batch_run_1.made_here
      assert [b.name for b in exp.walk() if b.made_here] == ["Genetic Algorithm (GA)", "Genetic Algorithm (GA)", "Tabu Search (TS)"]
      assert ga20.label == "Batch run 1 / Genetic Algorithm (GA)", ga20.label
      assert all(r.params.population_size == 20 for r in ga20.runs), ga20.values("population_size")
      assert all(r.params["PopulationSize"] == 60 for r in exp.batch_run_2.runs)
      assert all(isinstance(r.params.population_size, int) for r in ga20.runs)
      assert all(isinstance(r.params.mutation_probability, float) for r in ga20.runs)
      assert ga20.values("BestQuality") == [r.results.best_quality for r in ga20.runs]
      curves = ga20.curves("qualities", "best_quality")
      assert len(curves) == 5 and all(len(c) == 26 and all(isinstance(v, float) for v in c) for c in curves), [len(c) for c in curves]
      assert ga20.runs[0].path == {{PythonString(experimentName)}} + " / Batch run 1 / Genetic Algorithm (GA)", ga20.runs[0].path
      assert "population_size" in PARAMETERS and "best_quality" in RESULTS
      print("ok")
      """;
    if (RunPython(check) is { } output) StringAssert.StartsWith(output, "ok", output);
  }

  private static string PythonString(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

  /// <summary>Runs a script with python3 (null if Python is not installed: the check is skipped).</summary>
  private static string? RunPython(string script) {
    var start = new System.Diagnostics.ProcessStartInfo("python3") { ArgumentList = { "-I", "-c", script },
      RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    try {
      using var process = System.Diagnostics.Process.Start(start)!;
      var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
      process.WaitForExit();
      return output;
    } catch (System.ComponentModel.Win32Exception) {
      return null;
    }
  }

  [TestMethod]
  [Timeout(300_000)]
  public async Task CompareBatchRunsOfAnExperiment() {
    await session.Dispatch(async () => {
      var vm = new MainViewModel(null, null);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1500, Height = 960 };
      window.Show();
      void Shot(string name) {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, $"studio-results-{name}.png"));
      }

      // an experiment: a GA with population 20 and one with 60, five runs each, and one tabu search run
      var experiment = ws.NewExperiment();
      var batchRun = ExperimentTree.ContainerTypes().Single(e => e.Type == typeof(BatchRun));
      foreach (var size in new[] { 20, 60 }) {
        var batch = ws.AddContainer(experiment, batchRun);
        batch.RepetitionsText = "5";
        var ga = ws.AddAlgorithm(batch, Catalog.Find(Catalog.Algorithms(), "GeneticAlgorithm")!);
        ws.SetProblem(ga, Ch130());
        ParameterEditor.Apply(ga.Algorithm, [$"PopulationSize={size}", "MaximumGenerations=25"]);
      }
      var ts = ws.AddAlgorithm(experiment, Catalog.Find(Catalog.Algorithms(), "TabuSearch")!);
      ws.SetProblem(ts, Ch130());
      ParameterEditor.Apply(ts.Algorithm, ["MaximumIterations=10"]);
      await ws.StartAllCommand.ExecuteAsync(null);

      // the experiment's runs table right below its "Runs" heading
      ws.Selected = experiment;
      Avalonia.Threading.Dispatcher.UIThread.RunJobs();
      var heading = window.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().First(t => t.Text == "Runs" && t.IsEffectivelyVisible && t.FontWeight == Avalonia.Media.FontWeight.SemiBold);
      var experimentRuns = window.GetVisualDescendants().OfType<TableGridView>().First(v => v.IsEffectivelyVisible);
      double below = Avalonia.VisualExtensions.TranslatePoint(experimentRuns, default, window)!.Value.Y
                     - Avalonia.VisualExtensions.TranslatePoint(heading, new Avalonia.Point(0, heading.Bounds.Height), window)!.Value.Y;
      Assert.IsTrue(below is >= 0 and < 30, $"the table starts {below:0} px below its heading");
      Shot("experiment-runs");

      vm.SelectedTab = MainViewModel.ResultsTab;
      var analysis = vm.RunAnalysis;
      var root = analysis.Sources.First();
      Assert.AreEqual(3, root.Children.Count, "two batch runs and the tabu search");
      root.IsChecked = true;
      Assert.AreEqual(11, analysis.Table.Rows.Count, analysis.Status);
      Assert.IsTrue(root.Subtree().All(s => s.IsChecked == true), "ticking the experiment ticks everything below it");

      // unticking a part leaves its runs out; the boxes above are partly ticked
      var gaBatch = root.Children[0];
      gaBatch.Children[0].IsChecked = false;
      Assert.AreEqual(6, analysis.Table.Rows.Count, "the GA's runs are left out, also from its batch run and the experiment");
      Assert.IsNull(root.IsChecked);
      Assert.AreEqual(false, gaBatch.IsChecked, "nothing left under the batch run");
      Avalonia.Threading.Dispatcher.UIThread.RunJobs();
      var rootBox = window.GetVisualDescendants().OfType<Avalonia.Controls.CheckBox>().First(c => c.DataContext == root);
      Assert.IsNull(rootBox.IsChecked, "the experiment's box shows it is partly ticked");
      Assert.IsTrue(analysis.Table.Rows.All(r => r.Text("source") != root.Name), "runs are listed under the fully ticked boxes");
      rootBox.IsChecked = true;  // a click on a partly ticked box ticks everything
      Assert.AreEqual(true, root.IsChecked);
      Assert.AreEqual(11, analysis.Table.Rows.Count);
      Assert.IsTrue(analysis.Table.Rows.All(r => r.Text("source") == root.Name));

      AssertPythonExport(analysis, experiment.Name);

      // group by where the runs were made: one group per batch run and the tabu search
      analysis.GroupToAdd = analysis.AllColumns.Single(c => c.Key == "origin");
      Assert.AreEqual(3, analysis.Groups.Count);
      Assert.IsTrue(analysis.TableColumns.Any(c => c.Key == "param:PopulationSize"), "a parameter that differs between runs is shown");
      analysis.SortCommand.Execute("result:BestQuality");
      var best = analysis.Rows.Select(r => r.Row.Number("result:BestQuality")!.Value).ToList();
      CollectionAssert.AreEqual(best.Order().ToList(), best, "sorted by quality");
      Shot("table");

      // readable table: source, origin and run in three oranges stay in view; columns grey / light grey, rows alternate
      var grid = analysis.Grid!;
      CollectionAssert.AreEqual(new[] { "source", "origin", "run" }, grid.Columns.Where(c => c.Fixed != null).Select(c => c.Key).ToList());
      CollectionAssert.AreEqual(new[] { TableGridViewModel.SourceShade, TableGridViewModel.OriginShade, TableGridViewModel.RunShade },
        grid.Columns.Take(3).Select(c => c.Fixed!.Value).ToList());
      AssertReadable(grid);
      AssertFixedColumnsStay(window, "studio-results-table-scrolled.png");

      // Runs tab: a batch run's runs, the Run column in the Results tab's run orange, staying in view
      vm.SelectedTab = MainViewModel.RunsTab;
      vm.Runs.SelectedSource = vm.Runs.Sources.First().Children.First(c => c.Kind == "Batch run");
      var runs = vm.Runs.Runs!.Grid!;
      Assert.AreEqual(5, runs.Rows.Count);
      Assert.AreEqual(TableGridViewModel.RunShade, runs.Columns.Single(c => c.Fixed != null).Fixed);
      Assert.AreEqual("Run", runs.Columns[0].Header);
      AssertReadable(runs);
      runs.Select(runs.Rows[2]);
      Assert.AreSame(runs.Rows[2].Item, vm.Runs.Runs.Selected, "a click on a row selects the run");
      Assert.IsTrue(runs.Rows[2].IsSelected && !runs.Rows[0].IsSelected);
      AssertFixedColumnsStay(window, "studio-runs-table-scrolled.png");
      vm.SelectedTab = MainViewModel.ResultsTab;

      // box plot of the best quality per group
      analysis.SelectedView = RunAnalysisViewModel.ChartsView;
      Assert.AreEqual("Box plot", analysis.ChartKind);
      Assert.AreEqual("result:BestQuality", analysis.ChartValue!.Key);
      var box = (SceneVisual)analysis.Chart.Current!;
      Assert.AreEqual(3, box.Legend.Count);
      // the box plot's numbers in the program's table design
      var boxes = analysis.BoxTable!;
      Assert.AreEqual(3, boxes.Rows.Count);
      CollectionAssert.AreEqual(new[] { "group", "n", "min", "whiskerLow", "q1", "median", "mean", "q3", "whiskerHigh", "max", "outliers" },
        boxes.Columns.Select(c => c.Key).ToList());
      Assert.AreEqual("5", boxes.Rows[0].Cells[1].Text);
      AssertReadable(boxes);
      Shot("boxplot");

      // fullscreen: the chart fills the screen, legend and table float in panels that move and resize
      var view = window.GetVisualDescendants().OfType<RunAnalysisView>().Single();
      var full = view.OpenFullscreen()!;
      Avalonia.Threading.Dispatcher.UIThread.RunJobs();
      Assert.IsFalse(analysis.Chart.ShowLegend, "the legend is in its panel, not above the chart");
      Assert.IsTrue(full.TablePanel.IsVisible, "a box plot's table");
      Assert.AreEqual(3, analysis.Chart.LegendItems.Count);
      var entry = full.LegendPanel.GetVisualDescendants().OfType<Avalonia.Controls.Border>().First(b => b.Classes.Contains("entry"));
      var at = Avalonia.VisualExtensions.TranslatePoint(entry, new Avalonia.Point(entry.Bounds.Width / 2, entry.Bounds.Height / 2), full)!.Value;
      full.MouseDown(at, Avalonia.Input.MouseButton.Left);
      full.MouseUp(at, Avalonia.Input.MouseButton.Left);
      Avalonia.Threading.Dispatcher.UIThread.RunJobs();
      Assert.IsTrue(analysis.Chart.Hidden.Contains(analysis.Chart.LegendItems[0].Name) && analysis.Chart.LegendItems[0].IsHidden,
        "a click on a legend entry hides that group");
      // the table folds to its title bar and opens again at its size
      full.TablePanel.ToggleMinimized();
      Avalonia.Threading.Dispatcher.UIThread.RunJobs();
      Assert.IsTrue(full.TablePanel.IsMinimized);
      Assert.IsFalse(full.TablePanel.GetVisualDescendants().OfType<TableGridView>().Any(v => v.IsEffectivelyVisible), "minimized: only the title bar");
      full.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-results-fullscreen-minimized.png"));
      full.TablePanel.ToggleMinimized();
      Assert.AreEqual(220, full.TablePanel.Height);
      full.TablePanel.MoveTo(40, 300);
      full.TablePanel.ResizeTo(700, 180);
      Assert.AreEqual(700, full.TablePanel.Width);
      Avalonia.Threading.Dispatcher.UIThread.RunJobs();
      full.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-results-fullscreen.png"));
      analysis.Chart.Toggle(analysis.Chart.LegendItems[0].Name);
      full.Close();
      Avalonia.Threading.Dispatcher.UIThread.RunJobs();
      Assert.IsTrue(analysis.Chart.ShowLegend, "back above the chart");
      Assert.IsNull(view.Fullscreen);
      analysis.ChartKind = "Curves";
      Assert.AreEqual("Qualities", analysis.CurveTable);
      Assert.AreEqual("BestQuality", analysis.CurveRow);
      Assert.AreEqual(6, ((ChartPanel)analysis.Chart.Current!).Series.Count, "runs and mean of three groups");
      Shot("curves");

      // only the two GAs, grouped by population size: statistics and tests
      analysis.AddFilterCommand.Execute(null);
      var filter = analysis.Filters.Single();
      filter.Column = analysis.AllColumns.Single(c => c.Key == "param:Algorithm Type");
      filter.Op = "contains";
      filter.Value = "Genetic";
      Assert.AreEqual(10, analysis.Table.Rows.Count);
      analysis.RemoveGroupCommand.Execute(analysis.GroupBy.Single());
      analysis.GroupToAdd = analysis.AllColumns.Single(c => c.Key == "param:PopulationSize");
      CollectionAssert.AreEqual(new[] { "PopulationSize 20", "PopulationSize 60" }, analysis.Groups.Select(g => g.Name).ToList());
      analysis.SelectedView = RunAnalysisViewModel.StatisticsView;
      Assert.AreEqual(2, analysis.Summaries.Count);
      Assert.IsTrue(analysis.Summaries.All(s => s.Count == "5"));
      Assert.AreEqual(1, analysis.Pairs.Count);
      Assert.AreEqual(2, analysis.SummaryGrid!.Rows.Count);
      Assert.AreEqual(1, analysis.PairsGrid!.Rows.Count);
      Assert.AreEqual(TableGridViewModel.RunShade, analysis.SummaryGrid.Columns[0].Fixed);
      StringAssert.StartsWith(analysis.AllGroupsTest, "Kruskal-Wallis over all 2 groups");
      Shot("statistics");

      // one run: its parameters and results, with the problem's picture of its best solution
      analysis.SelectedView = RunAnalysisViewModel.RunView;
      analysis.SelectedRow = analysis.Rows[0];
      Assert.AreEqual("Best TSP Solution", analysis.SelectedRunResult!.Name);
      Assert.IsTrue(analysis.RunResultDetail!.HasVisualization);
      Shot("run");

      var csv = Path.Combine(Path.GetTempPath(), $"runs-{Guid.NewGuid():N}.csv");
      analysis.Export(csv);
      var lines = File.ReadAllLines(csv);
      Assert.AreEqual(11, lines.Length, "header and ten runs");
      StringAssert.Contains(lines[0], "BestQuality (result)");
      File.Delete(csv);
      window.Close();
      return 0;
    }, CancellationToken.None);
  }
}
