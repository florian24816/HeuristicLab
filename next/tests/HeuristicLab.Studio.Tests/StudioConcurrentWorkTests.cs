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
/// Only the experiments that run are locked (started with the start buttons, or a quick batch run of one of their
/// algorithms): the others, and new ones, can be changed and started meanwhile.
/// </summary>
[TestClass]
public class StudioConcurrentWorkTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  private static CatalogEntry Ga => Catalog.Find(Catalog.Algorithms(), "GeneticAlgorithm")!;

  private static IProblem Tsp() =>
    (IProblem)Setups.Create(new SetupRequest("GeneticAlgorithm") { Problem = "TravelingSalesmanProblem", Instance = "ch130" }).Algorithm.Problem!.Clone();

  /// <summary>An algorithm that runs until it is stopped.</summary>
  private static AlgorithmBlockViewModel Endless(ExperimentWorkspaceViewModel ws, ContainerBlockViewModel container) {
    var block = ws.AddAlgorithm(container, Ga);
    ws.SetProblem(block, Tsp());
    ParameterEditor.Apply(block.Algorithm, ["PopulationSize=20", "MaximumGenerations=100000000"]);
    return block;
  }

  private static Button AddButton(Window window, ContainerBlockViewModel container) =>
    window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("addblock") && b.DataContext == container);

  [TestMethod]
  [Timeout(300_000)]
  public async Task OtherExperimentsCanBeChangedAndStartedWhileOneRuns() {
    await session.Dispatch(async () => {
      var vm = new MainViewModel(null, null);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
      window.Show();
      var a = ws.NewExperiment();
      var running = Endless(ws, a);
      var b = ws.NewExperiment();
      var bAlgorithm = ws.AddAlgorithm(b, Ga);
      ws.SetProblem(bAlgorithm, Tsp());
      ParameterEditor.Apply(bAlgorithm.Algorithm, ["PopulationSize=20", "MaximumGenerations=3"]);

      // A started alone ("Start selected"): A is locked, B is not
      a.IsChecked = true;
      var runA = ws.StartSelectedCommand.ExecuteAsync(null);
      Pump();
      Assert.IsTrue(a.IsRunning && a.IsLocked);
      Assert.IsFalse(b.IsLocked);
      Assert.IsFalse(a.IsEditable || running.IsEditable);
      Assert.IsTrue(b.IsEditable && bAlgorithm.IsEditable);
      CollectionAssert.AreEqual(new[] { "Show runs" }, a.Menu.Select(m => m.Display).ToList(), "the running one offers no changes");
      Assert.IsTrue(b.Menu.Any(m => m.Display == "Rename"), "the other one does");
      Pump();
      Assert.IsFalse(AddButton(window, a).IsEnabled);
      Assert.IsTrue(AddButton(window, b).IsEnabled);
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-concurrent-work.png"));

      // B: add, rename, change a parameter, move, copy, delete - all while A runs
      var added = ws.AddAlgorithm(b, Ga);
      added.Name = "Added while A runs";
      Assert.AreEqual("Added while A runs", added.Name);
      ws.Selected = bAlgorithm;
      var detail = (AlgorithmDetailViewModel)ws.Detail!;
      Assert.IsTrue(detail.CanEdit, "its parameters can be changed");
      Assert.IsTrue(ws.Move(added, b, 0));
      CollectionAssert.AreEqual(new[] { "Added while A runs", bAlgorithm.Name }, b.Children.Select(c => c.Name).ToList());
      Assert.AreEqual(1, ws.Copy(added, 1).Count);
      await ws.DeleteAsync(b.Children[1]);
      Assert.AreEqual(2, b.Children.Count);
      // A's blocks stay as they are
      running.Name = "renamed";
      Assert.AreNotEqual("renamed", running.Name);
      Assert.IsFalse(ws.Move(added, a, 0), "nothing goes into a running experiment");
      ws.Selected = running;
      Assert.IsFalse(((AlgorithmDetailViewModel)ws.Detail!).CanEdit);

      // a new experiment while A runs
      var c = ws.NewExperiment();
      Assert.IsTrue(c.IsEditable);
      var cAlgorithm = ws.AddAlgorithm(c, Ga);
      ws.SetProblem(cAlgorithm, Tsp());
      Assert.IsTrue(cAlgorithm.HasProblem);

      // B started too while A still runs; "Start all" would start the ones that do not run (B and C)
      await ws.DeleteAsync(b.Children[0]);
      Assert.IsTrue(ws.StartAllCommand.CanExecute(null));
      a.IsChecked = false;
      b.IsChecked = true;
      await ws.StartSelectedCommand.ExecuteAsync(null);
      Assert.IsFalse(b.IsRunning, "B finished");
      Assert.AreEqual(1, b.Experiment.Runs.Count);
      Assert.IsTrue(a.IsRunning, "A still runs");
      Assert.IsTrue(ws.IsRunning && ws.IsRunningExperiments);

      ws.StopCommand.Execute(null);
      await runA;
      Assert.IsFalse(a.IsRunning || a.IsLocked);
      Assert.IsFalse(ws.IsRunning);
      Assert.IsTrue(running.IsEditable);
      window.Close();
      return true;
    }, CancellationToken.None);
  }

  [TestMethod]
  [Timeout(300_000)]
  public async Task AQuickBatchRunLocksOnlyItsExperiment() {
    await session.Dispatch(async () => {
      var vm = new MainViewModel(null, null);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
      window.Show();
      var a = ws.NewExperiment();
      var qbr = Endless(ws, a);
      var b = ws.NewExperiment();
      var other = ws.AddAlgorithm(b, Ga);
      ws.SetProblem(other, Tsp());
      ParameterEditor.Apply(other.Algorithm, ["PopulationSize=20", "MaximumGenerations=3"]);

      ws.Selected = qbr;
      var detail = (AlgorithmDetailViewModel)ws.Detail!;
      detail.StartCommand.Execute(null);
      Pump();
      Assert.IsTrue(a.IsLocked, "its experiment is locked");
      Assert.IsFalse(a.IsRunning, "not started with the start buttons");
      Assert.IsFalse(b.IsLocked);
      Assert.IsTrue(b.IsEditable && other.IsEditable);
      Assert.IsTrue(ws.StartAllCommand.CanExecute(null), "B can be started (A goes on with its quick batch run)");

      // the other experiment: changed, its algorithm started (a QBR of its own), saved state follows
      ws.AddAlgorithm(b, Ga).Name = "New one";
      ws.Selected = other;
      var otherDetail = (AlgorithmDetailViewModel)ws.Detail!;
      Assert.IsTrue(otherDetail.CanEdit);
      ws.QuickBatchSize = 2;
      otherDetail.StartCommand.Execute(null);
      await otherDetail.WaitAsync();
      Pump();
      Assert.AreEqual(2, b.QuickBatchRuns.Count, "B's quick batch run while A's goes on");
      Assert.IsFalse(b.IsLocked);
      Assert.IsTrue(a.IsLocked);
      // a new experiment meanwhile
      var c = ws.NewExperiment();
      Assert.IsTrue(c.IsEditable);
      ws.AddAlgorithm(c, Ga);

      detail.StopCommand.Execute(null);
      await detail.WaitAsync();
      Pump();
      Assert.IsFalse(a.IsLocked);
      Assert.IsFalse(ws.IsRunning);
      Assert.AreEqual(1, a.QuickBatchRuns.Count, "stopped: the runs made so far");
      window.Close();
      return true;
    }, CancellationToken.None);
  }
}
