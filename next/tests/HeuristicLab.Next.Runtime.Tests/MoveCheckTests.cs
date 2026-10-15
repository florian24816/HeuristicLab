using HeuristicLab.Core;
using HeuristicLab.Data;
using HeuristicLab.Optimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Next.Runtime.Tests;

/// <summary>MoveChecks warns exactly about the move generators with which a local search does nothing.</summary>
[TestClass]
public class MoveCheckTests {
  private static IConstrainedValueParameter<IMoveGenerator> Generators(IAlgorithm a) => (IConstrainedValueParameter<IMoveGenerator>)a.Parameters["MoveGenerator"];

  [TestMethod]
  [Timeout(300_000)]
  public async Task WarnedGeneratorsAreThoseThatEvaluateNoMoves() {
    var ls = Setups.Create(new SetupRequest("LocalSearch") { Problem = "TravelingSalesmanProblem", Instance = "ch130" }).Algorithm;
    ParameterEditor.Apply(ls, ["MaximumIterations=3", "SampleSize=10"]);
    var warned = new List<string>();
    foreach (var generator in Generators(ls).ValidValues.ToList()) {
      var copy = (IAlgorithm)ls.Clone();
      var parameter = Generators(copy);
      var chosen = parameter.ValidValues.Single(g => g.GetType() == generator.GetType());
      string? warning = MoveChecks.Warning(copy, parameter, chosen);
      parameter.Value = chosen;
      var report = await OptimizerRunner.RunAsync(copy, new RunOptions { Seed = 1 });
      var moves = (IntValue)copy.Results["EvaluatedMoves"].Value;
      Assert.AreEqual(moves.Value == 0, warning != null, $"{generator.Name}: {moves.Value} moves, warning {warning}");
      if (warning == null) continue;
      warned.Add(generator.Name);
      // after the choice: no move evaluator is left, and that is said too
      Assert.IsNull(((IValueParameter)copy.Parameters["MoveEvaluator"]).Value);
      StringAssert.Contains(MoveChecks.ParameterWarning(copy, copy.Parameters["MoveEvaluator"]), "No MoveEvaluator");
      Assert.IsNull(MoveChecks.ParameterWarning(copy, copy.Parameters["MoveMaker"]), "the move maker fits");
    }
    CollectionAssert.IsSubsetOf(new[] { "StochasticSwap2SingleMoveGenerator", "StochasticScrambleMultiMoveGenerator" }, warned);
    StringAssert.Contains(MoveChecks.Warning(ls, Generators(ls), Generators(ls).ValidValues.First(g => g.Name == "ExhaustiveSwap2MoveGenerator")),
      "Permutation Swap2 moves");
  }

  [TestMethod]
  public void TheGenericMoveGeneratorNeedsTheProblemsNeighbors() {
    var ls = Setups.Create(new SetupRequest("LocalSearch") { Problem = "OneMaxProblem" }).Algorithm;
    var generic = Generators(ls).ValidValues.OfType<SingleObjectiveMoveGenerator>().Single();
    StringAssert.Contains(MoveChecks.Warning(ls, Generators(ls), generic), "GetNeighbors");
  }
}
