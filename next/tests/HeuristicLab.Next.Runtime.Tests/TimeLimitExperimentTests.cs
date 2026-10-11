using HeuristicLab.Core;
using HeuristicLab.Optimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Next.Runtime.Tests;

/// <summary>The time-limit run that holds several building blocks and limits their total time.</summary>
[TestClass]
public class TimeLimitExperimentTests {
  private static IAlgorithm TabuSearch(int iterations) {
    var algorithm = Setups.Create(new SetupRequest("TabuSearch") { Problem = "TravelingSalesmanProblem", Instance = "berlin52" }).Algorithm;
    ParameterEditor.Apply(algorithm, [$"MaximumIterations={iterations}"]);
    return algorithm;
  }

  [TestMethod]
  public async Task SeveralBlocksRunOneAfterAnotherWithinTheLimit() {
    var limit = new TimeLimitExperiment(TimeSpan.FromSeconds(30));
    ExperimentTree.Add(limit, TabuSearch(5));
    ExperimentTree.Add(limit, TabuSearch(5));
    var batch = new BatchRun { Repetitions = 2 };
    ExperimentTree.Add(batch, TabuSearch(5));
    ExperimentTree.Add(limit, batch);
    Assert.AreEqual(3, ExperimentTree.Children(limit).Count, "any building blocks, also containers");

    var report = await OptimizerRunner.RunAsync(limit);
    Assert.AreEqual(RunOutcome.Completed, report.Outcome, report.Error);
    Assert.AreEqual(4, limit.Runs.Count, "2 algorithms + 2 repetitions, well within the time");
    Assert.IsFalse(limit.StoppedByTimeLimit);
  }

  [TestMethod]
  public async Task WhatStillRunsIsStoppedWhenTheTimeIsUp() {
    var limit = new TimeLimitExperiment(TimeSpan.FromSeconds(1.5));
    ExperimentTree.Add(limit, TabuSearch(100_000_000));
    ExperimentTree.Add(limit, TabuSearch(100_000_000));
    var clock = System.Diagnostics.Stopwatch.StartNew();
    var report = await OptimizerRunner.RunAsync(limit);
    Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(30), $"stopped by the limit ({clock.Elapsed})");
    Assert.IsTrue(limit.StoppedByTimeLimit);
    Assert.AreEqual(1, limit.Runs.Count, "the first algorithm used up the time and was stopped (with its run)");
    Assert.AreEqual(ExecutionState.Prepared, ExperimentTree.Children(limit)[1].ExecutionState, "the second never started");
    Assert.IsTrue(limit.ExecutionTime >= TimeSpan.FromSeconds(1.5));
    Assert.AreNotEqual(RunOutcome.Failed, report.Outcome, report.Error);
  }

  [TestMethod]
  public void NestedLimitLongerThanTheOuterOneIsWarnedAbout() {
    var root = new Experiment();
    var outer = new TimeLimitExperiment(TimeSpan.FromMinutes(5)) { Name = "Outer" };
    var inner = new TimeLimitExperiment(TimeSpan.FromMinutes(10)) { Name = "Inner" };
    var fine = new TimeLimitExperiment(TimeSpan.FromMinutes(2)) { Name = "Fine" };
    ExperimentTree.Add(root, outer);
    ExperimentTree.Add(outer, inner);
    ExperimentTree.Add(outer, fine);
    ExperimentTree.Add(inner, TabuSearch(5));
    ExperimentTree.Add(fine, TabuSearch(5));
    Assert.AreEqual(TimeSpan.FromMinutes(5), ExperimentTree.EnclosingTimeLimit(root, inner));
    Assert.IsNull(ExperimentTree.EnclosingTimeLimit(root, outer));
    var warnings = ExperimentTree.Warnings(root);
    Assert.AreEqual(1, warnings.Count, string.Join(" | ", warnings));
    StringAssert.Contains(warnings[0], "Inner (10:00) is inside Outer (05:00)");
    Assert.AreEqual(0, ExperimentTree.Problems(root).Count, "a warning, not a reason not to run");
  }

  [TestMethod]
  public void SavedAndOpenedLikeAnyOptimizer() {
    var root = new Experiment { Name = "With a time-limit run" };
    var limit = new TimeLimitExperiment(TimeSpan.FromSeconds(42)) { Name = "Limited" };
    ExperimentTree.Add(limit, TabuSearch(7));
    ExperimentTree.Add(limit, new BatchRun { Repetitions = 3 });
    ExperimentTree.Add(root, limit);
    var path = Path.Combine(Path.GetTempPath(), $"time-limit-{Guid.NewGuid():N}.hl");
    try {
      Documents.Save(root, path);
      var loaded = (Experiment)Documents.Load(path);
      var copy = (TimeLimitExperiment)loaded.Optimizers.Single();
      Assert.AreEqual("Limited", copy.Name);
      Assert.AreEqual(TimeSpan.FromSeconds(42), copy.MaximumExecutionTime);
      Assert.AreEqual(2, ExperimentTree.Children(copy).Count);
      Assert.AreEqual("7", ParameterEditor.Describe((IAlgorithm)ExperimentTree.Children(copy)[0]).First(p => p.Name == "MaximumIterations").Value);
    } finally {
      File.Delete(path);
    }
    var clone = (TimeLimitExperiment)ExperimentTree.Copy(limit);
    Assert.AreNotSame(ExperimentTree.Children(limit)[0], ExperimentTree.Children(clone)[0]);
    Assert.AreEqual(limit.MaximumExecutionTime, clone.MaximumExecutionTime);
  }
}
