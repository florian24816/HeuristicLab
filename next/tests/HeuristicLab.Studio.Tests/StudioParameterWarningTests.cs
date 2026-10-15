using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using HeuristicLab.Core;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>
/// Choices and values that probably do not work are yellow (ParameterChecks): a GA's selector, a population of 1,
/// an algorithm that cannot run with its problem (and its Start says why), problems an algorithm cannot solve.
/// </summary>
[TestClass]
public class StudioParameterWarningTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  private static readonly Color Yellow = Color.Parse("#F4E803");

  private static IProblem Problem(string algorithm, string problem, string? instance = null) =>
    (IProblem)Setups.Create(new SetupRequest(algorithm) { Problem = problem, Instance = instance }).Algorithm.Problem!.Clone();

  [TestMethod]
  [Timeout(300_000)]
  public async Task WarnedChoicesValuesAndProblemsAreYellow() {
    await session.Dispatch(async () => {
      var main = new MainViewModel(null, null);
      var ws = main.Workspace;
      var window = new MainWindow { DataContext = main, Width = 1400, Height = 900 };
      window.Show();
      var experiment = ws.NewExperiment();

      // a GA on the TSP: the diversity selector compares expression trees, the TSP has none
      var ga = ws.AddAlgorithm(experiment, Catalog.Find(Catalog.Algorithms(), "GeneticAlgorithm")!);
      ws.SetProblem(ga, Problem("GeneticAlgorithm", "TravelingSalesmanProblem", "ch130"));
      ws.Selected = ga;
      var detail = (AlgorithmDetailViewModel)ws.Detail!;
      detail.SelectedTab = AlgorithmDetailViewModel.AlgorithmTab;
      ParameterNodeViewModel Node(string name) => detail.AlgorithmParameters.Parameters.Single(p => p.Name == name);
      var selector = Node("Selector");
      StringAssert.Contains(selector.Choices.Single(c => c.Display == "DiversitySelector").Warning, "expression trees");
      Assert.IsFalse(selector.Choices.Single(c => c.Display == "TournamentSelector").IsWarned);
      Assert.IsNull(detail.AlgorithmWarning, "a GA on the TSP can run");

      // a population of 1: the value is yellow and says why, also in the detail's message
      var population = Node("PopulationSize");
      detail.AlgorithmParameters.Selected = population;
      Pump();
      population.Editor.Text = "1";
      Pump();
      Assert.IsTrue(population.HasWarning);
      StringAssert.Contains(population.Warning, "nothing evolves");
      StringAssert.Contains(detail.Message, "PopulationSize");
      var field = window.GetVisualDescendants().OfType<TextBox>().First(t => t.Classes.Contains("field") && t.IsEffectivelyVisible);
      var fieldFace = field.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_BorderElement");
      Assert.AreEqual(Yellow, ((ISolidColorBrush)fieldFace.Background!).Color);
      // elites depend on the population: 1 elite of 1 is warned as well (lists check again after any edit)
      Assert.IsTrue(Node("Elites").HasWarning);
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-parameter-warnings-value.png"));
      population.Editor.Text = "100";
      Pump();
      Assert.IsFalse(population.HasWarning);
      Assert.IsFalse(Node("Elites").HasWarning);

      // simulated annealing on job shop scheduling: no move operators, HeuristicLab will not run it
      var sa = ws.AddAlgorithm(experiment, Catalog.Find(Catalog.Algorithms(), "SimulatedAnnealing")!);
      ws.SetProblem(sa, Problem("GeneticAlgorithm", "JobShopSchedulingProblem", "abz5"));
      ws.Selected = sa;
      var saDetail = (AlgorithmDetailViewModel)ws.Detail!;
      saDetail.SelectedTab = AlgorithmDetailViewModel.AlgorithmTab;
      Pump();
      StringAssert.Contains(saDetail.AlgorithmWarning, "MoveGenerator");
      var box = window.GetVisualDescendants().OfType<Border>()
        .First(b => b.Classes.Contains("caution") && b.IsEffectivelyVisible && b.Child is TextBlock t && t.Text == saDetail.AlgorithmWarning);
      Assert.AreEqual(Yellow, ((ISolidColorBrush)box.Background!).Color);
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-parameter-warnings-cannot-run.png"));
      saDetail.StartCommand.Execute(null);
      Assert.AreEqual(saDetail.AlgorithmWarning, saDetail.Message, "Start says why instead of failing");
      Assert.AreNotEqual(ExecutionState.Started, sa.Algorithm.ExecutionState);

      // the problem picker of a tabu search: problems it cannot run are yellow and say why
      var picker = new NewSetupViewModel(null, Catalog.Find(Catalog.Algorithms(), "TabuSearch"));
      await picker.WarningsReady;
      var coloring = picker.VisibleProblems.Single(i => i.Display.StartsWith("Graph Coloring", StringComparison.Ordinal));
      Assert.IsTrue(coloring.IsWarned, "attaching it fails in HeuristicLab");
      Assert.AreEqual(Yellow, ((ISolidColorBrush)coloring.Tint).Color);
      Assert.IsTrue(picker.VisibleProblems.Single(i => i.Display.StartsWith("One Max", StringComparison.Ordinal)).IsWarned, "no move evaluator");
      Assert.IsFalse(picker.VisibleProblems.Single(i => i.Display.StartsWith("Traveling Salesman Problem", StringComparison.Ordinal)).IsWarned);
      picker.SelectedProblemItem = coloring;
      Assert.IsNotNull(picker.ProblemWarning);
      var pickerWindow = new NewSetupWindow { DataContext = picker };
      pickerWindow.Show();
      Pump();
      pickerWindow.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-parameter-warnings-problems.png"));
      pickerWindow.Close();
      window.Close();
      return true;
    }, CancellationToken.None);
  }
}
