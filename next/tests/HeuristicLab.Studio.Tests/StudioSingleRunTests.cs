using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>
/// Start on one algorithm makes single runs: kept apart from the experiment's runs, listed under "Single runs"
/// in the Runs and Results tabs (ticked only when wanted), saved next to the experiment's file.
/// </summary>
[TestClass]
public class StudioSingleRunTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  [TestMethod]
  [Timeout(600_000)]
  public async Task SingleRunsAreKeptApartFromTheExperiment() {
    var folder = Path.Combine(Path.GetTempPath(), $"studio-single-{Guid.NewGuid():N}");
    Directory.CreateDirectory(folder);
    try {
      await session.Dispatch(async () => {
        var vm = new MainViewModel(null, null);
        var ws = vm.Workspace;
        var window = new MainWindow { DataContext = vm, Width = 1500, Height = 960 };
        window.Show();
        var experiment = ws.NewExperiment();
        var batch = ws.AddContainer(experiment, ExperimentTree.ContainerTypes().Single(e => e.Type == typeof(BatchRun)));
        batch.RepetitionsText = "2";
        var ga = ws.AddAlgorithm(batch, Catalog.Find(Catalog.Algorithms(), "GeneticAlgorithm")!);
        ws.SetProblem(ga, (IProblem)Setups.Create(new SetupRequest("GeneticAlgorithm") { Problem = "TravelingSalesmanProblem", Instance = "ch130" }).Algorithm.Problem!.Clone());
        ParameterEditor.Apply(ga.Algorithm, ["PopulationSize=20", "MaximumGenerations=5"]);
        await ws.StartAllCommand.ExecuteAsync(null);
        Assert.AreEqual(2, experiment.Experiment.Runs.Count, "the experiment's own runs");

        // Start on the algorithm: a single run, not one of the batch run's or the experiment's
        ws.Selected = ga;
        var detail = (AlgorithmDetailViewModel)ws.Detail!;
        detail.StartCommand.Execute(null);
        await detail.WaitAsync();
        Pump();
        Assert.AreEqual(2, experiment.Experiment.Runs.Count, "the experiment is as it was made");
        Assert.AreEqual(2, ((BatchRun)batch.Optimizer).Runs.Count);
        Assert.AreEqual(1, experiment.SingleRuns.Count);
        Assert.AreEqual("Single run 1", experiment.SingleRuns.Runs.Single().Name);
        StringAssert.Contains(detail.Message, "Single runs");
        detail.Runs.Refresh();
        Assert.IsTrue(detail.Runs.Rows.Any(r => r.Name == "Single run 1"), "the algorithm's Runs tab shows it too");

        // the experiment's runs table reaches down to the window's edge
        ws.Selected = experiment;
        Pump();
        var table = window.GetVisualDescendants().OfType<ContentControl>().First(c => c.Name == "Table" && c.IsEffectivelyVisible);
        var bottom = table.TranslatePoint(new Avalonia.Point(0, table.Bounds.Height), window)!.Value.Y;
        Assert.IsTrue(bottom > window.Bounds.Height - 80, $"the table ends at {bottom:0} of {window.Bounds.Height:0}");
        window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-single-experiment-runs.png"));

        // Runs tab: "Single runs" > experiment > algorithm
        vm.SelectedTab = MainViewModel.RunsTab;
        vm.Runs.Refresh();
        var singles = vm.Runs.Sources.Last();
        Assert.AreEqual("Single runs", singles.Name);
        var algorithmNode = singles.Children.Single().Children.Single();
        Assert.AreEqual(ga.Algorithm.Name, algorithmNode.Name);
        vm.Runs.SelectedSource = algorithmNode;
        Assert.AreEqual("Single run 1", vm.Runs.Runs!.Rows.Single().Name);
        Pump();
        window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-single-runs-tab.png"));

        // Results tab: the experiment alone, then with the single run ticked too
        vm.SelectedTab = MainViewModel.ResultsTab;
        var analysis = vm.RunAnalysis;
        analysis.RefreshSources();
        analysis.Sources.First().IsChecked = true;
        Assert.AreEqual(2, analysis.Table.Rows.Count, "single runs are not ticked at first");
        var singleSource = analysis.Sources.Single(s => s.Name == "Single runs");
        Assert.AreEqual(false, singleSource.IsChecked);
        singleSource.IsChecked = true;
        Assert.AreEqual(3, analysis.Table.Rows.Count);
        Assert.AreEqual(1, analysis.Table.Rows.Count(r => r.Text("source") == "Single runs"));
        Assert.IsTrue(analysis.Table.Rows.All(r => r.Text("param:Resources: CPU cores") != ""), "the single run is tagged with its resources too");
        Pump();
        window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-single-results.png"));

        // saved next to the experiment, back when it is opened again
        var path = Path.Combine(folder, "exp.hl");
        experiment.FilePath = path;
        await ws.SaveChangesAsync(experiment);
        var side = SingleRuns.FileFor(path);
        Assert.IsTrue(File.Exists(side), "exp.single-runs.hl");
        Assert.IsInstanceOfType<RunCollection>(Documents.Load(side), "a run collection HeuristicLab opens");
        var again = new MainViewModel(null, null);
        var reopened = await again.Workspace.OpenExperimentAsync(path);
        Assert.AreEqual(2, reopened!.Experiment.Runs.Count);
        Assert.AreEqual(1, reopened.SingleRuns.Count);
        window.Close();
        return true;
      }, CancellationToken.None);
    } finally {
      Directory.Delete(folder, recursive: true);
    }
  }
}
