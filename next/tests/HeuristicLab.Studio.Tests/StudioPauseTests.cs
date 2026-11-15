using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using HeuristicLab.Core;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>
/// Pause (user's request 2026-11-14): in the Experiments tab between "Start selected experiments" and Stop - pauses the
/// experiment runs and turns into Resume (the start pictogram); Stop while paused ends them. The quick batch run's Pause
/// works the same way.
/// </summary>
[TestClass]
public class StudioPauseTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  private static async Task Until(Func<bool> condition, int seconds = 30) {
    var end = DateTime.UtcNow.AddSeconds(seconds);
    while (!condition()) {
      if (DateTime.UtcNow > end) Assert.Fail("timed out");
      Pump();
      await Task.Delay(20);
    }
  }

  /// <summary>Paused and settled: their engines wait (the engine clock, ticking every 250 ms, stands still).</summary>
  private static async Task Frozen(params AlgorithmBlockViewModel[] algorithms) {
    await Task.Delay(350);
    TimeSpan[] Times() => algorithms.Select(a => a.Algorithm.ExecutionTime).ToArray();
    for (int attempt = 0; ; attempt++) {
      var before = Times();
      await Task.Delay(400);
      if (before.SequenceEqual(Times())) return;
      if (attempt > 20) Assert.Fail("still running");
    }
  }

  private static CatalogEntry Ga => Catalog.Find(Catalog.Algorithms(), "GeneticAlgorithm")!;

  private static AlgorithmBlockViewModel Endless(ExperimentWorkspaceViewModel ws, ContainerBlockViewModel container) {
    var block = ws.AddAlgorithm(container, Ga);
    ws.SetProblem(block, (IProblem)Setups.Create(new SetupRequest("GeneticAlgorithm") { Problem = "TravelingSalesmanProblem", Instance = "ch130" }).Algorithm.Problem!.Clone());
    ParameterEditor.Apply(block.Algorithm, ["PopulationSize=20", "MaximumGenerations=100000000"]);
    return block;
  }

  [TestMethod]
  [Timeout(300_000)]
  public async Task ExperimentsPauseResumeAndStop() {
    await session.Dispatch(async () => {
      var vm = new MainViewModel(null, null);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1400, Height = 800 };
      window.Show();
      // two experiments run at the same time (an experiment runs its algorithms one after the other)
      var experiment = ws.NewExperiment();
      var first = Endless(ws, experiment);
      var other = ws.NewExperiment();
      var second = Endless(ws, other);
      Pump();

      // the controls: Start all, Start selected, Pause, Stop, Eject - Pause like the others, pause pictogram
      var labels = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("picto"))
        .Select(b => b.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible).Text).ToList();
      CollectionAssert.AreEqual(new[] { "Start all experiments", "Start selected experiments", "Pause", "Stop", "Eject" }, labels);
      var pauseButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PauseResume");
      string Shown() => pauseButton.GetVisualDescendants().OfType<TextBlock>().Single(t => t.IsEffectivelyVisible).Text!;
      string ShownPictogram() => pauseButton.GetVisualDescendants().OfType<HeuristicLab.Studio.Controls.Pictogram>().Single(p => p.IsEffectivelyVisible).Symbol!;
      Assert.AreEqual("experiment_pause", ShownPictogram());
      // all five fit the experiments' column (none cut off at its right edge)
      var row = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "RunControls");
      var last = window.GetVisualDescendants().OfType<Button>().Last(b => b.Classes.Contains("picto"));
      Assert.IsTrue(last.TranslatePoint(new Point(last.Bounds.Width, 0), row)!.Value.X <= row.Bounds.Width + 0.5,
        $"Eject fits: ends at {last.TranslatePoint(new Point(last.Bounds.Width, 0), row)!.Value.X} of {row.Bounds.Width}");
      Assert.IsFalse(pauseButton.IsEffectivelyEnabled, "nothing runs");

      var run = ws.StartAllCommand.ExecuteAsync(null);
      await Until(() => first.Algorithm.ExecutionState == ExecutionState.Started && second.Algorithm.ExecutionState == ExecutionState.Started);
      Assert.IsTrue(pauseButton.IsEffectivelyEnabled);

      // Pause: the runs pause, the button turns into Resume (start pictogram)
      ws.PauseOrResumeCommand.Execute(null);
      await Frozen(first, second);
      Assert.AreEqual(ExecutionState.Started, first.Algorithm.ExecutionState, "held below the optimizers: they stay Started");
      Assert.AreEqual(AlgorithmStatus.Waiting, first.Status, "shown waiting, not working");
      Assert.IsTrue(ws.IsPausedExperiments);
      Assert.AreEqual("Resume", Shown());
      Assert.AreEqual("Experiment_run_selected", ShownPictogram());
      Assert.AreEqual("Paused", experiment.RunStatus);
      Assert.AreEqual("Paused", other.RunStatus);
      Assert.IsFalse(run.IsCompleted, "paused, not ended");
      Assert.IsFalse(ws.StartAllCommand.CanExecute(null), "no further starts while paused");
      var pausedAt = first.Algorithm.ExecutionTime;
      await Task.Delay(400);
      Assert.AreEqual(pausedAt, first.Algorithm.ExecutionTime, "no time passes while paused");
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-experiments-paused.png"));

      // Resume: they go on, the button is Pause again
      ws.PauseOrResumeCommand.Execute(null);
      Assert.AreEqual(AlgorithmStatus.Working, first.Status);
      Assert.IsFalse(ws.IsPausedExperiments);
      Assert.AreEqual("Pause", Shown());
      await Until(() => first.Algorithm.ExecutionTime > pausedAt);

      // paused again, then Stop: the runs end (stopped) and are recorded
      ws.PauseOrResumeCommand.Execute(null);
      await Frozen(first, second);
      ws.StopCommand.Execute(null);
      await run;
      Pump();
      Assert.AreEqual(ExecutionState.Stopped, first.Algorithm.ExecutionState);
      Assert.AreEqual(ExecutionState.Stopped, second.Algorithm.ExecutionState);
      Assert.AreEqual(1, experiment.Experiment.Runs.Count, "its run recorded");
      Assert.AreEqual(1, other.Experiment.Runs.Count, "the other's too");
      StringAssert.StartsWith(experiment.RunStatus, "Stopped");
      Assert.IsFalse(ws.IsPausedExperiments);
      Assert.AreEqual("Pause", Shown());
      Assert.IsFalse(pauseButton.IsEffectivelyEnabled);
      window.Close();
      return true;
    }, CancellationToken.None);
  }

  /// <summary>Pause / Resume pressed at random moments on experiments as Studio builds them: never a failure.</summary>
  [TestMethod]
  [Timeout(600_000)]
  public async Task PauseAndResumeAtAnyMomentInStudio() {
    await session.Dispatch(async () => {
      var vm = new MainViewModel(null, null);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1400, Height = 800 };
      window.Show();
      AlgorithmBlockViewModel Short(ContainerBlockViewModel container, int generations) {
        var block = Endless(ws, container);
        ParameterEditor.Apply(block.Algorithm, [$"MaximumGenerations={generations}", "PopulationSize=10"]);
        return block;
      }
      var random = new System.Random(3);
      var failures = new List<string>();
      for (int round = 0; round < 20; round++) {
        foreach (var old in ws.Experiments.ToList()) ws.Experiments.Remove(old);
        var experiment = ws.NewExperiment();
        var batch = ws.AddContainer(experiment, ExperimentTree.ContainerTypes().Single(e => e.Type == typeof(BatchRun)));
        batch.RepetitionsText = "100";
        Short(batch, 3);
        Short(batch, 2);
        var single = ws.AddContainer(experiment, ExperimentTree.ContainerTypes().Single(e => e.Type == typeof(BatchRun)));
        single.RepetitionsText = "100";
        Short(single, 2);
        var timed = ws.AddContainer(experiment, ExperimentTree.ContainerTypes().Single(e => e.Type == typeof(TimeLimitRun)));
        Short(timed, 4);
        Short(experiment, 3);
        var run = ws.StartAllCommand.ExecuteAsync(null);
        for (int i = 0; i < 40 && !run.IsCompleted; i++) {
          await Task.Delay(random.Next(0, 15));
          Pump();
          if (ws.PauseOrResumeCommand.CanExecute(null)) ws.PauseOrResumeCommand.Execute(null);
        }
        Pump();
        if (experiment.RunStatus.StartsWith("Failed", StringComparison.Ordinal)) failures.Add($"round {round}, before Stop: {experiment.RunStatus}");
        if (ws.IsPausedExperiments) ws.PauseOrResumeCommand.Execute(null);
        if (random.Next(2) == 0) await Task.Delay(random.Next(0, 10));
        if (ws.StopCommand.CanExecute(null)) ws.StopCommand.Execute(null);
        await run;
        Pump();
        if (experiment.RunStatus.StartsWith("Failed", StringComparison.Ordinal)) failures.Add($"round {round}: {experiment.RunStatus}");
      }
      Assert.AreEqual(0, failures.Count, string.Join("\n", failures));
      window.Close();
      return true;
    }, CancellationToken.None);
  }

  /// <summary>Paused, stopped, started again: it runs (it failed with "Start not allowed in execution state Stopped").</summary>
  [TestMethod]
  [Timeout(300_000)]
  public async Task StoppedWhilePausedExperimentsStartAgain() {
    await session.Dispatch(async () => {
      var vm = new MainViewModel(null, null);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1400, Height = 800 };
      window.Show();
      var experiment = ws.NewExperiment();
      var plain = Endless(ws, experiment);
      var batch = ws.AddContainer(experiment, ExperimentTree.ContainerTypes().Single(e => e.Type == typeof(BatchRun)));
      var inBatch = Endless(ws, batch);
      var timed = ws.AddContainer(experiment, ExperimentTree.ContainerTypes().Single(e => e.Type == typeof(TimeLimitRun)));
      var inTimed = Endless(ws, timed);
      foreach (var round in new[] { 1, 2, 3 }) {
        var run = ws.StartAllCommand.ExecuteAsync(null);
        await Until(() => ws.IsRunningExperiments && experiment.Experiment.ExecutionState == ExecutionState.Started || run.IsCompleted);
        Assert.IsFalse(run.IsCompleted, $"round {round}: {experiment.RunStatus}");
        await Task.Delay(200);
        ws.PauseOrResumeCommand.Execute(null);
        await Frozen(plain, inBatch, inTimed);
        ws.StopCommand.Execute(null);
        await run;
        Pump();
        StringAssert.StartsWith(experiment.RunStatus, "Stopped", $"round {round}");
      }
      window.Close();
      return true;
    }, CancellationToken.None);
  }

  /// <summary>Pause pressed between two runs of a quick batch run (the algorithm stopped for an instant): the next run pauses.</summary>
  [TestMethod]
  [Timeout(300_000)]
  public async Task AQuickBatchRunPausedBetweenTwoRuns() {
    await session.Dispatch(async () => {
      var vm = new MainViewModel(null, null);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1400, Height = 800 };
      window.Show();
      var experiment = ws.NewExperiment();
      var ga = Endless(ws, experiment);
      ParameterEditor.Apply(ga.Algorithm, ["MaximumGenerations=3"]);
      ws.QuickBatchSize = 3;
      ws.Selected = ga;
      var detail = (AlgorithmDetailViewModel)ws.Detail!;
      bool pressed = false;
      ga.Algorithm.Stopped += (_, _) => {
        if (pressed) return;
        pressed = true;
        detail.PauseCommand.Execute(null);  // the first run just stopped, the second not started
      };
      detail.StartCommand.Execute(null);
      await Until(() => pressed && ga.Algorithm.ExecutionState == ExecutionState.Paused || detail.WaitAsync().IsCompleted && pressed);
      Assert.AreEqual(ExecutionState.Paused, ga.Algorithm.ExecutionState, "the next run paused");
      Assert.AreEqual(0, experiment.QuickBatchRuns.Count, "not ended");
      Pump();
      Assert.AreEqual("\u25B6 Resume", detail.PauseText);
      detail.PauseCommand.Execute(null);  // Resume
      await detail.WaitAsync();
      Pump();
      Assert.AreEqual(3, experiment.QuickBatchRuns.Count, "the quick batch run went on to its 3 runs");
      window.Close();
      return true;
    }, CancellationToken.None);
  }

  [TestMethod]
  [Timeout(300_000)]
  public async Task AQuickBatchRunPausesResumesAndStops() {
    await session.Dispatch(async () => {
      var vm = new MainViewModel(null, null);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1400, Height = 800 };
      window.Show();
      var experiment = ws.NewExperiment();
      var ga = Endless(ws, experiment);
      ws.QuickBatchSize = 3;
      ws.Selected = ga;
      var detail = (AlgorithmDetailViewModel)ws.Detail!;
      Pump();
      Button Exec(string start) => window.GetVisualDescendants().OfType<Button>()
        .Single(b => b.Classes.Contains("exec") && b.IsEffectivelyVisible && (b.Content as string ?? "").Contains(start));
      var pause = Exec("Pause");

      detail.StartCommand.Execute(null);
      await Until(() => ga.Algorithm.ExecutionState == ExecutionState.Started);
      detail.PauseCommand.Execute(null);
      await Until(() => ga.Algorithm.ExecutionState == ExecutionState.Paused);
      Pump();
      Assert.AreEqual("▶ Resume", pause.Content, "Pause turned into Resume");
      Assert.IsTrue(pause.IsEffectivelyEnabled);
      Assert.IsFalse(detail.StartCommand.CanExecute(null), "Resume goes on, not Start");
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-qbr-paused.png"));

      detail.PauseCommand.Execute(null);  // Resume
      await Until(() => ga.Algorithm.ExecutionState == ExecutionState.Started);
      Pump();
      Assert.AreEqual("⏸ Pause", pause.Content);
      Assert.IsNotNull(detail.BatchProgress, "the same quick batch run goes on");

      detail.PauseCommand.Execute(null);
      await Until(() => ga.Algorithm.ExecutionState == ExecutionState.Paused);
      detail.StopCommand.Execute(null);
      await detail.WaitAsync();
      Pump();
      Assert.AreEqual(1, experiment.QuickBatchRuns.Count, "stopped while paused: the run made so far is the quick batch run");
      Assert.AreEqual("⏸ Pause", pause.Content);
      Assert.IsFalse(ws.IsRunning);
      window.Close();
      return true;
    }, CancellationToken.None);
  }
}
