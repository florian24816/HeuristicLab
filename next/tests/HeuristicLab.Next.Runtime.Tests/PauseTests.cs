using HeuristicLab.Algorithms.GeneticAlgorithm;
using HeuristicLab.Core;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Next.Runtime.Tests;

/// <summary>
/// RunOptions.Pause: paused, the run's engines wait before their next operation (the optimizers stay Started and never
/// see a pause); resumed it goes on where it was; stopped while paused it ends as stopped.
/// </summary>
[TestClass]
public class PauseTests {
  private static string LegacyGaTsp => Path.Combine(AppContext.BaseDirectory, "TestData", "GA_TSP.hl");

  private static GeneticAlgorithm LongGa() {
    var ga = (GeneticAlgorithm)Documents.Load(LegacyGaTsp);
    ga.MaximumGenerations.Value = 1_000_000;
    return ga;
  }

  private static async Task Until(Func<bool> condition, int seconds = 20) {
    var end = DateTime.UtcNow.AddSeconds(seconds);
    while (!condition()) {
      if (DateTime.UtcNow > end) Assert.Fail("timed out");
      await Task.Delay(20);
    }
  }

  /// <summary>Paused and settled: the running engines wait at the hold.</summary>
  private static Task Held(RunPause pause) => Until(() => pause.Waiting > 0);

  private static async Task PauseResumeStop(IOptimizer optimizer, Func<IEnumerable<IAlgorithm>> parts) {
    var pause = new RunPause();
    using var stop = new CancellationTokenSource();
    var run = OptimizerRunner.RunAsync(optimizer, new RunOptions { Pause = pause }, cancellationToken: stop.Token);
    await Until(() => optimizer.ExecutionState == ExecutionState.Started);
    await Task.Delay(300);
    pause.Pause();
    await Held(pause);
    await Task.Delay(300);  // the engine clock catches up (it ticks every 250 ms)
    var pausedAt = parts().Select(p => p.ExecutionTime).ToList();
    await Task.Delay(600);
    Assert.IsFalse(run.IsCompleted, "a pause does not end the run");
    Assert.AreEqual(ExecutionState.Started, optimizer.ExecutionState, "the optimizers never see the pause");
    CollectionAssert.AreEqual(pausedAt, parts().Select(p => p.ExecutionTime).ToList(), "no time passes while paused");

    pause.Resume();
    await Until(() => pause.Waiting == 0);
    await Until(() => parts().Select(p => p.ExecutionTime).Sum(t => t.Ticks) > pausedAt.Sum(t => t.Ticks));
    pause.Pause();
    await Held(pause);
    // Stop while paused: the run ends as stopped and is recorded
    stop.Cancel();
    var report = await run;
    Assert.AreEqual(RunOutcome.Stopped, report.Outcome);
    Assert.AreEqual(ExecutionState.Stopped, optimizer.ExecutionState);
    Assert.IsTrue(report.Runs.Count >= 1, "its run recorded");
    Assert.IsTrue(report.ExecutionSeconds >= pausedAt.Sum(t => t.TotalSeconds), "it went on after the resume");
  }

  [TestMethod]
  [Timeout(120_000)]
  public Task AnAlgorithmPausesResumesAndStops() {
    var ga = LongGa();
    return PauseResumeStop(ga, () => [ga]);
  }

  [TestMethod]
  [Timeout(120_000)]
  public Task AnExperimentPausesResumesAndStops() {
    var experiment = new Experiment();
    var first = LongGa();
    var second = LongGa();
    experiment.Optimizers.Add(first);
    experiment.Optimizers.Add(second);
    return PauseResumeStop(experiment, () => [first, second]);
  }

  [TestMethod]
  [Timeout(120_000)]
  public Task ABatchRunAndATimeLimitRunPauseResumeAndStop() {
    var experiment = new Experiment { NumberOfWorkers = 2 };
    var inBatch = LongGa();
    var batch = new BatchRun { Repetitions = 3 };
    ExperimentTree.Add(batch, inBatch);
    var inTimed = LongGa();
    var timed = new TimeLimitRun { MaximumExecutionTime = TimeSpan.FromMinutes(5) };
    ExperimentTree.Add(timed, inTimed);
    ExperimentTree.Add(experiment, batch);
    ExperimentTree.Add(experiment, timed);
    return PauseResumeStop(experiment, () => [inBatch, inTimed]);
  }

  /// <summary>Stopped while paused, the optimizer starts again (a fresh run) - it failed with "Start not allowed in execution state Stopped".</summary>
  [TestMethod]
  [Timeout(120_000)]
  public async Task StoppedWhilePausedItStartsAgain() {
    foreach (var make in new Func<IOptimizer>[] { LongGa, () => { var e = new Experiment(); e.Optimizers.Add(LongGa()); e.Optimizers.Add(LongGa()); return e; } }) {
      var optimizer = make();
      var pause = new RunPause();
      using (var stop = new CancellationTokenSource()) {
        var run = OptimizerRunner.RunAsync(optimizer, new RunOptions { Pause = pause }, cancellationToken: stop.Token);
        await Until(() => optimizer.ExecutionState == ExecutionState.Started);
        await Task.Delay(200);
        pause.Pause();
        await Held(pause);
        stop.Cancel();
        Assert.AreEqual(RunOutcome.Stopped, (await run).Outcome);
      }
      var again = new RunPause();
      using var second = new CancellationTokenSource();
      var rerun = OptimizerRunner.RunAsync(optimizer, new RunOptions { Pause = again }, cancellationToken: second.Token);
      await Until(() => optimizer.ExecutionState == ExecutionState.Started || rerun.IsCompleted);
      Assert.IsFalse(rerun.IsCompleted, rerun.IsCompleted ? rerun.Result.Error : "");
      second.Cancel();
      var report = await rerun;
      Assert.AreEqual(RunOutcome.Stopped, report.Outcome, report.Error);
    }
  }

  /// <summary>Stop while paused, for experiments holding an algorithm, a batch run, a time-limit run, all of them (and all without a pause).</summary>
  [TestMethod]
  [Timeout(120_000)]
  [DataRow("plain")]
  [DataRow("batch")]
  [DataRow("timed")]
  [DataRow("all")]
  [DataRow("all-nopause")]
  public async Task StopWhilePausedEndsAsStopped(string kind) {
    var experiment = new Experiment();
    if (kind is "plain" or "all" or "all-nopause") experiment.Optimizers.Add(LongGa());
    if (kind is "batch" or "all" or "all-nopause") experiment.Optimizers.Add(new BatchRun { Optimizer = LongGa(), Repetitions = 10 });
    if (kind is "timed" or "all" or "all-nopause") experiment.Optimizers.Add(new TimeLimitRun { Algorithm = LongGa(), MaximumExecutionTime = TimeSpan.FromMinutes(5) });
    var pause = new RunPause();
    using var stop = new CancellationTokenSource();
    var run = OptimizerRunner.RunAsync(experiment, new RunOptions { Pause = pause }, cancellationToken: stop.Token);
    await Until(() => experiment.ExecutionState == ExecutionState.Started);
    await Task.Delay(300);
    if (kind != "all-nopause") {
      pause.Pause();
      await Held(pause);
    }
    stop.Cancel();
    var report = await run;
    Assert.AreEqual(RunOutcome.Stopped, report.Outcome, report.Error);
  }

  private static GeneticAlgorithm ShortGa(int generations) {
    var ga = (GeneticAlgorithm)Documents.Load(LegacyGaTsp);
    ga.MaximumGenerations.Value = generations;
    ga.PopulationSize.Value = 10;
    return ga;
  }

  /// <summary>
  /// Pause exactly between two optimizers of an experiment (the first just stopped, the next not started): HeuristicLab's
  /// own Pause left the experiment Started though nothing ran (alone its run counted as completed, inside a batch run the
  /// batch run failed with "Prepare not allowed in execution state Started"). Held engines: the next algorithm starts and
  /// waits at its first operation; resumed, everything finishes.
  /// </summary>
  [TestMethod]
  [Timeout(120_000)]
  [DataRow(false)]
  [DataRow(true)]
  public async Task PausedBetweenTwoOptimizers(bool inBatchRun) {
    var first = ShortGa(2);
    var second = ShortGa(2);
    // as Studio builds them: a batch run with two blocks holds them in an inner "Batch content" experiment
    IOptimizer root = inBatchRun ? new BatchRun { Repetitions = 2 } : new Experiment();
    ExperimentTree.Add(root, first);
    ExperimentTree.Add(root, second);
    var pause = new RunPause();
    bool pausedOnce = false;
    first.Stopped += (_, _) => {
      if (pausedOnce) return;
      pausedOnce = true;
      pause.Pause();  // the experiment is between its two algorithms now
    };
    var run = OptimizerRunner.RunAsync(root, new RunOptions { Pause = pause });
    await Until(() => pausedOnce && pause.Waiting > 0 || run.IsCompleted);
    Assert.IsFalse(run.IsCompleted, run.IsCompleted ? $"ended instead of pausing: {(await run).Outcome} {(await run).Error}" : "");
    Assert.AreEqual(ExecutionState.Started, second.ExecutionState, "the second algorithm started and waits");
    await Task.Delay(300);
    Assert.IsFalse(run.IsCompleted);
    pause.Resume();
    var report = await run;
    Assert.AreEqual(RunOutcome.Completed, report.Outcome, report.Error);
    Assert.AreEqual(inBatchRun ? 4 : 2, report.Runs.Count, "every algorithm ran (each repetition)");
  }

  /// <summary>
  /// Pause and Resume pressed at any moment - also between two repetitions of a batch run, while a time-limit run or
  /// an experiment starts its next part: the run must neither fail nor hang ("Prepare not allowed in execution state
  /// Started" was seen), and Stop must end it.
  /// </summary>
  [TestMethod]
  [Timeout(600_000)]
  [DataRow(1, 7)]
  [DataRow(4, 8)]
  [DataRow(1, 9)]
  public async Task PauseAndResumeAtAnyMoment(int workers, int seed) {
    var random = new System.Random(seed);
    for (int round = 0; round < 30; round++) {
      var experiment = new Experiment();
      var batch = new BatchRun { Repetitions = 200 };
      ExperimentTree.Add(experiment, batch);
      ExperimentTree.Add(batch, ShortGa(3));
      ExperimentTree.Add(batch, ShortGa(2));  // several children: Studio's "Batch content" experiment
      var single = new BatchRun { Repetitions = 200 };
      ExperimentTree.Add(experiment, single);
      ExperimentTree.Add(single, ShortGa(2));
      var timed = new TimeLimitRun { MaximumExecutionTime = TimeSpan.FromSeconds(30) };
      ExperimentTree.Add(experiment, timed);
      ExperimentTree.Add(timed, ShortGa(1_000_000));
      ExperimentTree.Add(experiment, ShortGa(5));
      experiment.NumberOfWorkers = workers;

      var pause = new RunPause();
      using var stop = new CancellationTokenSource();
      var run = OptimizerRunner.RunAsync(experiment, new RunOptions { Pause = pause }, cancellationToken: stop.Token);
      for (int i = 0; i < 60 && !run.IsCompleted; i++) {
        await Task.Delay(random.Next(0, 12));
        if (pause.IsPaused) pause.Resume(); else pause.Pause();
      }
      Assert.IsFalse(run.IsCompleted && (await run).Outcome == RunOutcome.Failed, run.IsCompleted ? (await run).Error : "");
      pause.Resume();
      await Task.Delay(random.Next(1, 40));
      stop.Cancel();
      var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(30)));
      Assert.AreSame(run, finished, $"round {round}: Stop must end the run (state {experiment.ExecutionState})");
      var report = await run;
      Assert.AreNotEqual(RunOutcome.Failed, report.Outcome, $"round {round}: {report.Error}");
    }
  }

  [TestMethod]
  [Timeout(120_000)]
  public async Task WithoutPauseItRunsToTheEnd() {
    var ga = (GeneticAlgorithm)Documents.Load(LegacyGaTsp);
    ga.MaximumGenerations.Value = 20;
    var report = await OptimizerRunner.RunAsync(ga, new RunOptions { Pause = new RunPause(), Seed = 1 });
    Assert.AreEqual(RunOutcome.Completed, report.Outcome);
  }
}
