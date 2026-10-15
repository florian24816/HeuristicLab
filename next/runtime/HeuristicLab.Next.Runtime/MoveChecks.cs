using HeuristicLab.Core;
using HeuristicLab.Optimization;

namespace HeuristicLab.Next.Runtime;

/// <summary>
/// Move operators that cannot work. HeuristicLab offers every move generator of a problem's encoding,
/// also those for which the problem has no fitting move evaluator or move maker (the TSP has none for
/// swap2 and scramble moves): local search, tabu search and simulated annealing then "complete"
/// without evaluating a single move, with no result and no message. The generic move generator of
/// encoding-based problems needs the problem's own neighbors (GetNeighbors), which the built-in ones
/// do not define: the search stops with an error. These checks find both, so a front end can warn.
/// </summary>
public static class MoveChecks {
  /// <summary>
  /// Why <paramref name="choice"/>, as the value of <paramref name="parameter"/> of
  /// <paramref name="owner"/> (an algorithm or improvement operator with a MoveGenerator), probably
  /// does not work; null if nothing speaks against it.
  /// </summary>
  public static string? Warning(IParameterizedItem owner, IParameter parameter, IItem? choice) {
    if (choice is IMoveGenerator generator && IsMoveParameter(parameter)) return GeneratorWarning(owner, parameter, generator);
    if (choice != null && IsMoveParameter(parameter) && Generator(owner) is { } current && !Fits(current, choice))
      return $"Does not fit the moves of the move generator {Name(current)} ({MoveKind(current)}).";
    return null;
  }

  /// <summary>Why the parameter's current value probably does not work, also when it has none (no operator fits).</summary>
  public static string? ParameterWarning(IParameterizedItem owner, IParameter parameter) {
    if (!IsMoveParameter(parameter) || parameter is not IValueParameter value) return null;
    if (value.Value != null) return Warning(owner, parameter, value.Value);
    if (parameter.Name == "MoveGenerator" || Generator(owner) is not { } generator || Problem(owner) == null) return null;
    return $"No {parameter.Name} fits the moves of {Name(generator)} ({MoveKind(generator)}) in this problem: {Consequence(parameter.Name)}";
  }

  private static string? GeneratorWarning(IParameterizedItem owner, IParameter parameter, IMoveGenerator generator) {
    if (Problem(owner) is not { } problem) return null;
    if (generator is SingleObjectiveMoveGenerator && !DefinesNeighbors(problem))
      return "The problem defines no neighbors (its GetNeighbors is not implemented): the search stops with an error.";
    var missing = owner.Parameters
      .Where(p => p != parameter && IsMoveParameter(p) && !typeof(IMoveGenerator).IsAssignableFrom(p.DataType))
      .Where(p => !problem.Operators.Any(op => p.DataType.IsInstanceOfType(op) && Fits(generator, (IItem)op)))
      .Select(p => p.Name).ToList();
    if (missing.Count == 0) return null;
    return $"The problem has no {string.Join(" and no ", missing)} for these moves ({MoveKind(generator)}): {Consequence(missing[0])}";
  }

  private static string Consequence(string parameter) => parameter switch {
    "MoveEvaluator" => "no move is evaluated, the run ends without results.",
    "MoveMaker" => "no move is ever made.",
    _ => "the search cannot run as intended."
  };

  /// <summary>A selection among move operators (MoveGenerator, MoveEvaluator, MoveMaker, TabuChecker, TabuMaker).</summary>
  private static bool IsMoveParameter(IParameter parameter) =>
    MoveParameterTypes.Any(t => t.IsAssignableFrom(parameter.DataType)) && ItemInspector.Choices(parameter) != null;

  private static readonly Type[] MoveParameterTypes =
    [typeof(IMoveGenerator), typeof(ISingleObjectiveMoveEvaluator), typeof(IMoveMaker), typeof(ITabuChecker), typeof(ITabuMaker)];

  private static IMoveGenerator? Generator(IParameterizedItem owner) =>
    owner.Parameters.TryGetValue("MoveGenerator", out var p) && p is IValueParameter v ? v.Value as IMoveGenerator : null;

  /// <summary>The problem of an algorithm or of an improvement operator (both have a Problem property).</summary>
  private static IHeuristicOptimizationProblem? Problem(IParameterizedItem owner) =>
    owner is IAlgorithm algorithm ? algorithm.Problem as IHeuristicOptimizationProblem
    : owner.GetType().GetProperty("Problem")?.GetValue(owner) as IHeuristicOptimizationProblem;

  /// <summary>
  /// The kinds of moves a generator makes: its most specific move operator interfaces (as HeuristicLab's
  /// local search matches evaluators and makers to it), without the generator interfaces.
  /// </summary>
  private static List<Type> MoveTypes(IMoveGenerator generator) {
    var types = generator.GetType().GetInterfaces()
      .Where(t => typeof(IMoveOperator).IsAssignableFrom(t) && !typeof(IMoveGenerator).IsAssignableFrom(t)).ToList();
    return types.Where(t => !types.Any(u => u != t && t.IsAssignableFrom(u))).ToList();
  }

  private static bool Fits(IMoveGenerator generator, IItem op) => MoveTypes(generator).Any(t => t.IsInstanceOfType(op));

  /// <summary>"Permutation Swap2" for IPermutationSwap2MoveOperator.</summary>
  private static string MoveKind(IMoveGenerator generator) {
    var names = MoveTypes(generator).Select(t => {
      string name = t.Name.StartsWith('I') ? t.Name[1..] : t.Name;
      foreach (var end in new[] { "MoveOperator", "Operator", "Move" })
        if (name.EndsWith(end, StringComparison.Ordinal)) { name = name[..^end.Length]; break; }
      return System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ") + " moves";
    }).ToList();
    return names.Count == 0 ? "its moves" : string.Join(", ", names);
  }

  private static string Name(IItem item) => item is INamedItem n ? n.Name : item.ItemName;

  /// <summary>Whether an encoding-based problem overrides GetNeighbors (the default returns none).</summary>
  private static bool DefinesNeighbors(IHeuristicOptimizationProblem problem) {
    var method = problem.GetType().GetMethods().FirstOrDefault(m => m.Name == "GetNeighbors" && m.GetParameters().Length == 2);
    if (method == null) return true;
    var declaring = method.DeclaringType!;
    return !(declaring.IsGenericType && declaring.GetGenericTypeDefinition() == typeof(SingleObjectiveBasicProblem<>));
  }
}
