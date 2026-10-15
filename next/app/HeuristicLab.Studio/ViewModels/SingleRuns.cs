using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeuristicLab.Core;
using HeuristicLab.Data;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;

namespace HeuristicLab.Studio.ViewModels;

/// <summary>The single runs of one algorithm (by the algorithm's name, which every run records).</summary>
public sealed class SingleRunGroup(SingleRuns owner, string algorithm) {
  public string Algorithm { get; } = algorithm;
  public SingleRuns Owner { get; } = owner;
  public IReadOnlyList<IRun> Runs => Owner.Runs.Where(r => SingleRuns.AlgorithmOf(r) == Algorithm).ToList();
}

/// <summary>
/// Runs made by Start on one algorithm (its detail's Start), kept apart from the runs of the experiment
/// (user's decision 2026-10-15): HeuristicLab would add them to the algorithm's batch run, time-limit run
/// and experiment, mixing trials into the experiment that was really made. They are listed under "Single
/// runs" in the Runs and Results tabs, where they can be looked at or ticked to compare. HeuristicLab's
/// .hl format has no place for them, so they are saved next to the experiment's file as a run collection
/// ("name.single-runs.hl", which HeuristicLab 3.3 opens too).
/// </summary>
public sealed class SingleRuns {
  private readonly Dictionary<string, SingleRunGroup> groups = new(StringComparer.Ordinal);

  public RunCollection Runs { get; } = new();

  public int Count => Runs.Count;

  /// <summary>One group per algorithm (by name, in the order of their first run); the same objects every time.</summary>
  public IReadOnlyList<SingleRunGroup> Groups =>
    Runs.Select(AlgorithmOf).Distinct().Select(name => groups.TryGetValue(name, out var g) ? g : groups[name] = new SingleRunGroup(this, name)).ToList();

  public static string AlgorithmOf(IRun run) =>
    run.Parameters.TryGetValue("Algorithm Name", out var name) && name is StringValue s ? s.Value : "Algorithm";

  /// <summary>Takes runs over, named "Single run k" (k counting this algorithm's single runs).</summary>
  public void Add(IEnumerable<IRun> runs) {
    foreach (var run in runs) {
      int k = Runs.Count(r => AlgorithmOf(r) == AlgorithmOf(run)) + 1;
      run.Name = $"Single run {k}";
      Runs.Add(run);
    }
  }

  /// <summary>The file next to an experiment's .hl file that holds its single runs.</summary>
  public static string FileFor(string experimentFile) =>
    Path.Combine(Path.GetDirectoryName(experimentFile) ?? "", Path.GetFileNameWithoutExtension(experimentFile) + ".single-runs.hl");

  /// <summary>Reads the single runs saved next to this experiment file, if there are any.</summary>
  public void Load(string experimentFile) {
    var file = FileFor(experimentFile);
    if (!File.Exists(file) || Documents.Load(file) is not RunCollection saved) return;
    Runs.Clear();
    Runs.AddRange(saved);
  }

  /// <summary>Writes them next to the experiment file (also when there are none any more, so an old file does not bring them back).</summary>
  public void Save(string experimentFile) {
    var file = FileFor(experimentFile);
    if (Runs.Count == 0 && !File.Exists(file)) return;
    Documents.Save(Runs, file);
  }

  /// <summary>Follows a renamed experiment file.</summary>
  public static void Move(string from, string to) {
    if (File.Exists(FileFor(from)) && !File.Exists(FileFor(to))) File.Move(FileFor(from), FileFor(to));
  }
}
