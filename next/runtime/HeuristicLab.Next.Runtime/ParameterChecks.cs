using HeuristicLab.Core;
using HeuristicLab.Data;
using HeuristicLab.Optimization;

namespace HeuristicLab.Next.Runtime;

/// <summary>
/// Settings that probably do not work, so a front end can warn before a run fails or does nothing.
/// HeuristicLab offers every operator of a kind (every selector, every mutator of the encoding) and accepts
/// any number, also where the run then crashes or searches nothing, mostly without a message. Each rule here
/// was found by running every choice and edge value of local search, tabu search, simulated annealing,
/// the genetic algorithms (GA, Island GA), the offspring selection ones (OSGA, Island OSGA, SASEGASA) and
/// NSGA-II on HeuristicLab's problems (MoveChecks covers the move operators).
/// </summary>
public static class ParameterChecks {
  /// <summary>
  /// Why <paramref name="choice"/> as the value of <paramref name="parameter"/> of <paramref name="owner"/>
  /// probably does not work; <paramref name="root"/> is the algorithm the owner belongs to (or the owner itself).
  /// </summary>
  public static string? Warning(IParameterizedItem root, IParameterizedItem owner, IParameter parameter, IItem? choice) {
    try {
      return MoveChecks.Warning(owner, parameter, choice) ?? ChoiceWarning(root, owner, parameter, choice);
    } catch (Exception e) when (e is InvalidOperationException or NullReferenceException or ArgumentException) {
      return null;  // the items changed while being looked at (e.g. a running algorithm)
    }
  }

  /// <summary>Why the parameter's current value probably does not work (also when a needed choice has none).</summary>
  public static string? ParameterWarning(IParameterizedItem root, IParameterizedItem owner, IParameter parameter) {
    try {
      if (parameter is not IValueParameter vp) return null;
      if (MoveChecks.ParameterWarning(owner, parameter) is { } move) return move;
      // without a problem nothing is offered yet (the problem brings the operators): no warning then
      if (vp.Value == null) return root is IAlgorithm { Problem: null } ? null : MissingChoice(parameter);
      return ChoiceWarning(root, owner, parameter, vp.Value) ?? ValueWarning(root, owner, parameter, vp.Value);
    } catch (Exception e) when (e is InvalidOperationException or NullReferenceException or ArgumentException) {
      return null;
    }
  }

  /// <summary>
  /// Why the algorithm cannot run at all with its problem: HeuristicLab does not prepare an algorithm whose
  /// required operators are missing, and Start then fails with "Start not allowed in execution state Stopped".
  /// </summary>
  public static string? AlgorithmWarning(IAlgorithm algorithm) {
    if (algorithm.Problem == null) return null;
    if (KnownCrash(algorithm.GetType(), algorithm.Problem) is { } crash) return crash;
    var missing = algorithm.Parameters.Where(p => !p.Hidden && MissingChoice(p) != null).Select(p => p.Name).ToList();
    return missing.Count == 0 ? null
      : $"Cannot run: no {string.Join(", no ", missing)} for this problem. HeuristicLab will not prepare it, and Start fails.";
  }

  /// <summary>
  /// Why <paramref name="problem"/> (a new instance) probably cannot be solved by an algorithm of the
  /// algorithm's type: attaching it fails, a needed operator is missing, or HeuristicLab is known to crash.
  /// </summary>
  public static string? ProblemWarning(Type algorithmType, IProblem problem) {
    if (KnownCrash(algorithmType, problem) is { } crash) return crash;
    IAlgorithm algorithm;
    try { algorithm = (IAlgorithm)Activator.CreateInstance(algorithmType)!; } catch { return null; }
    if (!algorithm.ProblemType.IsInstanceOfType(problem)) return null;
    try {
      algorithm.Problem = problem;
    } catch (Exception e) {
      return $"HeuristicLab fails to attach it to this algorithm ({e.Message}).";
    }
    return AlgorithmWarning(algorithm);
  }

  /// <summary>Combinations that crash in HeuristicLab whatever the settings.</summary>
  private static string? KnownCrash(Type algorithmType, IProblem problem) =>
    algorithmType.Name == "NSGA2" && problem.GetType().Name == "ShapeConstrainedRegressionMultiObjectiveProblem"
      ? "NSGA-II crashes on this problem in HeuristicLab (its dominance sort indexes past the objectives), whatever the settings."
      : null;

  // ---- choices

  private static string? ChoiceWarning(IParameterizedItem root, IParameterizedItem owner, IParameter parameter, IItem? choice) {
    if (choice == null) return null;
    var problem = (root as IAlgorithm)?.Problem;
    string type = choice.GetType().Name.Split('`')[0];  // ContextAwareCrossover`1 is generic
    switch (type) {
      case "DiversitySelector" when problem != null && !HasTrees(problem):
        return "Compares symbolic expression trees, which this problem's solutions are not: the run stops with an error.";
      case "SelfAdaptiveRoundedNormalAllPositionsManipulator" when !root.GetType().Name.Contains("EvolutionStrategy"):
        return "Needs the strategy parameters only an evolution strategy provides: the run stops with an error.";
      case "PartiallyMatchedCrossover" or "MaximalPreservativeCrossover" or "MultiPermutationCrossover"
          when problem != null && PermutationLength(problem) is int length and < 4:
        return $"Needs permutations of length 4 or more; this problem's have {length}: the run stops with an error.";
      case "SymbolicDataAnalysisExpressionContextAwareCrossover" or "SymbolicDataAnalysisExpressionDeterministicBestCrossover"
          or "MultiSymbolicDataAnalysisExpressionCrossover"
          when problem?.GetType().Name == "SymbolicTimeSeriesPrognosisSingleObjectiveProblem":
        return "Fails on time series prognosis problems in HeuristicLab (null reference): the run stops with an error.";
      case "GenderSpecificSelector" or "NoSameMatesSelector" when SelectedCount(root, choice) is int count && count % 2 == 1:
        return $"Selects pairs, but {count} solutions are to be selected here (an odd number): the run stops with an error.";
      case "ExponentialDiscreteDoubleValueModifier"
          when parameter.Name == "ComparisonFactorModifier" && (Number(root, "ComparisonFactorLowerBound") <= 0 || Number(root, "ComparisonFactorUpperBound") <= 0):
        return "Needs ComparisonFactorLowerBound and ComparisonFactorUpperBound above 0: the run stops with an error.";
      case "GeneralizedRankSelector" when problem is IMultiObjectiveHeuristicOptimizationProblem:
        return "Ranks by one quality, but this problem has several objectives: the run stops with an error.";
      case "AlbaTranslocationMoveGenerator":
        return "Its inner permutation move generator does not find the VRP's tours (HeuristicLab bug): the move evaluator stops the run with an error.";
      case "CopyCrossover" when NoMutation(root):
        return "Copies a parent, and no mutation happens (no Mutator or MutationProbability 0): no new solution is ever created.";
    }
    return null;
  }

  /// <summary>A required choice (not optional) without value, because nothing is offered for it.</summary>
  private static string? MissingChoice(IParameter parameter) {
    if (parameter is not IValueParameter { Value: null } || ItemInspector.Choices(parameter) is not { } choices) return null;
    if (parameter.GetType().Name.StartsWith("Optional", StringComparison.Ordinal)) return null;
    return choices.Count == 0
      ? $"Nothing is offered for {parameter.Name} with this problem: HeuristicLab will not run the algorithm."
      : $"No {parameter.Name} is chosen: HeuristicLab will not run the algorithm.";
  }

  // ---- values

  private static string? ValueWarning(IParameterizedItem root, IParameterizedItem owner, IParameter parameter, IItem value) {
    double? number = value switch { IntValue i => i.Value, DoubleValue d => d.Value, _ => null };
    bool topLevel = ReferenceEquals(owner, root);
    if (value is BoolValue { Value: true } && parameter.Name == "FillPopulationWithParents" && topLevel
        && Number(root, "PopulationSize") is double population && population < 100)
      return "Can crash with a small population (HeuristicLab's OffspringSelector reads a parent that does not exist " +
             "once the maximum selection pressure is reached; seen with populations of 20 to 50). Use 100 or more, or switch it off.";
    if (number is not double x) return null;
    if (!topLevel) {
      if (parameter.Name == "Sigma" && x == 0) return "A step size of 0: the moves change nothing.";
      return null;
    }
    if (value is PercentValue && (x < 0 || x > 1)) return "A share must lie between 0 % and 100 %.";
    switch (parameter.Name) {
      case "PopulationSize" when x < 2:
        return x < 1 ? "No population: the run stops with an error." : "One solution: no parents to cross, nothing evolves.";
      case "Elites" when x < 0:
        return "Fewer than 0 elites: the run stops with an error.";
      case "Elites" when Number(root, "PopulationSize") is double size && x >= size:
        return "Every solution is an elite: no offspring are created (or the run stops with an error).";
      case "MaximumGenerations" or "MaximumIterations" when x < 1:
        return "No generation or iteration: only the start is evaluated.";
      case "MaximumSelectionPressure" when x <= 1:
        return "A maximum selection pressure of 1 or less ends the run after the first generation.";
      case "SuccessRatio" when x < 0 || x > 1:
        return "Must lie between 0 and 1 (above 1 it is never reached).";
      case "ComparisonFactorLowerBound" or "ComparisonFactorUpperBound" when x < 0 || x > 1:
        return "Must lie between 0 and 1.";
      case "ComparisonFactorLowerBound" or "ComparisonFactorUpperBound" when x <= 0 && ChoiceName(root, "ComparisonFactorModifier") == "ExponentialDiscreteDoubleValueModifier":
        return "The exponential ComparisonFactorModifier needs both bounds above 0: the run stops with an error.";
      case "SelectedParents" when x <= 0 || x % 2 != 0:
        return "Two parents make a child: needs an even number above 0 (0 lets the run wait forever, an odd number stops it with an error).";
      case "NumberOfIslands" or "NumberOfVillages" when x < 1:
        return "No island: the run stops with an error.";
      case "StartTemperature" or "EndTemperature" when x <= 0:
        return "Temperatures must lie above 0: the run stops with an error.";
      case "StartTemperature" when Number(root, "EndTemperature") is double end && x < end:
        return "Starts colder than it ends: the temperature rises during the run.";
      case "InnerIterations" when x < 1:
        return x < 0 ? "Less than 0: the run stops with an error." : "No inner iteration: no move is ever made.";
      case "SampleSize" when x < 1 && ((root.Parameters.TryGetValue("MoveGenerator", out var g) && g is IValueParameter { Value: IMultiMoveGenerator }) || !root.Parameters.ContainsKey("MoveGenerator")):
        return x < 0 ? "Less than 0: the run stops with an error." : "No moves are sampled: the search stands still.";
      case "TabuTenure" when x < 0:
        return "A tabu tenure below 0: the run stops with an error.";
      case "MutationProbability" when x == 0 && ChoiceName(root, "Crossover") == "CopyCrossover":
        return "No mutation, and the crossover (CopyCrossover) only copies: no new solution is ever created.";
    }
    return null;
  }

  // ---- helpers

  private static double? Number(IParameterizedItem item, string name) =>
    item.Parameters.TryGetValue(name, out var p) && p is IValueParameter vp
      ? vp.Value switch { IntValue i => i.Value, DoubleValue d => d.Value, _ => null } : null;

  private static string? ChoiceName(IParameterizedItem item, string name) =>
    item.Parameters.TryGetValue(name, out var p) && p is IValueParameter { Value: { } v } ? v.GetType().Name : null;

  private static bool NoMutation(IParameterizedItem root) =>
    root.Parameters.ContainsKey("Mutator") && (ChoiceName(root, "Mutator") == null || Number(root, "MutationProbability") == 0);

  /// <summary>How many solutions a selector selects: its own fixed number, else the value it looks up in the algorithm.</summary>
  private static int? SelectedCount(IParameterizedItem root, IItem selector) {
    if (selector is not IParameterizedItem s || !s.Parameters.TryGetValue("NumberOfSelectedSubScopes", out var p)) return null;
    if (p is IValueLookupParameter { Value: IntValue fixedCount }) return fixedCount.Value;
    if (p is ILookupParameter lookup && Number(root, lookup.ActualName) is double looked) return (int)looked;
    return null;
  }

  /// <summary>Whether the problem's solutions are (or are decoded into) symbolic expression trees.</summary>
  private static bool HasTrees(IProblem problem) {
    static bool IsTree(Type t) => t.Name == "ISymbolicExpressionTree" || t.GetInterfaces().Any(i => i.Name == "ISymbolicExpressionTree");
    var items = problem.Operators.OfType<IParameterizedItem>().ToList();
    if (problem is IHeuristicOptimizationProblem { SolutionCreator: IParameterizedItem creator }) items.Add(creator);
    return items.Any(op => op.Parameters.Any(p => IsTree(p.DataType)));
  }

  /// <summary>The length of a permutation problem's solutions, if its solution creator says.</summary>
  private static int? PermutationLength(IProblem problem) {
    if (problem is not IHeuristicOptimizationProblem { SolutionCreator: IParameterizedItem creator }
        || !creator.GetType().Name.Contains("Permutation") || !creator.Parameters.TryGetValue("Length", out var p)) return null;
    if (p is IValueLookupParameter { Value: IntValue own }) return own.Value;
    if (p is ILookupParameter lookup && problem is IParameterizedItem pi && Number(pi, lookup.ActualName) is double looked) return (int)looked;
    return null;
  }
}
