using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>
/// Many runs: tables show 50 rows and a button as wide as the table shows all (drawn only where in view); the
/// Results tab reads a large set of runs in the background behind a bright layer with an orange progress bar.
/// </summary>
[TestClass]
public class StudioLargeTableTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  [TestMethod]
  [Timeout(600_000)]
  public async Task ManyRunsShowFiftyRowsAndBuildInTheBackground() {
    await session.Dispatch(async () => {
      var vm = new MainViewModel(null, null);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1500, Height = 960 };
      window.Show();
      var experiment = ws.NewExperiment();
      var ga = ws.AddAlgorithm(experiment, Catalog.Find(Catalog.Algorithms(), "GeneticAlgorithm")!);
      ws.SetProblem(ga, (IProblem)Setups.Create(new SetupRequest("GeneticAlgorithm") { Problem = "TravelingSalesmanProblem", Instance = "ch130" }).Algorithm.Problem!.Clone());
      ParameterEditor.Apply(ga.Algorithm, ["PopulationSize=20", "MaximumGenerations=5"]);
      await ws.StartAllCommand.ExecuteAsync(null);
      // 600 runs: copies of the one made
      var run = ga.Algorithm.Runs.Single();
      for (int i = 1; i < 600; i++) {
        var copy = (IRun)run.Clone();
        copy.Name = $"Run {i + 1}";
        ga.Algorithm.Runs.Add(copy);
      }

      // the Results tab: the runs are read in the background, the views covered with the progress bar
      vm.SelectedTab = MainViewModel.ResultsTab;
      var analysis = vm.RunAnalysis;
      analysis.RefreshSources();
      analysis.Sources.First().IsChecked = true;
      Assert.IsTrue(analysis.IsBusy, "600 runs not read yet: built in the background");
      Pump();
      var overlay = window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("busy"));
      Assert.IsTrue(overlay.IsEffectivelyVisible);
      Assert.IsTrue(((ISolidColorBrush)overlay.Background!).Color.A < 255, "a bright layer, the views still seen through it");
      var bar = overlay.GetVisualDescendants().OfType<ProgressBar>().Single();
      Assert.AreEqual(Color.Parse("#F7931D"), ((ISolidColorBrush)bar.Foreground!).Color);
      StringAssert.Contains(analysis.ProgressText, "of 600");
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-large-busy.png"));
      while (!analysis.Settled.IsCompleted) { Pump(); await Task.Delay(20); }
      Pump();
      Assert.IsFalse(analysis.IsBusy);
      Assert.IsFalse(overlay.IsEffectivelyVisible);
      Assert.AreEqual(600, analysis.Table.Rows.Count);

      // the table: 50 rows, then a button as wide as the table
      analysis.SelectedView = RunAnalysisViewModel.TableView;
      Pump();
      var grid = analysis.Grid!;
      Assert.AreEqual(600, grid.Rows.Count);
      Assert.AreEqual(TableGridViewModel.Limit, grid.ShownRows.Count);
      var view = window.GetVisualDescendants().OfType<TableGridView>().First(v => v.IsEffectivelyVisible);
      var expand = view.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("expand"));
      Assert.IsTrue(expand.IsEffectivelyVisible);
      StringAssert.Contains(expand.Content as string, "600");
      var header = view.GetVisualDescendants().OfType<ItemsControl>().First(i => i.Classes.Contains("cells"));
      Assert.AreEqual(header.Bounds.Width, expand.Bounds.Width, 1, "as wide as the table");
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-large-50.png"));
      expand.Command!.Execute(null);
      Pump();
      Assert.AreEqual(600, grid.ShownRows.Count);
      int drawn = view.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("row"));
      Assert.IsTrue(drawn < 100, $"only the rows in view are drawn ({drawn})");
      StringAssert.Contains(expand.Content as string, "first 50");
      expand.Command!.Execute(null);
      Pump();
      Assert.AreEqual(TableGridViewModel.Limit, grid.ShownRows.Count);

      // a filter on the runs read already: at once, no waiting
      analysis.AddFilterCommand.Execute(null);
      var filter = analysis.Filters.Single();
      filter.Column = analysis.AllColumns.Single(c => c.Key == "run");
      filter.Op = "contains";
      filter.Value = "Run 5";
      Assert.IsFalse(analysis.IsBusy, "the runs are cached: filtering does not read them again");
      Assert.AreEqual(111, analysis.Table.Rows.Count, "Run 5, Run 50..59, Run 500..599");
      window.Close();
      return true;
    }, CancellationToken.None);
  }
}
