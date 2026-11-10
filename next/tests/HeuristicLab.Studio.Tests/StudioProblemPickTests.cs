using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;
using HeuristicLab.Studio.Services;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>
/// Picking problem blocks: an algorithm picked takes its problem along (shown picked, blue under the rectangle), a
/// problem picked alone leaves its algorithm; only problems picked: "Change selected problem blocks to..." gives all of
/// them the problem chosen, whatever their algorithms.
/// </summary>
[TestClass]
public class StudioProblemPickTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private sealed class ProblemDialogs(Func<IProblem> problem) : IDialogService {
    public IAlgorithm? AskedFor { get; private set; }
    public Task<SetupResult?> NewSetupAsync() => Task.FromResult<SetupResult?>(null);
    public Task<CatalogEntry?> PickAlgorithmAsync() => Task.FromResult<CatalogEntry?>(null);
    public Task<IProblem?> PickProblemAsync(IAlgorithm algorithm) {
      AskedFor = algorithm;
      return Task.FromResult<IProblem?>(problem());
    }
    public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
  }

  private static IProblem Tsp(string instance) =>
    (IProblem)Setups.Create(new SetupRequest("TabuSearch") { Problem = "TravelingSalesmanProblem", Instance = instance }).Algorithm.Problem!.Clone();

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  private static Border BlockBorder(Window window, object block) =>
    window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("block") && b.DataContext == block);

  [TestMethod]
  [Timeout(300_000)]
  public async Task ProblemsArePickedAloneOrWithTheirAlgorithm() {
    await session.Dispatch(() => {
      var dialogs = new ProblemDialogs(() => Tsp("ch130"));
      var vm = new MainViewModel(null, dialogs);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
      window.Show();
      var experiment = ws.NewExperiment();
      var ts = ws.AddAlgorithm(experiment, Catalog.Find(Catalog.Algorithms(), "TabuSearch")!);
      var ga = ws.AddAlgorithm(experiment, Catalog.Find(Catalog.Algorithms(), "GeneticAlgorithm")!);
      ws.SetProblem(ts, Tsp("berlin52"));
      ws.SetProblem(ga, Tsp("berlin52"));
      ws.Selected = null;
      Pump();

      // the rectangle over the algorithm only: its problem gets the blue frame too, and both are picked when let go
      ws.CoverWithBand(ws.InOneExperiment([ts], null));
      Pump();
      Assert.IsTrue(ts.Problem!.IsBandCovered);
      Assert.IsTrue(BlockBorder(window, ts.Problem).Classes.Contains("bandcovered"));
      ws.CoverWithBand(null);
      ws.Pick([ts], add: false);
      Assert.IsTrue(ts.IsPicked && ts.Problem.IsPicked, "algorithm and problem shown picked");
      // over algorithm and problem: the same
      ws.Pick([ts, ts.Problem], add: false);
      Assert.IsTrue(ts.IsPicked && ts.Problem.IsPicked);
      Assert.IsFalse(ws.OnlyProblemsPicked());

      // a rectangle drawn with the mouse over the problems only: blue frames on the problems, not on the algorithms
      ws.ClearPicks();
      Pump();
      var tsProblem = BlockBorder(window, ts.Problem);
      var gaProblem = BlockBorder(window, ga.Problem!);
      var start = gaProblem.TranslatePoint(new Point(gaProblem.Bounds.Width + 30, gaProblem.Bounds.Height + 4), window)!.Value;
      var end = tsProblem.TranslatePoint(new Point(gaProblem.Bounds.Width - 20, 6), window)!.Value;
      window.MouseDown(start, MouseButton.Left);
      window.MouseMove(end, RawInputModifiers.LeftMouseButton);
      Pump();
      Assert.IsTrue(tsProblem.Classes.Contains("bandcovered") && gaProblem.Classes.Contains("bandcovered"), "problems framed blue");
      Assert.IsFalse(BlockBorder(window, ts).Classes.Contains("bandcovered") || BlockBorder(window, ga).Classes.Contains("bandcovered"));
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-problems-band.png"));
      window.MouseUp(end, MouseButton.Left);
      Pump();
      Assert.IsTrue(ts.Problem.IsPicked && ga.Problem!.IsPicked, "problems picked");
      Assert.IsFalse(ts.IsPicked || ga.IsPicked, "their algorithms not");
      Assert.IsTrue(ws.OnlyProblemsPicked());
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-problems-picked.png"));

      // their menu: change them all (no copies)
      var menu = ts.Problem.Menu.Select(m => m.Display).ToList();
      CollectionAssert.Contains(menu, "Change selected problem blocks to...");
      Assert.IsFalse(menu.Any(m => m.StartsWith("Copy")), string.Join(", ", menu));
      ts.Problem.Menu.Single(m => m.Display == "Change selected problem blocks to...").Command.Execute(null);
      Pump();
      Assert.AreSame(ts.Algorithm, dialogs.AskedFor, "the dialog offers the first one's algorithm's problems");
      Assert.AreEqual("ch130", ts.Algorithm.Problem!.Name);
      Assert.AreEqual("ch130", ga.Algorithm.Problem!.Name, "a tabu search and a genetic algorithm alike");
      Assert.AreNotSame(ts.Algorithm.Problem, ga.Algorithm.Problem, "a copy each");
      Assert.AreEqual("ch130", ts.Problem!.Name);
      Assert.IsFalse(ws.AllBlocks().Any(b => b.IsPicked));

      // Ctrl+click on a problem picks it alone; on its algorithm both
      ws.TogglePick(ga.Problem!);
      Assert.IsTrue(ga.Problem!.IsPicked && !ga.IsPicked);
      ws.TogglePick(ga);
      Assert.IsTrue(ga.Problem.IsPicked && ga.IsPicked);
      ws.TogglePick(ga.Problem);  // picked with its algorithm: both go
      Assert.IsFalse(ga.Problem.IsPicked || ga.IsPicked);
      window.Close();
      return true;
    }, CancellationToken.None);
  }
}
