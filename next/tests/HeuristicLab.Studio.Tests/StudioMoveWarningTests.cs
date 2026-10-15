using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>
/// Move generators that cannot work (no move evaluator for their moves) are yellow in the choice, and after a
/// choice the move evaluator / maker lists are the new valid ones (not the old ones, which could not be chosen).
/// </summary>
[TestClass]
public class StudioMoveWarningTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  private static readonly Color Yellow = Color.Parse("#F4E803");

  [TestMethod]
  [Timeout(300_000)]
  public async Task MoveGeneratorsWithoutEvaluatorAreYellowAndTheListsFollowTheChoice() {
    await session.Dispatch(() => {
      var main = new MainViewModel(null, null);
      var ws = main.Workspace;
      var window = new MainWindow { DataContext = main, Width = 1400, Height = 900 };
      window.Show();
      var experiment = ws.NewExperiment();
      var ls = ws.AddAlgorithm(experiment, Catalog.Find(Catalog.Algorithms(), "LocalSearch")!);
      ws.SetProblem(ls, (IProblem)Setups.Create(new SetupRequest("LocalSearch") { Problem = "TravelingSalesmanProblem", Instance = "ch130" }).Algorithm.Problem!.Clone());
      ws.Selected = ls;
      var detail = (AlgorithmDetailViewModel)ws.Detail!;
      detail.SelectedTab = AlgorithmDetailViewModel.AlgorithmTab;
      var list = detail.AlgorithmParameters;
      ParameterNodeViewModel Node(string name) => list.Parameters.Single(p => p.Name == name);
      var generator = Node("MoveGenerator");
      list.Selected = generator;
      Pump();

      // the TSP has no move evaluator for swap2 and scramble moves
      CollectionAssert.AreEquivalent(
        new[] { "ExhaustiveSwap2MoveGenerator", "StochasticScrambleMultiMoveGenerator", "StochasticSwap2MultiMoveGenerator", "StochasticSwap2SingleMoveGenerator" },
        generator.Choices.Where(c => c.IsWarned).Select(c => c.Display).ToList());
      StringAssert.Contains(generator.Choices.First(c => c.Display == "StochasticSwap2SingleMoveGenerator").Warning, "no MoveEvaluator");
      Assert.IsFalse(generator.HasWarning, "the default (exhaustive insertion) works");

      // the open list: warned entries yellow with dark writing
      var combo = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Classes.Contains("field") && c.IsEffectivelyVisible);
      combo.IsDropDownOpen = true;
      Pump();
      for (int i = 0; i < generator.Choices.Count; i++) {
        var container = (ComboBoxItem)combo.ContainerFromIndex(i)!;
        Assert.AreEqual(generator.Choices[i].IsWarned, container.Classes.Contains("warned"), generator.Choices[i].Display);
        var presenter = container.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
        if (generator.Choices[i].IsWarned && !container.IsSelected)
          Assert.AreEqual(Yellow, ((ISolidColorBrush)presenter.Background!).Color, generator.Choices[i].Display);
      }
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-move-warnings-open.png"));
      combo.IsDropDownOpen = false;

      // choosing one: the move evaluator is none (true in HeuristicLab) and its list is empty, both said
      var before = Node("MoveEvaluator").Choices.Count;
      Assert.AreEqual(1, before);
      generator.SelectedChoice = generator.Choices.First(c => c.Display == "StochasticSwap2SingleMoveGenerator");
      Pump();
      var evaluator = Node("MoveEvaluator");
      Assert.AreEqual(0, evaluator.Choices.Count, "the old evaluators are gone (choosing one failed silently)");
      Assert.IsTrue(evaluator.IsChoice);
      Assert.IsTrue(evaluator.HasWarning);
      StringAssert.Contains(evaluator.Warning, "No MoveEvaluator");
      Assert.IsTrue(generator.HasWarning);
      Assert.IsFalse(Node("MoveMaker").HasWarning, "the swap2 move maker fits");
      Assert.AreEqual("Swap2MoveMaker", Node("MoveMaker").SelectedChoice!.Display);
      StringAssert.Contains(detail.Message, "MoveGenerator");
      Pump();
      var face = combo.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Background");
      Assert.AreEqual(Yellow, ((ISolidColorBrush)face.Background!).Color, "the chosen one is yellow too");
      var cautions = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("caution") && b.IsEffectivelyVisible).ToList();
      Assert.IsTrue(cautions.Count >= 3, "two rows in the list (generator, evaluator) and the text below the choice");
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-move-warnings-light.png"));
      list.Selected = evaluator;
      Pump();
      Assert.IsFalse(window.GetVisualDescendants().OfType<ComboBox>().Any(c => c.Classes.Contains("field") && c.IsEffectivelyVisible), "nothing to choose: only the warning");
      Assert.IsTrue(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text?.StartsWith("No MoveEvaluator") == true));
      main.IsDarkMode = true;
      Pump();
      Assert.IsTrue(window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("caution") && b.IsEffectivelyVisible)
        .All(b => ((ISolidColorBrush)b.Background!).Color == Yellow), "the same yellow in dark mode");
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-move-warnings-dark.png"));
      main.IsDarkMode = false;

      // back to inversion: an evaluator again, offered and chosen, no warnings
      list.Selected = generator;
      generator.SelectedChoice = generator.Choices.First(c => c.Display == "StochasticInversionMultiMoveGenerator");
      Pump();
      evaluator = Node("MoveEvaluator");
      Assert.AreEqual("TSPInversionMoveRoundedEuclideanPathEvaluator", evaluator.SelectedChoice?.Display);
      Assert.AreEqual(1, evaluator.Choices.Count);
      Assert.IsFalse(list.Parameters.Any(p => p.HasWarning));
      window.Close();
      return true;
    }, CancellationToken.None);
  }
}
