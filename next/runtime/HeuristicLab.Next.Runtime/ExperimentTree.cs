using HeuristicLab.Optimization;

namespace HeuristicLab.Next.Runtime;

/// <summary>
/// Building blocks of an experiment as a tree over HeuristicLab's own optimizers, so that an
/// experiment built here is a plain HeuristicLab Experiment (.hl) and runs with OptimizerRunner.
/// <list type="bullet">
/// <item>Experiment: any number of children (algorithms, batch runs, time-limit runs, experiments).</item>
/// <item>Batch run: repeats its content Repetitions times. HeuristicLab's BatchRun repeats one
/// optimizer, so several children live in an inner Experiment; each run repeats all of them.</item>
/// <item>Time-limit run: any building blocks, run one after another and stopped once they ran
/// MaximumExecutionTime in total (TimeLimitExperiment). HeuristicLab's own TimeLimitRun (from
/// older files) holds exactly one algorithm.</item>
/// </list>
/// </summary>
public static class ExperimentTree {
  /// <summary>Name of the inner experiment holding a batch run's children.</summary>
  public const string BatchContentName = "Batch content";

  public static bool IsContainer(IOptimizer optimizer) => optimizer is Experiment or BatchRun or TimeLimitRun or TimeLimitExperiment;

  /// <summary>
  /// Containers that can be added as building blocks: HeuristicLab's experiment and batch run, and
  /// the time-limit run that holds several blocks (TimeLimitExperiment).
  /// </summary>
  public static IReadOnlyList<CatalogEntry> ContainerTypes() =>
    Catalog.MetaOptimizers().Where(e => e.Type == typeof(BatchRun) || e.Type == typeof(Experiment))
      .Append(new CatalogEntry("Time-limit run", "Runs its building blocks one after another and stops them when the time limit is reached.",
        typeof(TimeLimitExperiment).FullName!, typeof(TimeLimitExperiment)))
      .ToList();

  public static bool IsTimeLimit(IOptimizer optimizer) => optimizer is TimeLimitRun or TimeLimitExperiment;

  /// <summary>The time limit of a time-limit run (either kind); null for other optimizers.</summary>
  public static TimeSpan? TimeLimit(IOptimizer optimizer) => optimizer switch {
    TimeLimitRun t => t.MaximumExecutionTime,
    TimeLimitExperiment t => t.MaximumExecutionTime,
    _ => null
  };

  public static void SetTimeLimit(IOptimizer optimizer, TimeSpan limit) {
    switch (optimizer) {
      case TimeLimitRun t: t.MaximumExecutionTime = limit; break;
      case TimeLimitExperiment t: t.MaximumExecutionTime = limit; break;
    }
  }

  /// <summary>
  /// The shortest time limit of the time-limit runs around a node (null if none): a time-limit run
  /// inside another one with a shorter limit is stopped by the outer one first.
  /// </summary>
  public static TimeSpan? EnclosingTimeLimit(IOptimizer root, IOptimizer node) {
    TimeSpan? Search(IOptimizer current, TimeSpan? limit) {
      if (current == node) return limit;
      if (!IsContainer(current)) return null;
      var here = TimeLimit(current) is { } own && (limit == null || own < limit) ? own : limit;
      foreach (var child in Children(current))
        if (Contains(child, node)) return Search(child, here);
      return null;
    }
    return Search(root, null);
  }

  /// <summary>Settings that run but make little sense, e.g. a time-limit run longer than one around it.</summary>
  public static IReadOnlyList<string> Warnings(IOptimizer root) {
    var warnings = new List<string>();
    void Visit(IOptimizer node, TimeSpan? outer, string? outerName) {
      if (TimeLimit(node) is { } own) {
        if (outer is { } o && own > o)
          warnings.Add($"{node.Name} ({Format(own)}) is inside {outerName} ({Format(o)}): it is stopped after {Format(o)} anyway.");
        if (outer == null || own < outer) { outer = own; outerName = node.Name; }
      }
      if (IsContainer(node)) foreach (var child in Children(node)) Visit(child, outer, outerName);
    }
    Visit(root, null, null);
    return warnings;
  }

  private static string Format(TimeSpan t) => t.ToString(t.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);

  public static IReadOnlyList<IOptimizer> Children(IOptimizer container) => container switch {
    Experiment e => e.Optimizers.ToList(),
    BatchRun { Optimizer: Experiment inner } when inner.Name == BatchContentName => inner.Optimizers.ToList(),
    BatchRun { Optimizer: IOptimizer single } => [single],
    TimeLimitRun { Algorithm: IAlgorithm algorithm } => [algorithm],
    TimeLimitExperiment t => t.Content.Optimizers.ToList(),
    _ => []
  };

  /// <summary>Whether a child of this type can be added (a time-limit run takes one algorithm only).</summary>
  public static bool CanAdd(IOptimizer container, Type childType) => container switch {
    Experiment or BatchRun or TimeLimitExperiment => typeof(IOptimizer).IsAssignableFrom(childType),
    TimeLimitRun t => t.Algorithm == null && typeof(IAlgorithm).IsAssignableFrom(childType),
    _ => false
  };

  public static void Add(IOptimizer container, IOptimizer child) => Insert(container, Children(container).Count, child);

  /// <summary>
  /// Puts a child at a position among the container's children (0 = first). HeuristicLab's
  /// experiments run their optimizers in this order, and prepare an optimizer that is added.
  /// </summary>
  public static void Insert(IOptimizer container, int index, IOptimizer child) {
    if (!CanAdd(container, child.GetType()))
      throw new ArgumentException($"{container.Name} cannot contain {child.Name}.");
    switch (container) {
      case Experiment e:
        e.Optimizers.Insert(Math.Clamp(index, 0, e.Optimizers.Count), child);
        break;
      case BatchRun b:
        if (b.Optimizer is not Experiment { Name: BatchContentName } inner) {
          inner = new Experiment { Name = BatchContentName };
          var existing = b.Optimizer;
          b.Optimizer = null;
          if (existing != null) inner.Optimizers.Add(existing);
          b.Optimizer = inner;
        }
        inner.Optimizers.Insert(Math.Clamp(index, 0, inner.Optimizers.Count), child);
        break;
      case TimeLimitRun t:
        t.Algorithm = (IAlgorithm)child;
        break;
      case TimeLimitExperiment t:
        t.Content.Optimizers.Insert(Math.Clamp(index, 0, t.Content.Optimizers.Count), child);
        break;
    }
  }

  public static void Remove(IOptimizer container, IOptimizer child) {
    switch (container) {
      case Experiment e: e.Optimizers.Remove(child); break;
      case BatchRun { Optimizer: Experiment { Name: BatchContentName } inner }: inner.Optimizers.Remove(child); break;
      case BatchRun b when b.Optimizer == child: b.Optimizer = null; break;
      case TimeLimitRun t when t.Algorithm == child: t.Algorithm = null; break;
      case TimeLimitExperiment t: t.Content.Optimizers.Remove(child); break;
    }
  }

  /// <summary>Whether a container is this optimizer or lies inside it (a block cannot be moved into itself).</summary>
  public static bool Contains(IOptimizer root, IOptimizer node) =>
    root == node || (IsContainer(root) && Children(root).Any(c => Contains(c, node)));

  /// <summary>
  /// An independent copy of a building block with everything inside it (an algorithm with its
  /// problem and parameters, a batch run or time-limit run with its content), without results
  /// and runs: a copy starts unrun.
  /// </summary>
  public static IOptimizer Copy(IOptimizer optimizer) {
    var copy = (IOptimizer)optimizer.Clone();
    ClearRuns(copy);
    return copy;
  }

  private static void ClearRuns(IOptimizer optimizer) {
    foreach (var child in IsContainer(optimizer) ? Children(optimizer) : []) ClearRuns(child);
    try {
      optimizer.Prepare(clearRuns: true);
    } catch (Exception e) when (e is InvalidOperationException or NullReferenceException) {
      optimizer.Runs.Clear();  // e.g. an algorithm without problem
    }
  }

  public static IEnumerable<IAlgorithm> Algorithms(IOptimizer root) =>
    IsContainer(root) ? Children(root).SelectMany(Algorithms) : root is IAlgorithm a ? [a] : [];

  /// <summary>Why the tree cannot run yet; empty when it is ready to start.</summary>
  public static IReadOnlyList<string> Problems(IOptimizer root) {
    var problems = new List<string>();
    void Visit(IOptimizer node) {
      if (node is IAlgorithm algorithm) {
        if (algorithm.Problem == null) problems.Add($"{algorithm.Name} has no problem.");
      } else if (IsContainer(node)) {
        var children = Children(node);
        if (children.Count == 0) problems.Add($"{node.Name} is empty.");
        foreach (var child in children) Visit(child);
      }
    }
    Visit(root);
    return problems;
  }
}
