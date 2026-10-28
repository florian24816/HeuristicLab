using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>
/// "Add results window" (next to the Results tab's header): another Results tab in its own window, named in its title
/// bar ("HL-Results: n", changed live), ticked as the main tab; "Select all", "Select all (without single runs)",
/// "Unselect all" (full orange when that is the state), and in a window "Copy selection from main results tab".
/// </summary>
[TestClass]
public class StudioResultsWindowTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  private static readonly Color Orange = Color.Parse("#F7931D"), Pale = Color.Parse("#FDE5C8");

  private static Button Shortcut(Visual root, string text) =>
    root.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == text && b.IsEffectivelyVisible);

  private static Color Fill(Button button) => ((ISolidColorBrush)button.Background!).Color;

  [TestMethod]
  [Timeout(300_000)]
  public async Task ResultsWindowsCompareSelections() {
    await session.Dispatch(async () => {
      var vm = new MainViewModel(null, null);
      var ws = vm.Workspace;
      var main = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
      main.Show();
      var experiment = ws.NewExperiment();
      var ga = ws.AddAlgorithm(experiment, Catalog.Find(Catalog.Algorithms(), "GeneticAlgorithm")!);
      ws.SetProblem(ga, (IProblem)Setups.Create(new SetupRequest("GeneticAlgorithm") { Problem = "TravelingSalesmanProblem", Instance = "ch130" }).Algorithm.Problem!.Clone());
      ParameterEditor.Apply(ga.Algorithm, ["PopulationSize=10", "MaximumGenerations=2"]);
      await ws.StartAllCommand.ExecuteAsync(null);
      experiment.SingleRuns.Add([(IRun)experiment.Experiment.Runs.Single().Clone()]);

      // "Add results window" only while the Results tab is shown, in its header
      Pump();
      var add = main.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Add window");
      Assert.IsFalse(add.IsEffectivelyVisible);
      vm.SelectedTab = MainViewModel.ResultsTab;
      Pump();
      Assert.IsTrue(add.IsEffectivelyVisible);
      // a plain rectangle in grey: darker than the window in light mode, lighter in dark mode
      Assert.AreEqual(new CornerRadius(0), add.CornerRadius);
      Assert.AreEqual(Color.Parse("#D4D4D4"), Fill(add));
      Assert.IsTrue(add.FontSize > 13, "a little larger than before");
      vm.IsDarkMode = true;
      Pump();
      Assert.AreEqual(Color.Parse("#3A3A3A"), Fill(add));
      main.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-results-addwindow-dark.png"));
      vm.IsDarkMode = false;
      Pump();
      var results = main.GetVisualDescendants().OfType<TabItem>().Single(t => t.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "Results"));
      var scripts = main.GetVisualDescendants().OfType<TabItem>().Single(t => t.Header as string == "Scripts");
      double addX = add.TranslatePoint(default, main)!.Value.X;
      Assert.IsTrue(addX > results.TranslatePoint(default, main)!.Value.X && addX < scripts.TranslatePoint(default, main)!.Value.X, "between Results and Scripts");

      // the main tab: the experiment ticked, not the single runs - "Select all (without single runs)" is full orange
      var analysis = vm.RunAnalysis;
      analysis.Sources.First().IsChecked = true;
      Pump();
      Assert.IsTrue(analysis.AllButSingleTicked);
      Assert.IsFalse(analysis.AllTicked);
      Assert.AreEqual(Orange, Fill(Shortcut(main, "Select all (without single runs)")));
      Assert.AreEqual(Pale, Fill(Shortcut(main, "Select all")));
      Assert.AreEqual(Pale, Fill(Shortcut(main, "Unselect all")));
      Assert.IsFalse(main.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Copy selection from main results tab" && b.IsEffectivelyVisible),
        "only results windows copy from the main tab");
      main.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-results-shortcuts.png"));

      // a results window: named "HL-Results: 1", ticked as the main tab
      add.Command!.Execute(null);
      Pump();
      var window = main.OpenResultsWindows.Single();
      Assert.AreEqual("HL-Results: 1", window.Title);
      var other = vm.ResultsWindows.Single().Analysis;
      Assert.AreEqual(analysis.Table.Rows.Count, other.Table.Rows.Count);
      Assert.IsTrue(other.MatchesMain);
      Assert.AreEqual(Orange, Fill(Shortcut(window, "Copy selection from main results tab")), "ticked as the main tab: full orange");
      Assert.IsFalse(window.GetVisualDescendants().OfType<TabItem>().Any(t => t.Header as string == "Experiments"), "no main tabs");
      // its name, live in the title bar
      var name = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Classes.Contains("windowname"));
      name.Text = "GA only";
      Pump();
      Assert.AreEqual("GA only", window.Title);
      // select all / unselect all / copy from the main tab
      Shortcut(window, "Select all").Command!.Execute(null);
      Pump();
      Assert.IsTrue(other.AllTicked);
      Assert.AreEqual(analysis.Table.Rows.Count + 1, other.Table.Rows.Count, "with the single run");
      Assert.AreEqual(Orange, Fill(Shortcut(window, "Select all")));
      Assert.AreEqual(1, analysis.Table.Rows.Count, "the main tab keeps its own selection");
      Shortcut(window, "Unselect all").Command!.Execute(null);
      Assert.IsTrue(other.NoneTicked);
      Pump();
      Assert.AreEqual(Pale, Fill(Shortcut(window, "Copy selection from main results tab")), "not the main tab's ticks any more");
      Assert.AreEqual(0, other.Table.Rows.Count);
      Shortcut(window, "Copy selection from main results tab").Command!.Execute(null);
      Pump();
      Assert.IsTrue(other.AllButSingleTicked);
      Assert.AreEqual(analysis.Table.Rows.Count, other.Table.Rows.Count);
      Assert.AreEqual(Orange, Fill(Shortcut(window, "Copy selection from main results tab")));
      // the main tab changes: the window's copy button notices
      analysis.SelectAllCommand.Execute(null);
      Pump();
      Assert.IsFalse(other.MatchesMain);
      Assert.AreEqual(Pale, Fill(Shortcut(window, "Copy selection from main results tab")));
      analysis.SelectAllWithoutSingleCommand.Execute(null);
      Pump();
      Assert.IsTrue(other.MatchesMain);
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-results-window.png"));

      // a second window counts on; closing one forgets it
      vm.AddResultsWindowCommand.Execute(null);
      Pump();
      Assert.AreEqual("HL-Results: 2", main.OpenResultsWindows[1].Title);
      window.Close();
      Pump();
      Assert.AreEqual(1, vm.ResultsWindows.Count);
      main.OpenResultsWindows.Single().Close();
      main.Close();
      return true;
    }, CancellationToken.None);
  }

  private sealed class Dialogs : HeuristicLab.Studio.Services.IDialogService {
    public bool Answer { get; set; }
    public List<string> Asked { get; } = [];
    public Task<SetupResult?> NewSetupAsync() => Task.FromResult<SetupResult?>(null);
    public Task<CatalogEntry?> PickAlgorithmAsync() => Task.FromResult<CatalogEntry?>(null);
    public Task<IProblem?> PickProblemAsync(IAlgorithm algorithm) => Task.FromResult<IProblem?>(null);
    public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
    public Task<bool> AcknowledgeAsync(string title, string message) { Asked.Add(message); return Task.FromResult(Answer); }
  }

  [TestMethod]
  [Timeout(300_000)]
  public async Task CloseWindowsAsksThenClosesThemAll() {
    await session.Dispatch(async () => {
      var dialogs = new Dialogs();
      var vm = new MainViewModel(null, dialogs);
      var main = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
      main.Show();
      vm.Workspace.NewExperiment();
      vm.SelectedTab = MainViewModel.ResultsTab;
      Pump();
      // in the main Results tab only, pale red, usable while results windows are open
      var close = Shortcut(main, "Close windows");
      Assert.AreEqual(Color.Parse("#F6CACA"), Fill(close));
      Assert.IsFalse(close.Command!.CanExecute(null), "no windows yet");
      vm.AddResultsWindowCommand.Execute(null);
      vm.AddResultsWindowCommand.Execute(null);
      Pump();
      Assert.IsTrue(close.Command.CanExecute(null));
      Assert.AreEqual(2, main.OpenResultsWindows.Count);
      Assert.IsFalse(main.OpenResultsWindows[0].GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Close windows" && b.IsEffectivelyVisible),
        "results windows copy from the main tab instead");
      main.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-results-closewindows.png"));

      // the dialog closed without "OK": nothing happens
      dialogs.Answer = false;
      await vm.CloseResultsWindowsCommand.ExecuteAsync(null);
      CollectionAssert.AreEqual(new[] { "2 result windows are about to be closed" }, dialogs.Asked);
      Assert.AreEqual(2, main.OpenResultsWindows.Count);
      // "OK": all closed
      dialogs.Answer = true;
      await vm.CloseResultsWindowsCommand.ExecuteAsync(null);
      Pump();
      Assert.AreEqual(0, main.OpenResultsWindows.Count);
      Assert.AreEqual(0, vm.ResultsWindows.Count);
      Assert.IsFalse(close.Command.CanExecute(null));

      // the dialog itself: only "OK" (closing it is the way back)
      var ask = new ConfirmWindow("Close windows", "2 result windows are about to be closed", yes: "OK", cancel: false);
      ask.Show(main);
      Pump();
      var buttons = ask.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string).ToList();
      CollectionAssert.AreEqual(new[] { "OK" }, buttons);
      ask.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-results-closewindows-dialog.png"));
      ask.Close();
      main.Close();
      return true;
    }, CancellationToken.None);
  }
}
