using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;
using HeuristicLab.Studio.Controls;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>Time-limit runs with several blocks (and nested ones), and running experiments: clickable, but not changeable.</summary>
[TestClass]
public class StudioTimeLimitAndRunningTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static CatalogEntry TabuSearch => Catalog.Find(Catalog.Algorithms(), "TabuSearch")!;
  private static CatalogEntry TimeLimit => ExperimentTree.ContainerTypes().Single(e => e.Type == typeof(TimeLimitExperiment));

  private static IProblem Tsp() {
    var scratch = Setups.Create(new SetupRequest("TabuSearch") { Problem = "TravelingSalesmanProblem", Instance = "berlin52" }).Algorithm;
    return (IProblem)scratch.Problem!.Clone();
  }

  private static AlgorithmBlockViewModel Algorithm(ExperimentWorkspaceViewModel ws, ContainerBlockViewModel container, int iterations) {
    var block = ws.AddAlgorithm(container, TabuSearch);
    ws.SetProblem(block, Tsp());
    ParameterEditor.Apply(block.Algorithm, [$"MaximumIterations={iterations}"]);
    return block;
  }

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  private static async Task Until(Func<bool> condition, string what, int seconds = 60) {
    var clock = System.Diagnostics.Stopwatch.StartNew();
    while (!condition()) {
      if (clock.Elapsed.TotalSeconds > seconds) Assert.Fail($"Timed out waiting for: {what}");
      await Task.Delay(20);
    }
  }

  private static Border BlockOf(Window window, BlockViewModel block) =>
    window.GetVisualDescendants().OfType<Border>().First(b => b.IsEffectivelyVisible && b.Classes.Contains("block") && b.DataContext == block);

  [TestMethod]
  public async Task TimeLimitRunsTakeSeveralBlocksAndWarnAboutLongerInnerLimits() {
    var shot = await session.Dispatch(() => {
      var vm = new MainViewModel(null);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1300, Height = 800 };
      window.Show();
      var experiment = ws.NewExperiment();
      var outer = ws.AddContainer(experiment, TimeLimit);
      outer.TimeLimitText = "5m";
      Algorithm(ws, outer, 5);
      Algorithm(ws, outer, 5);
      Assert.IsTrue(outer.CanAdd, "after the first block the add button stays");
      Assert.IsTrue(outer.AddChoices.Any(c => c.Display == "Time-limit run"), "a time-limit run can go inside one");
      var inner = ws.AddContainer(outer, TimeLimit);
      inner.TimeLimitText = "10m";
      Algorithm(ws, inner, 5);
      CollectionAssert.AreEqual(new[] { "Tabu Search (TS)", "Tabu Search (TS)", inner.Name }, outer.Children.Select(c => c.Name).ToList());

      Assert.IsTrue(inner.HasBadgeWarning, "10 min inside 5 min: yellow");
      Assert.IsFalse(outer.HasBadgeWarning);
      StringAssert.Contains(inner.BadgeTip, "stops it first");
      StringAssert.Contains(experiment.Warnings, "it is stopped after 05:00 anyway");
      Assert.IsTrue(ws.StartAllCommand.CanExecute(null), "it may still run");
      ws.Selected = null;
      Pump();
      var badge = window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("badge") && b.DataContext == inner);
      Assert.IsTrue(badge.Classes.Contains("warning"));
      Assert.AreEqual(Avalonia.Media.Color.Parse("#F4E803"), ((Avalonia.Media.ISolidColorBrush)badge.Background!).Color, "the yellow of the unsaved pictogram");
      var file = Path.Combine(AppContext.BaseDirectory, "studio-time-limit-nested.png");
      window.CaptureRenderedFrame()!.Save(file);

      // shorter than the outer one: fine again
      inner.TimeLimitText = "2m";
      Assert.IsFalse(inner.HasBadgeWarning);
      Assert.AreEqual("", experiment.Warnings);
      // and a longer outer one makes the inner one fine as well
      inner.TimeLimitText = "10m";
      outer.TimeLimitText = "20m";
      Assert.IsFalse(inner.HasBadgeWarning);
      window.Close();
      return Task.FromResult(file);
    }, CancellationToken.None);
    Assert.IsTrue(new FileInfo(shot).Length > 5_000);
  }

  [TestMethod]
  public async Task RunningBlocksCanBeClickedButNotChanged() {
    var shot = await session.Dispatch(async () => {
      var vm = new MainViewModel(null);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1300, Height = 800 };
      window.Show();
      var experiment = ws.NewExperiment();
      var limit = ws.AddContainer(experiment, TimeLimit);
      limit.TimeLimitText = "3s";
      var first = Algorithm(ws, limit, 100_000_000);
      var second = Algorithm(ws, limit, 100_000_000);
      ws.Selected = null;
      Pump();
      Assert.IsTrue(first.Menu.Any(m => m.Display == "Rename"));

      var run = ws.StartAllCommand.ExecuteAsync(null);
      await Until(() => first.Status == AlgorithmStatus.Working, "running");
      Pump();

      // clickable: selecting shows the details
      var at = BlockOf(window, second);
      var point = at.TranslatePoint(new Point(at.Bounds.Width / 2, at.Bounds.Height / 2), window)!.Value;
      window.MouseDown(point, MouseButton.Left);
      window.MouseUp(point, MouseButton.Left);
      Pump();
      Assert.AreSame(second, ws.Selected, "a block of a running experiment can be clicked");
      Assert.AreSame(second.Detail, ws.Detail);

      // but nothing changes
      Assert.IsFalse(second.Menu.Any(m => m.Display is "Rename" or "Delete" or "Copy" or "Change problem..."), string.Join(", ", second.Menu.Select(m => m.Display)));
      Assert.IsFalse(experiment.Menu.Any(m => m.Display == "Save as..."), "a running experiment cannot be saved");
      second.Name = "Renamed";
      Assert.AreEqual("Tabu Search (TS)", second.Name, "no renaming while it runs");
      second.RenameCommand.Execute(null);
      Assert.IsFalse(second.IsEditingName);
      limit.TimeLimitText = "1h";
      Assert.AreEqual("3 s", limit.TimeLimitText);
      Assert.IsFalse(ws.BeginDrag(second), "no moving");
      Assert.AreEqual(0, ws.Copy(second, 1).Count, "no copying");
      var add = window.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.DataContext == limit && Equals(b.Content, limit.AddText));
      Assert.IsFalse(add.IsEnabled, "add buttons are disabled");

      // parameters show they cannot be changed now: value and choice parameters get the not-editable pictogram
      var parameters = second.Detail.AlgorithmParameters.Parameters;
      Assert.AreEqual("parameter_currently_not_editable", parameters.First(p => p.Name == "MaximumIterations").KindPictogram);
      Assert.AreEqual("parameter_currently_not_editable", parameters.First(p => p.Name == "MoveGenerator").KindPictogram);
      var maxIterations = parameters.First(p => p.Name == "MaximumIterations").Editor;
      Assert.IsFalse(maxIterations.ToggleMulti(), "no editing across algorithms either");
      second.Detail.AlgorithmParameters.Selected = parameters.First(p => p.Name == "MaximumIterations");
      Pump();
      Assert.IsTrue(window.GetVisualDescendants().OfType<Pictogram>().Any(p => p.IsEffectivelyVisible && p.Symbol == "parameter_currently_not_editable"));
      var file = Path.Combine(AppContext.BaseDirectory, "studio-running-readonly.png");
      window.CaptureRenderedFrame()!.Save(file);

      // the time limit stops it; afterwards everything can be changed again
      await run;
      Pump();
      Assert.AreEqual(AlgorithmStatus.Finished, first.Status, "stopped by the time limit: finished");
      Assert.AreEqual(AlgorithmStatus.NotRun, second.Status, "never got its turn");
      Assert.AreEqual(1, limit.Optimizer.Runs.Count);
      Assert.AreEqual("value_parameter", second.Detail.AlgorithmParameters.Parameters.First(p => p.Name == "MaximumIterations").KindPictogram);
      Assert.IsTrue(second.Menu.Any(m => m.Display == "Rename"));
      second.Name = "Renamed";
      Assert.AreEqual("Renamed", second.Name);
      window.Close();
      return file;
    }, CancellationToken.None);
    Assert.IsTrue(new FileInfo(shot).Length > 5_000);
  }
}
