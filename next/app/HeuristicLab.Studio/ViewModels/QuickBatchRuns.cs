using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HeuristicLab.Core;
using HeuristicLab.Data;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;

namespace HeuristicLab.Studio.ViewModels;

/// <summary>One quick batch run ("QBR k") of one algorithm: the runs one Start on the algorithm made.</summary>
public sealed class QuickBatch(QbrAlgorithmGroup group, int number) {
  public QbrAlgorithmGroup Group { get; } = group;
  public int Number { get; } = number;
  public string Name => $"QBR {Number}";
  public IReadOnlyList<IRun> Runs => Group.Runs.Where(r => QuickBatchRuns.NumberOf(r) == Number).ToList();
}

/// <summary>The quick batch runs of one algorithm (by the algorithm's name, which every run records).</summary>
public sealed class QbrAlgorithmGroup(QuickBatchRuns owner, string algorithm) {
  private readonly Dictionary<int, QuickBatch> batches = [];

  public string Algorithm { get; } = algorithm;
  public QuickBatchRuns Owner { get; } = owner;
  public IReadOnlyList<IRun> Runs => Owner.Runs.Where(r => QuickBatchRuns.AlgorithmOf(r) == Algorithm).ToList();

  /// <summary>Its quick batch runs in order of their numbers; the same objects every time (kept ticked/selected over a refresh).</summary>
  public IReadOnlyList<QuickBatch> Batches =>
    Runs.Select(QuickBatchRuns.NumberOf).Distinct().Order().Select(k => batches.TryGetValue(k, out var b) ? b : batches[k] = new QuickBatch(this, k)).ToList();
}

/// <summary>
/// Quick batch runs ("QBRs"): Start on one algorithm (its detail's Start) runs it <see cref="DefaultSize"/> times (the
/// Options tab's size), and those runs are kept apart from the runs of the experiment (user's decisions 2026-10-15 and
/// 2026-11-10; they replaced single runs): HeuristicLab would add them to the algorithm's batch run, time-limit run and
/// experiment, mixing trials into the experiment that was really made. They are listed under "Quick batch runs" >
/// experiment > algorithm > "QBR k" in the Runs and Results tabs, where they can be looked at or ticked to compare. The
/// runs are named "QBR k, run j". HeuristicLab's .hl format has no place for them, so they are saved next to the
/// experiment's file as a run collection ("name.quick-batch-runs.hl", which HeuristicLab 3.3 opens too); an older
/// "name.single-runs.hl" is read when there is no such file, each single run becoming a QBR of one run.
/// </summary>
public sealed class QuickBatchRuns {
  public const int DefaultSize = 10, MaxSize = 1000;
  private static readonly Regex Numbered = new(@"^QBR (\d+)\b", RegexOptions.Compiled);
  private static readonly Regex OldSingle = new(@"^Single run (\d+)$", RegexOptions.Compiled);
  private readonly Dictionary<string, QbrAlgorithmGroup> groups = new(StringComparer.Ordinal);

  public RunCollection Runs { get; } = new();

  public int Count => Runs.Count;

  /// <summary>One group per algorithm (by name, in the order of their first run); the same objects every time.</summary>
  public IReadOnlyList<QbrAlgorithmGroup> Groups =>
    Runs.Select(AlgorithmOf).Distinct().Select(name => groups.TryGetValue(name, out var g) ? g : groups[name] = new QbrAlgorithmGroup(this, name)).ToList();

  public static string AlgorithmOf(IRun run) =>
    run.Parameters.TryGetValue("Algorithm Name", out var name) && name is StringValue s ? s.Value : "Algorithm";

  /// <summary>The number k of the quick batch run a run belongs to (its name "QBR k, run j"; 0 if it has none).</summary>
  public static int NumberOf(IRun run) => Numbered.Match(run.Name) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

  /// <summary>Takes the runs of one Start over as the algorithm's next quick batch run ("QBR k, run j"); returns k.</summary>
  public int Add(IReadOnlyList<IRun> runs) {
    if (runs.Count == 0) return 0;
    var algorithm = AlgorithmOf(runs[0]);
    int k = Runs.Where(r => AlgorithmOf(r) == algorithm).Select(NumberOf).DefaultIfEmpty(0).Max() + 1;
    for (int j = 0; j < runs.Count; j++) {
      runs[j].Name = $"QBR {k}, run {j + 1}";
      Runs.Add(runs[j]);
    }
    return k;
  }

  /// <summary>The file next to an experiment's .hl file that holds its quick batch runs.</summary>
  public static string FileFor(string experimentFile) => Beside(experimentFile, ".quick-batch-runs.hl");

  /// <summary>Where single runs (before quick batch runs) were saved.</summary>
  public static string OldFileFor(string experimentFile) => Beside(experimentFile, ".single-runs.hl");

  private static string Beside(string experimentFile, string ending) =>
    Path.Combine(Path.GetDirectoryName(experimentFile) ?? "", Path.GetFileNameWithoutExtension(experimentFile) + ending);

  /// <summary>Reads the quick batch runs saved next to this experiment file (or its older single runs), if there are any.</summary>
  public void Load(string experimentFile) {
    var file = FileFor(experimentFile);
    bool old = !File.Exists(file);
    if (old) file = OldFileFor(experimentFile);
    if (!File.Exists(file) || Documents.Load(file) is not RunCollection saved) return;
    Runs.Clear();
    foreach (var run in saved) {
      // a single run k becomes QBR k of one run
      if (old && OldSingle.Match(run.Name) is { Success: true } m) run.Name = $"QBR {m.Groups[1].Value}, run 1";
      Runs.Add(run);
    }
  }

  /// <summary>Writes them next to the experiment file (also when there are none any more, so an old file does not bring them back).</summary>
  public void Save(string experimentFile) {
    var file = FileFor(experimentFile);
    if (Runs.Count == 0 && !File.Exists(file) && !File.Exists(OldFileFor(experimentFile))) return;
    Documents.Save(Runs, file);
  }

  /// <summary>Follows a renamed experiment file.</summary>
  public static void Move(string from, string to) {
    foreach (var name in new Func<string, string>[] { FileFor, OldFileFor })
      if (File.Exists(name(from)) && !File.Exists(name(to))) File.Move(name(from), name(to));
  }
}
