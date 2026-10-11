using HeuristicLab.Core;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;

// Builds the course's HeuristicLab experiments (HEU5, exercises 3-6) as .hl files that open in
// HeuristicLab 3.3: every configuration is a batch run inside one experiment per task, prepared and
// without runs. Each configuration is tried first on a copy with a tiny budget, so a setup that
// cannot work (e.g. a move without an evaluator for the problem) is reported instead of saved.
//   dotnet run --project next/tools/HeuristicLab.Examples -- <output folder>
//   dotnet run --project next/tools/HeuristicLab.Examples -- probe <algorithm> <problem|-> [instance] [Name=Value...]

if (args is ["probe", var probeAlgorithm, var probeProblem, .. var rest]) {
  var setup = Setups.Create(new SetupRequest(probeAlgorithm) {
    Problem = probeProblem == "-" ? null : probeProblem,
    Instance = rest.FirstOrDefault(r => !r.Contains('=')), Settings = rest.Where(r => r.Contains('=')).ToList()
  });
  foreach (var p in ParameterEditor.Describe(setup.Algorithm))
    Console.WriteLine($"{p.Name} = {p.Value}" + (p.Choices.Count > 0 ? "  [" + string.Join(", ", p.Choices) + "]" : ""));
  if (setup.Algorithm.Problem is IParameterizedItem problemItem)
    foreach (var p in ParameterEditor.Describe(problemItem))
      Console.WriteLine($"  Problem.{p.Name} = {p.Value}" + (p.Choices.Count > 0 ? "  [" + string.Join(", ", p.Choices) + "]" : ""));
  return 0;
}
if (args is ["show", var file]) {
  // what a saved file holds: per batch run its repetitions and the algorithm's (and problem's) settings
  var experiment = (Experiment)Documents.Load(file);
  Console.WriteLine($"{experiment.Name}: {experiment.Description}");
  foreach (var batch in experiment.Optimizers.OfType<BatchRun>()) {
    var algorithm = (IAlgorithm)batch.Optimizer!;
    var settings = ParameterEditor.Describe(algorithm).Where(p => p.Name is not ("Analyzer" or "Seed" or "SetSeedRandomly")).Select(p => $"{p.Name}={p.Value}");
    if (algorithm.Parameters.TryGetValue("Selector", out var selector) && selector is IValueParameter { Value: IParameterizedItem chosen }
        && chosen.Parameters.TryGetValue("GroupSize", out var groupSize) && groupSize is IValueParameter { Value: { } size })
      settings = settings.Append($"Selector.GroupSize={size}");
    if (algorithm.Problem is IParameterizedItem problem)
      settings = settings.Concat(ParameterEditor.Describe(problem).Where(p => p.Name is "ProblemData" or "TestFunction" or "ProblemSize" or "Bounds" or "ReferencePoint").Select(p => $"Problem.{p.Name}={p.Value}"));
    Console.WriteLine($"  {batch.Name} x{batch.Repetitions} [{algorithm.ExecutionState}, {algorithm.Runs.Count} runs, problem {algorithm.Problem?.Name}]: {string.Join("; ", settings)}");
  }
  return 0;
}
if (args.Length != 1) {
  Console.Error.WriteLine("usage: HeuristicLab.Examples <output folder> | probe <algorithm> <problem|-> [instance] [Name=Value...]");
  return 2;
}

var folder = Directory.CreateDirectory(args[0]).FullName;
var failures = new List<string>();
const string Tsp = "TravelingSalesmanProblem", Ch130 = "TSPLIB (symmetric TSP)/ch130", Tsp225 = "TSPLIB (symmetric TSP)/tsp225";

// ---- exercise 3: local search, simulated annealing, tabu search

Save("HEU5_ex03_experiment01", "Local Search on ch130 and tsp225: MaximumIterations 100, three neighbourhoods, 20 repetitions each",
  from instance in new[] { Ch130, Tsp225 }
  from move in new[] { ("Inversion (2-opt)", "StochasticInversionMultiMoveGenerator"),
                       ("Translocation (3-opt)", "StochasticTranslocationMultiMoveGenerator"),
                       ("Insertion", "StochasticInsertionMultiMoveGenerator") }
  select Batch($"LS {Short(instance)} {move.Item1}", 20,
    Algorithm("LocalSearch", Tsp, instance, "MaximumIterations=100", $"MoveGenerator={move.Item2}")));

string[] annealing = ["ExponentialDiscreteDoubleValueModifier", "GeneralizedExponentialDiscreteDoubleValueModifier",
  "LinearDiscreteDoubleValueModifier", "QuadraticDiscreteDoubleValueModifier", "SquareRootDiscreteDoubleValueModifier"];
Save("HEU5_ex03_experiment02", "Simulated Annealing on ch130: InnerIterations 10, MaximumIterations 300, five annealing operators at start temperature 100 / 30 / 300, 20 runs each; Local Search for comparison",
  (from temperature in new[] { 100, 30, 300 }
   from op in annealing
   select Batch($"SA ch130 T{temperature} {op.Replace("DiscreteDoubleValueModifier", "")}", 20,
     Algorithm("SimulatedAnnealing", Tsp, Ch130, "InnerIterations=10", "MaximumIterations=300", $"StartTemperature={temperature}", $"AnnealingOperator={op}")))
  .Append(Batch("LS ch130 Inversion (comparison)", 20, Algorithm("LocalSearch", Tsp, Ch130, "MaximumIterations=100", "MoveGenerator=StochasticInversionMultiMoveGenerator"))));

// HeuristicLab's TSP has no move evaluator for swap2 moves (a swap2 tabu search evaluates nothing):
// the exhaustive 2-opt (inversion) neighbourhood with its soft tabu criterion is the working equivalent
Save("HEU5_ex03_experiment03", "Tabu Search on ch130: MaximumIterations 500, ExhaustiveInversionMoveGenerator, InversionMoveSoftTabuCriterion; TabuTenure 3 (a) and varied (b), 10 runs each",
  from tenure in new[] { 3, 1, 5, 10, 20, 40, 80 }
  select Batch($"TS ch130 TabuTenure {tenure}", 10,
    Algorithm("TabuSearch", Tsp, Ch130, "MaximumIterations=500", "MoveGenerator=ExhaustiveInversionMoveGenerator",
      "TabuChecker=InversionMoveSoftTabuCriterion", $"TabuTenure={tenure}")));

// ---- exercise 4: genetic algorithms

string[] gaBase = ["MaximumGenerations=1000", "PopulationSize=100", "Elites=1", "MutationProbability=0.05", "Mutator=InversionManipulator"];
var selectors = new (string Name, string[] Settings)[] {
  ("Proportional", ["Selector=ProportionalSelector"]),
  ("Tournament 2", ["Selector=TournamentSelector", "Selector.GroupSize=2"]),
  ("Tournament 3", ["Selector=TournamentSelector", "Selector.GroupSize=3"]),
  ("Tournament 4", ["Selector=TournamentSelector", "Selector.GroupSize=4"]) };
Save("HEU5_ex04_experiment01", "GA on ch130 (1000 generations, population 100, 1 elite, 5% InversionManipulator): 3 crossovers x 4 selections, 5 runs each",
  from crossover in new[] { "OrderCrossover2", "EdgeRecombinationCrossover", "PartiallyMatchedCrossover" }
  from selector in selectors
  select Batch($"GA ch130 {crossover} {selector.Name}", 5,
    Algorithm("GeneticAlgorithm", Tsp, Ch130, [.. gaBase, $"Crossover={crossover}", .. selector.Settings])));

Save("HEU5_ex04_experiment02", "GA on ch130 as in experiment 1 with OrderCrossover2 and tournament selection (group size 2): mutation probability 0 / 5 / 20 / 40 / 60 / 100 %, 5 runs each",
  from probability in new[] { 0, 5, 20, 40, 60, 100 }
  select Batch($"GA ch130 OX2 Tournament 2 mutation {probability}%", 5,
    Algorithm("GeneticAlgorithm", Tsp, Ch130, [.. gaBase, "Crossover=OrderCrossover2", "Selector=TournamentSelector", "Selector.GroupSize=2",
      $"MutationProbability={probability / 100.0}"])));

// ---- exercise 5: offspring selection, NSGA-II

// same budget: a GA with population 100 and 1 elite evaluates 100 + 1000 * 99 = 99,100 solutions in 1000 generations
Save("HEU5_ex05_experiment01", "OSGA vs. GA on ch130 with the same number of evaluated solutions (about 100,000), 10 runs each",
  new[] {
    Batch("OSGA ch130 OX2 Proportional", 10, Algorithm("OffspringSelectionGeneticAlgorithm", Tsp, Ch130, OsgaBase("OrderCrossover2", "Selector=ProportionalSelector"))),
    Batch("OSGA ch130 ERX Proportional", 10, Algorithm("OffspringSelectionGeneticAlgorithm", Tsp, Ch130, OsgaBase("EdgeRecombinationCrossover", "Selector=ProportionalSelector"))),
    Batch("OSGA ch130 OX2 Tournament 2", 10, Algorithm("OffspringSelectionGeneticAlgorithm", Tsp, Ch130, OsgaBase("OrderCrossover2", "Selector=TournamentSelector", "Selector.GroupSize=2"))),
    Batch("OSGA ch130 OX2 Proportional MaxSelPressure 300", 10, Algorithm("OffspringSelectionGeneticAlgorithm", Tsp, Ch130, OsgaBase("OrderCrossover2", "Selector=ProportionalSelector", "MaximumSelectionPressure=300"))),
    Batch("GA ch130 OX2 Tournament 2", 10, Algorithm("GeneticAlgorithm", Tsp, Ch130, [.. gaBase, "Crossover=OrderCrossover2", "Selector=TournamentSelector", "Selector.GroupSize=2"])),
    Batch("GA ch130 ERX Tournament 2", 10, Algorithm("GeneticAlgorithm", Tsp, Ch130, [.. gaBase, "Crossover=EdgeRecombinationCrossover", "Selector=TournamentSelector", "Selector.GroupSize=2"])),
    Batch("GA ch130 OX2 Proportional", 10, Algorithm("GeneticAlgorithm", Tsp, Ch130, [.. gaBase, "Crossover=OrderCrossover2", "Selector=ProportionalSelector"])),
  });

string[] nsgaBase = ["CrossoverProbability=0.9", "DominateOnEqualQualities=false", "MaximumGenerations=500", "MutationProbability=0.05",
  "PopulationSize=200", "SelectedParents=400"];
const string MultiObjective = "HeuristicLab.Problems.TestFunctions.MultiObjective.MultiObjectiveTestFunctionProblem";
Save("HEU5_ex05_experiment02", "NSGA-II on ZDT4 (20 dimensions; settings of the task): crossovers (a, with the default mutator) and mutators (b, with SimulatedBinaryCrossover), 10 runs each",
  (from crossover in new[] { "SimulatedBinaryCrossover", "BlendAlphaCrossover", "SinglePointCrossover", "LocalCrossover", "UniformAllPositionsArithmeticCrossover" }
   select Batch($"NSGA-II ZDT4 {crossover}", 10, Nsga2([.. nsgaBase, $"Crossover={crossover}"])))
  .Concat(
   from mutator in new[] { "PolynomialAllPositionManipulator", "PolynomialOnePositionManipulator", "NormalAllPositionsManipulator", "UniformOnePositionManipulator", "MichalewiczNonUniformAllPositionsManipulator" }
   select Batch($"NSGA-II ZDT4 SBX {mutator}", 10, Nsga2([.. nsgaBase, "Crossover=SimulatedBinaryCrossover", $"Mutator={mutator}"]))));

// ---- exercise 6: genetic programming

const string Regression = "HeuristicLab.Problems.DataAnalysis.Symbolic.Regression.SymbolicRegressionSingleObjectiveProblem";
const string Classification = "HeuristicLab.Problems.DataAnalysis.Symbolic.Classification.SymbolicClassificationSingleObjectiveProblem";
var gpProblems = new (string Problem, string Instance, string Short)[] {
  (Regression, "Real World Benchmark Problems/Tower", "Tower (regression)"),
  (Regression, "Real World Benchmark Problems/Housing", "Housing (regression)"),
  (Classification, "UCI Problems/Wisconsin Diagnostic Breast Cancer, Nick Street, 1995", "Wisconsin (classification)"),
  (Classification, "UCI Problems/Mammography, M. Elter, 2007", "Mammography (classification)") };
var gpConfigs = new (string Name, string Algorithm, string[] Settings)[] {
  ("GA pop 500 gen 100 Tournament 4", "GeneticAlgorithm", ["PopulationSize=500", "MaximumGenerations=100", "MutationProbability=0.15", "Selector=TournamentSelector", "Selector.GroupSize=4"]),
  ("GA pop 500 gen 100 Proportional", "GeneticAlgorithm", ["PopulationSize=500", "MaximumGenerations=100", "MutationProbability=0.15", "Selector=ProportionalSelector"]),
  ("GA pop 1000 gen 50 Tournament 7", "GeneticAlgorithm", ["PopulationSize=1000", "MaximumGenerations=50", "MutationProbability=0.15", "Selector=TournamentSelector", "Selector.GroupSize=7"]),
  // an OSGA selects twice the population as parents
  ("OSGA pop 500 MaxSelPressure 100", "OffspringSelectionGeneticAlgorithm", ["PopulationSize=500", "SelectedParents=1000", "MaximumGenerations=100", "MutationProbability=0.15", "MaximumSelectionPressure=100", "Selector=GenderSpecificSelector"]),
  ("OSGA pop 500 MaxSelPressure 300", "OffspringSelectionGeneticAlgorithm", ["PopulationSize=500", "SelectedParents=1000", "MaximumGenerations=100", "MutationProbability=0.15", "MaximumSelectionPressure=300", "Selector=GenderSpecificSelector"]) };
Save("HEU5_ex06_experiment01", "GP for 2 regression (Tower, Housing) and 2 classification problems (Wisconsin, Mammography): 5 configurations, 5 runs each",
  from problem in gpProblems
  from config in gpConfigs
  select Batch($"{problem.Short} {config.Name}", 5, Algorithm(config.Algorithm, problem.Problem, problem.Instance, config.Settings)));

if (failures.Count > 0) {
  Console.Error.WriteLine("Not working:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
  return 1;
}
return 0;

// ---- helpers

string Short(string instance) => instance[(instance.LastIndexOf('/') + 1)..];

string[] OsgaBase(string crossover, params string[] more) =>
  ["PopulationSize=100", "Elites=1", "MutationProbability=0.05", "Mutator=InversionManipulator", $"Crossover={crossover}",
   "MaximumGenerations=100000", "MaximumEvaluatedSolutions=99100", .. more];

IAlgorithm Algorithm(string algorithm, string problem, string? instance, params string[] settings) =>
  Setups.Create(new SetupRequest(algorithm) { Problem = problem, Instance = instance, Settings = settings }).Algorithm;

IAlgorithm Nsga2(string[] settings) {
  var algorithm = Setups.Create(new SetupRequest("NSGA2") { Problem = MultiObjective }).Algorithm;
  var problem = (IParameterizedItem)algorithm.Problem!;
  // the test function is a plain value (in HeuristicLab dragged in), not a choice: ZDT4 from Fonseca's assembly
  var parameter = (IValueParameter)problem.Parameters["TestFunction"];
  var zdt4 = parameter.Value!.GetType().Assembly.GetTypes().Single(t => t.Name == "ZDT4");
  parameter.Value = (IItem)Activator.CreateInstance(zdt4)!;
  ParameterEditor.Apply(problem, ["ProblemSize=20"]);
  ParameterEditor.Apply(algorithm, settings);
  return algorithm;
}

BatchRun Batch(string name, int repetitions, IAlgorithm algorithm) {
  algorithm.Name = name;
  Check(name, algorithm);
  // the algorithm directly in the batch run, as HeuristicLab builds it (no inner experiment)
  return new BatchRun { Name = name, Repetitions = repetitions, Optimizer = algorithm };
}

// a copy with a tiny budget must run and evaluate something
void Check(string name, IAlgorithm algorithm) {
  var copy = (IAlgorithm)algorithm.Clone();
  foreach (var (parameter, value) in new[] { ("MaximumIterations", "3"), ("MaximumGenerations", "2"), ("MaximumEvaluatedSolutions", "2000") })
    if (copy.Parameters.ContainsKey(parameter)) ParameterEditor.Apply(copy, [$"{parameter}={value}"]);
  var report = OptimizerRunner.RunAsync(copy, new RunOptions { Seed = 1 }).GetAwaiter().GetResult();
  var results = report.Runs.LastOrDefault()?.Results;
  double Count(string key) => results != null && results.TryGetValue(key, out var v) && v.Value is int or long or double ? Convert.ToDouble(v.Value) : -1;
  bool evaluated = Count("EvaluatedMoves") > 0 || Count("EvaluatedSolutions") > 0 || (Count("EvaluatedMoves") < 0 && Count("EvaluatedSolutions") < 0);
  if (report.Outcome != RunOutcome.Completed || !evaluated)
    failures.Add($"{name}: {report.Outcome} {report.Error?.Split('\n')[0]} (evaluated moves {Count("EvaluatedMoves")}, solutions {Count("EvaluatedSolutions")})");
}

void Save(string file, string description, IEnumerable<IOptimizer> optimizers) {
  var experiment = new Experiment { Name = file, Description = description };
  foreach (var optimizer in optimizers) ExperimentTree.Add(experiment, optimizer);
  var path = Path.Combine(folder, file + ".hl");
  Documents.Save(experiment, path);
  Console.WriteLine($"{file}.hl: {experiment.Optimizers.Count} batch runs, {experiment.Optimizers.OfType<BatchRun>().Sum(b => b.Repetitions)} runs");
}
