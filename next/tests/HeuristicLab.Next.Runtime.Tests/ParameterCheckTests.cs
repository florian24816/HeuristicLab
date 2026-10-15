using HeuristicLab.Core;
using HeuristicLab.Optimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Next.Runtime.Tests;

/// <summary>ParameterChecks warns about the choices and values that were found to crash or to search nothing.</summary>
[TestClass]
public class ParameterCheckTests {
  private static IAlgorithm Create(string algorithm, string problem, string? instance = null, params string[] settings) =>
    Setups.Create(new SetupRequest(algorithm) { Problem = problem, Instance = instance, Settings = settings }).Algorithm;

  private static string? Choice(IAlgorithm a, string parameter, string choice) {
    var p = a.Parameters[parameter];
    return ParameterChecks.Warning(a, a, p, ItemInspector.Choices(p)!.First(c => c.GetType().Name.Split('`')[0] == choice));
  }

  private static string? Value(IAlgorithm a, string assignment) {
    ParameterEditor.Apply(a, [assignment]);
    return ParameterChecks.ParameterWarning(a, a, a.Parameters[assignment.Split('=')[0]]);
  }

  [TestMethod]
  public void GeneticAlgorithmChoices() {
    var tsp = Create("GeneticAlgorithm", "TravelingSalesmanProblem", "ch130");
    StringAssert.Contains(Choice(tsp, "Selector", "DiversitySelector"), "expression trees");
    Assert.IsNull(Choice(tsp, "Selector", "TournamentSelector"));
    Assert.IsNull(Choice(Create("GeneticAlgorithm", "SymbolicRegressionSingleObjectiveProblem"), "Selector", "DiversitySelector"), "trees: fine");
    StringAssert.Contains(Choice(Create("GeneticAlgorithm", "LinearAssignmentProblem"), "Crossover", "PartiallyMatchedCrossover"), "length 4");
    Assert.IsNull(Choice(tsp, "Crossover", "PartiallyMatchedCrossover"));
    StringAssert.Contains(Choice(Create("GeneticAlgorithm", "GESymbolicRegressionSingleObjectiveProblem"), "Mutator", "SelfAdaptiveRoundedNormalAllPositionsManipulator"), "strategy");
    StringAssert.Contains(Choice(Create("GeneticAlgorithm", "SymbolicTimeSeriesPrognosisSingleObjectiveProblem"), "Crossover", "SymbolicDataAnalysisExpressionContextAwareCrossover"), "time series");
    // a real-vector GA crosses by copying: without mutation nothing new appears
    var rastrigin = Create("GeneticAlgorithm", "SingleObjectiveTestFunctionProblem");
    Assert.IsNull(Choice(rastrigin, "Crossover", "CopyCrossover"));
    StringAssert.Contains(Value(rastrigin, "MutationProbability=0"), "CopyCrossover");
  }

  [TestMethod]
  public void GeneticAlgorithmValues() {
    var ga = Create("GeneticAlgorithm", "TravelingSalesmanProblem", "ch130");
    Assert.IsNull(Value(ga, "PopulationSize=100"));
    StringAssert.Contains(Value(ga, "PopulationSize=1"), "nothing evolves");
    StringAssert.Contains(Value(ga, "PopulationSize=0"), "error");
    ParameterEditor.Apply(ga, ["PopulationSize=10"]);
    StringAssert.Contains(Value(ga, "Elites=10"), "elite");
    StringAssert.Contains(Value(ga, "Elites=-1"), "error");
    Assert.IsNull(Value(ga, "Elites=1"));
    StringAssert.Contains(Value(ga, "MutationProbability=2"), "100 %");
    StringAssert.Contains(Value(ga, "MaximumGenerations=0"), "only the start");
  }

  [TestMethod]
  public void OffspringSelectionAndIslands() {
    var osga = Create("OffspringSelectionGeneticAlgorithm", "TravelingSalesmanProblem", "ch130");
    // HeuristicLab's default lower bound 0 does not suit the exponential modifiers
    StringAssert.Contains(Choice(osga, "ComparisonFactorModifier", "ExponentialDiscreteDoubleValueModifier"), "above 0");
    Assert.IsNull(Choice(osga, "ComparisonFactorModifier", "LinearDiscreteDoubleValueModifier"));
    StringAssert.Contains(Value(osga, "SelectedParents=3"), "even");
    StringAssert.Contains(Value(osga, "SelectedParents=0"), "forever");
    StringAssert.Contains(Value(osga, "MaximumSelectionPressure=1"), "first generation");
    ParameterEditor.Apply(osga, ["PopulationSize=20"]);
    StringAssert.Contains(Value(osga, "FillPopulationWithParents=true"), "OffspringSelector");
    ParameterEditor.Apply(osga, ["PopulationSize=100"]);
    Assert.IsNull(ParameterChecks.ParameterWarning(osga, osga, osga.Parameters["FillPopulationWithParents"]));
    // 15 % of 100 = 15 emigrants: pair selectors cannot pick an odd number
    var island = Create("IslandGeneticAlgorithm", "TravelingSalesmanProblem", "ch130");
    StringAssert.Contains(Choice(island, "EmigrantsSelector", "NoSameMatesSelector"), "odd");
    StringAssert.Contains(Value(island, "NumberOfIslands=0"), "island");
    StringAssert.Contains(Choice(island, "EmigrantsSelector", "GenderSpecificSelector"), "odd");
    var nsga = Create("NSGA2", "MultiObjectiveTestFunctionProblem");
    StringAssert.Contains(Choice(nsga, "Selector", "GeneralizedRankSelector"), "several objectives");
    Assert.IsNull(Choice(nsga, "Selector", "RandomSelector"));
  }

  [TestMethod]
  public void SimulatedAnnealingAndTabuSearch() {
    var sa = Create("SimulatedAnnealing", "TravelingSalesmanProblem", "ch130");
    StringAssert.Contains(Value(sa, "StartTemperature=0"), "above 0");
    StringAssert.Contains(Value(sa, "InnerIterations=0"), "no move");
    var vrp = Create("SimulatedAnnealing", "VehicleRoutingProblem", "A-n32-k5");
    StringAssert.Contains(Choice(vrp, "MoveGenerator", "AlbaTranslocationMoveGenerator"), "VRP's tours");
    Assert.IsNull(Choice(vrp, "MoveGenerator", "AlbaStochasticLambdaInterchangeMultiMoveGenerator"));
    var ts = Create("TabuSearch", "TravelingSalesmanProblem", "ch130");
    StringAssert.Contains(Value(ts, "TabuTenure=-1"), "error");
  }

  [TestMethod]
  public void ProblemsAnAlgorithmCannotRun() {
    IProblem New(string problem) => (IProblem)Catalog.Find(Catalog.Problems(), problem)!.CreateInstance();
    Type Algorithm(string name) => Catalog.Find(Catalog.Algorithms(), name)!.Type;
    StringAssert.Contains(ParameterChecks.ProblemWarning(Algorithm("TabuSearch"), New("GraphColoringProblem")), "attach");
    StringAssert.Contains(ParameterChecks.ProblemWarning(Algorithm("TabuSearch"), New("OneMaxProblem")), "MoveEvaluator");
    StringAssert.Contains(ParameterChecks.ProblemWarning(Algorithm("SimulatedAnnealing"), New("JobShopSchedulingProblem")), "MoveGenerator");
    Assert.IsNull(ParameterChecks.ProblemWarning(Algorithm("SimulatedAnnealing"), New("TravelingSalesmanProblem")));
    Assert.IsNull(ParameterChecks.ProblemWarning(Algorithm("GeneticAlgorithm"), New("JobShopSchedulingProblem")));
    StringAssert.Contains(ParameterChecks.ProblemWarning(Algorithm("NSGA2"), New("ShapeConstrainedRegressionMultiObjectiveProblem")), "NSGA-II");
  }
}
