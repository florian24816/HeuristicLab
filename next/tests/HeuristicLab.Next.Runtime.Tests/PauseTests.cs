using HeuristicLab.Algorithms.GeneticAlgorithm;
using HeuristicLab.Core;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Next.Runtime.Tests;

/// <summary>RunOptions.Pause: a run paused goes on where it was when resumed; stopped while paused it ends as stopped.</summary>
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

  private static async Task PauseResumeStop(IOptimizer optimizer, Func<IEnumerable<IOptimizer>> parts) {
    var pause = new RunPause();
    using var stop = new CancellationTokenSource();
    var run = OptimizerRunner.RunAsync(optimizer, new RunOptions { Pause = pause }, cancellationToken: stop.Token);
    await Until(() => optimizer.ExecutionState == ExecutionState.Started);
    await Task.Delay(300);
    pause.Pause();
    await Until(() => optimizer.ExecutionState == ExecutionState.Paused);
    Assert.IsTrue(parts().All(p => p.ExecutionState is ExecutionState.Paused or ExecutionState.Prepared), "every part paused");
    var pausedAt = optimizer.ExecutionTime;
    await Task.Delay(500);
    Assert.IsFalse(run.IsCompleted, "a pause does not end the run");
    Assert.AreEqual(ExecutionState.Paused, optimizer.ExecutionState);
    Assert.AreEqual(pausedAt, optimizer.ExecutionTime, "no time passes while paused");

    pause.Resume();
    await Until(() => optimizer.ExecutionState == ExecutionState.Started);
    await Task.Delay(300);
    pause.Pause();
    await Until(() => optimizer.ExecutionState == ExecutionState.Paused);
    // Stop while paused: the run ends as stopped and is recorded
    stop.Cancel();
    var report = await run;
    Assert.AreEqual(RunOutcome.Stopped, report.Outcome);
    Assert.AreEqual(ExecutionState.Stopped, optimizer.ExecutionState);
    Assert.IsTrue(report.Runs.Count >= 1, "its run recorded");
    Assert.IsTrue(report.ExecutionSeconds >= pausedAt.TotalSeconds, "it went on after the resume");
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
  public async Task WithoutPauseItRunsToTheEnd() {
    var ga = (GeneticAlgorithm)Documents.Load(LegacyGaTsp);
    ga.MaximumGenerations.Value = 20;
    var report = await OptimizerRunner.RunAsync(ga, new RunOptions { Pause = new RunPause(), Seed = 1 });
    Assert.AreEqual(RunOutcome.Completed, report.Outcome);
  }
}
