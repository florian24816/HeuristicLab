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

  private static Panel Tab(Visual root, ResultsWindowViewModel results) =>
    root.GetVisualDescendants().OfType<Panel>().Single(p => p.Classes.Contains("resultstab") && p.DataContext == results && p.IsEffectivelyVisible);

  private static int Ghosts(Window window) =>
    window.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("ghost") && b.IsEffectivelyVisible);

  private static bool Skewed(Border shape) => shape.RenderTransform is { } t && t.Value.M21 != 0;

  private static Border Shape(Panel tab) => tab.Children.OfType<Border>().Single(b => b.Classes.Contains("shape"));

  [TestMethod]
  [Timeout(300_000)]
  public async Task ResultsTabsDockAndUndock() {
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

      // "Add window" only while the Results tab is shown, in its header
      Pump();
      var add = main.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Add window");
      Assert.IsFalse(add.IsEffectivelyVisible);
      vm.SelectedTab = MainViewModel.ResultsTab;
      Pump();
      Assert.IsTrue(add.IsEffectivelyVisible);
      Assert.AreEqual(new CornerRadius(0), add.CornerRadius);
      Assert.AreEqual(Color.Parse("#D4D4D4"), Fill(add));
      var bar = main.GetVisualDescendants().OfType<ResultsTabBar>().Single();
      Assert.IsFalse(bar.IsEffectivelyVisible, "no second tab bar before a further results tab exists");

      // the main tab: the experiment ticked, not the single runs - "Select all (without single runs)" is full orange
      var analysis = vm.RunAnalysis;
      analysis.Sources.First().IsChecked = true;
      Pump();
      Assert.IsTrue(analysis.AllButSingleTicked);
      Assert.AreEqual(Orange, Fill(Shortcut(main, "Select all (without single runs)")));
      Assert.AreEqual(Pale, Fill(Shortcut(main, "Select all")));

      // "Add window": a tab in the second bar (spanning the window), selected and shown, ticked as the main tab
      add.Command!.Execute(null);
      Pump();
      var first = vm.ResultsWindows.Single();
      Assert.AreEqual("HL-Results: 1", first.Name);
      Assert.IsTrue(first.IsDocked && first.IsSelected);
      Assert.AreSame(first.Analysis, vm.ShownAnalysis);
      Assert.IsTrue(bar.IsEffectivelyVisible);
      Assert.AreEqual(main.Bounds.Width, bar.Bounds.Width, 1, "across the whole window");
      Assert.AreEqual(0, main.OpenResultsWindows.Count, "in the main window, not a window yet");
      Assert.IsTrue(first.Analysis.MatchesMain);
      Assert.AreEqual(Orange, Fill(Shortcut(main, "Copy selection from main results tab")));
      // "Main results" is a rectangle (first), further tabs parallelograms with italic names and a small cross
      var mainTab = main.GetVisualDescendants().OfType<Panel>().Single(p => p.Classes.Contains("resultstab") && p.Classes.Contains("main"));
      Assert.IsFalse(Skewed(Shape(mainTab)));
      var tab = Tab(main, first);
      Assert.IsTrue(Skewed(Shape(tab)), "a parallelogram while docked");
      Assert.AreEqual(FontStyle.Italic, tab.GetVisualDescendants().OfType<TextBlock>().First().FontStyle);
      Assert.IsTrue(tab.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "✕"));
      Assert.IsTrue(mainTab.TranslatePoint(default, main)!.Value.X < tab.TranslatePoint(default, main)!.Value.X, "Main results first");
      Assert.AreEqual(Orange, ((ISolidColorBrush)Shape(tab).Background!).Color, "the selected tab");
      // its name, under the bar, live in the tab
      var name = main.GetVisualDescendants().OfType<TextBox>().Single(t => t.Classes.Contains("windowname") && t.IsEffectivelyVisible);
      name.Text = "GA only";
      Pump();
      Assert.AreEqual("GA only", first.Name);
      Assert.IsTrue(tab.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "GA only"));
      main.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-results-tabs.png"));

      // its own selection: select all / unselect all / copy from the main tab
      var other = first.Analysis;
      Shortcut(main, "Select all").Command!.Execute(null);
      Pump();
      Assert.IsTrue(other.AllTicked);
      Assert.AreEqual(analysis.Table.Rows.Count + 1, other.Table.Rows.Count, "with the single run");
      Assert.AreEqual(1, analysis.Table.Rows.Count, "the main tab keeps its own selection");
      Assert.AreEqual(Pale, Fill(Shortcut(main, "Copy selection from main results tab")));
      Shortcut(main, "Copy selection from main results tab").Command!.Execute(null);
      Assert.IsTrue(other.MatchesMain);

      // "Main results" shows the main tab again
      vm.SelectResults(null);
      Pump();
      Assert.AreSame(analysis, vm.ShownAnalysis);
      Assert.IsFalse(first.IsSelected);
      Assert.AreEqual(Orange, ((ISolidColorBrush)Shape(mainTab).Background!).Color);

      // a second tab, dragged out of the bar: a window of its own, a rectangle in the bar now
      vm.AddResultsWindowCommand.Execute(null);
      Pump();
      var second = vm.ResultsWindows[1];
      Assert.AreEqual("HL-Results: 2", second.Name);
      var secondTab = Tab(main, second);
      var at = secondTab.TranslatePoint(new Point(secondTab.Bounds.Width / 2, secondTab.Bounds.Height / 2), main)!.Value;
      main.MouseDown(at, MouseButton.Left);
      main.MouseMove(at + new Point(10, 40), RawInputModifiers.LeftMouseButton);
      main.MouseMove(at + new Point(20, 260), RawInputModifiers.LeftMouseButton);
      main.MouseUp(at + new Point(20, 260), MouseButton.Left);
      Pump();
      Assert.IsFalse(second.IsDocked);
      var window = main.OpenResultsWindows.Single();
      Assert.AreEqual("HL-Results: 2", window.Title);
      Assert.AreSame(analysis, vm.ShownAnalysis, "the main window shows the main results again");
      Assert.IsFalse(Skewed(Shape(Tab(main, second))), "a rectangle while it is a window");
      // the window: the second tab bar at its top, its name below, no main tabs
      Assert.IsTrue(window.GetVisualDescendants().OfType<ResultsTabBar>().Single().IsEffectivelyVisible);
      Assert.IsFalse(window.GetVisualDescendants().OfType<TabItem>().Any(t => t.Header as string == "Experiments"));
      var windowName = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Classes.Contains("windowname"));
      windowName.Text = "Compare";
      Pump();
      Assert.AreEqual("Compare", window.Title);
      Pump();
      Assert.AreEqual(Orange, ((ISolidColorBrush)Shape(Tab(window, second)).Background!).Color, "in its window, its own tab is highlighted");
      var windowMain = window.GetVisualDescendants().OfType<Panel>().Single(p => p.Classes.Contains("resultstab") && p.Classes.Contains("main"));
      Assert.AreNotEqual(Orange, ((ISolidColorBrush)Shape(windowMain).Background!).Color);
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-results-window.png"));
      main.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-results-tabs-undocked.png"));

      // moved over the main window's bar: its tab's gap with the one ghost where it would land; it docks back only
      // once the mouse button is let go there (held: it stays a window, however long)
      var onBar = bar.PointToScreen(new Point(bar.Bounds.Width / 2, bar.Bounds.Height / 2));
      Assert.IsTrue(main.WouldDock(onBar));
      Assert.IsFalse(main.WouldDock(new PixelPoint(onBar.X, onBar.Y + 300)));
      bool held = true;
      main.IsMouseDown = () => held;
      var firstTab = Tab(main, first);
      var beforeFirst = bar.PointToScreen(firstTab.TranslatePoint(new Point(4, firstTab.Bounds.Height / 2), bar)!.Value);
      main.WindowOver(second, beforeFirst);
      Pump();
      Assert.AreEqual(1, Ghosts(main), "one ghost");
      Assert.AreEqual(0, Tab(main, second).Opacity, "the window's own tab gives way to the ghost");
      Assert.IsTrue(bar.Ghost!.Opacity < 1);
      Assert.AreEqual(0, bar.Landing, "before the first tab");
      main.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-results-dock-ghost.png"));
      await Task.Delay(MainWindow.DockAfter + TimeSpan.FromMilliseconds(200));
      Pump();
      Assert.IsFalse(second.IsDocked, "the button is still held: no docking yet");
      Assert.AreEqual(1, main.OpenResultsWindows.Count, "the window stays while it is held");
      held = false;
      main.CheckLetGo(null, EventArgs.Empty);
      Pump();
      Assert.IsTrue(second.IsDocked && second.IsSelected, "let go over the bar: docked");
      Assert.AreEqual(0, main.OpenResultsWindows.Count);
      CollectionAssert.AreEqual(new[] { second, first }, vm.ResultsWindows.ToList(), "where the ghost was");
      Assert.AreEqual(0, Ghosts(main));
      Assert.AreEqual(1, Tab(main, second).Opacity);
      // moved away again before it was let go: no docking, no ghost
      vm.Undock(second);
      Pump();
      held = true;
      main.WindowOver(second, beforeFirst);
      main.WindowOver(second, null);
      held = false;
      main.CheckLetGo(null, EventArgs.Empty);
      Pump();
      Assert.IsFalse(second.IsDocked);
      Assert.AreEqual(0, Ghosts(main));
      vm.Dock(second, 2);  // after both tabs (places count the tabs before it is taken out)
      Pump();
      CollectionAssert.AreEqual(new[] { first, second }, vm.ResultsWindows.ToList());

      // dragging a tab along the bar: the tab disappears, one ghost in the gap where it lands
      var drag = Tab(main, second);
      var from = drag.TranslatePoint(new Point(drag.Bounds.Width / 2, drag.Bounds.Height / 2), main)!.Value;
      var leftOfFirst = Tab(main, first).TranslatePoint(new Point(4, drag.Bounds.Height / 2), main)!.Value;
      main.MouseDown(from, MouseButton.Left);
      main.MouseMove(from + new Point(-10, 0), RawInputModifiers.LeftMouseButton);
      main.MouseMove(leftOfFirst, RawInputModifiers.LeftMouseButton);
      Pump();
      Assert.AreEqual(1, Ghosts(main), "only the ghost");
      Assert.AreEqual(0, drag.Opacity, "the original disappears meanwhile");
      // the other tab slid aside to make room: right after the ghost, still in view
      var shifted = Tab(main, first);
      double ghostRight = bar.Ghost!.Margin.Left + bar.Ghost.Bounds.Width;
      double shiftedLeft = shifted.TranslatePoint(default, bar)!.Value.X;
      Assert.IsTrue(Math.Abs(shiftedLeft - ghostRight) < 12, $"next to the ghost: {shiftedLeft} vs {ghostRight}");
      Assert.IsTrue(shiftedLeft + shifted.Bounds.Width < bar.Bounds.Width);
      Assert.AreEqual(0, bar.Landing);
      main.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-results-tab-drag.png"));
      // off the bar the ghost follows the pointer
      main.MouseMove(leftOfFirst + new Point(30, 200), RawInputModifiers.LeftMouseButton);
      Pump();
      Assert.AreEqual(1, Ghosts(main));
      Assert.IsNull(bar.Landing);
      main.MouseMove(leftOfFirst, RawInputModifiers.LeftMouseButton);
      main.MouseUp(leftOfFirst, MouseButton.Left);
      Pump();
      Assert.AreEqual(0, Ghosts(main));
      CollectionAssert.AreEqual(new[] { second, first }, vm.ResultsWindows.ToList(), "moved before the first");
      Assert.AreEqual(1, Tab(main, second).Opacity);
      // let go where nothing happens: back where it was
      drag = Tab(main, second);
      from = drag.TranslatePoint(new Point(drag.Bounds.Width / 2, drag.Bounds.Height / 2), main)!.Value;
      main.MouseDown(from, MouseButton.Left);
      main.MouseMove(from + new Point(3, 12), RawInputModifiers.LeftMouseButton);
      main.MouseUp(from + new Point(3, 12), MouseButton.Left);
      Pump();
      Assert.AreEqual(0, Ghosts(main));
      CollectionAssert.AreEqual(new[] { second, first }, vm.ResultsWindows.ToList());
      Assert.IsTrue(second.IsDocked);

      // shuffling: three tabs dragged around many times - names always in the right order, never more than one ghost
      vm.AddResultsWindowCommand.Execute(null);
      Pump();
      var random = new System.Random(4);
      for (int round = 0; round < 12; round++) {
        var order = vm.ResultsWindows.ToList();
        var moving = order[random.Next(order.Count)];
        var target = order[random.Next(order.Count)];
        var source = Tab(main, moving);
        var start = source.TranslatePoint(new Point(source.Bounds.Width / 2, source.Bounds.Height / 2), main)!.Value;
        var aim = Tab(main, target);
        var end = aim.TranslatePoint(new Point(random.Next(2) == 0 ? 3 : aim.Bounds.Width - 3, aim.Bounds.Height / 2), main)!.Value;
        main.MouseDown(start, MouseButton.Left);
        main.MouseMove(start + new Point(8, 0), RawInputModifiers.LeftMouseButton);
        main.MouseMove(end, RawInputModifiers.LeftMouseButton);
        Pump();
        Assert.IsTrue(Ghosts(main) <= 1, $"round {round}: at most one ghost");
        main.MouseUp(end, MouseButton.Left);
        Pump();
        Assert.AreEqual(0, Ghosts(main), $"round {round}: no ghost left");
        var shown = main.GetVisualDescendants().OfType<Panel>()
          .Where(p => p.Classes.Contains("resultstab") && !p.Classes.Contains("main") && p.IsEffectivelyVisible)
          .OrderBy(p => p.TranslatePoint(default, main)!.Value.X)
          .Select(p => p.GetVisualDescendants().OfType<TextBlock>().First().Text).ToList();
        CollectionAssert.AreEqual(vm.ResultsWindows.Select(r => r.Name).ToList(), shown, $"round {round}: names in the tabs' order");
      }
      vm.CloseResults(vm.ResultsWindows.Single(r => r != first && r != second));
      Pump();
      Assert.IsTrue(Skewed(Shape(Tab(main, second))));
      Assert.IsTrue(Skewed(Shape(Tab(main, first))));

      // the small cross closes a tab
      var cross = Tab(main, first).GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "✕");
      var crossAt = cross.TranslatePoint(new Point(cross.Bounds.Width / 2, cross.Bounds.Height / 2), main)!.Value;
      main.MouseDown(crossAt, MouseButton.Left);
      main.MouseUp(crossAt, MouseButton.Left);
      Pump();
      CollectionAssert.AreEqual(new[] { second }, vm.ResultsWindows.ToList());
      vm.CloseResults(second);
      Pump();
      Assert.IsFalse(bar.IsEffectivelyVisible, "no further tabs: no second bar");
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
      vm.Undock(vm.ResultsWindows[1]);
      vm.SelectResults(null);
      Pump();
      close = Shortcut(main, "Close windows");  // the main tab's view is built again when it is shown again
      Assert.IsTrue(close.Command!.CanExecute(null));
      Assert.AreEqual(1, main.OpenResultsWindows.Count, "one docked tab, one window");
      Assert.IsFalse(main.OpenResultsWindows[0].GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Close windows" && b.IsEffectivelyVisible),
        "results windows copy from the main tab instead");
      main.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-results-closewindows.png"));

      // the dialog closed without "OK": nothing happens
      dialogs.Answer = false;
      await vm.CloseResultsWindowsCommand.ExecuteAsync(null);
      CollectionAssert.AreEqual(new[] { "2 result windows are about to be closed" }, dialogs.Asked);
      Assert.AreEqual(2, vm.ResultsWindows.Count);
      Assert.AreEqual(1, main.OpenResultsWindows.Count);
      // "OK": all closed
      dialogs.Answer = true;
      await vm.CloseResultsWindowsCommand.ExecuteAsync(null);
      Pump();
      Assert.AreEqual(0, main.OpenResultsWindows.Count);
      Assert.AreEqual(0, vm.ResultsWindows.Count);
      Assert.IsFalse(close.Command.CanExecute(null));

      // closing the main window while a results window is open: the same question first
      vm.AddResultsWindowCommand.Execute(null);
      vm.Undock(vm.ResultsWindows.Single());
      Pump();
      dialogs.Asked.Clear();
      dialogs.Answer = false;
      main.Close();
      Pump();
      await Task.Delay(50);
      Pump();
      CollectionAssert.AreEqual(new[] { "1 result window is about to be closed" }, dialogs.Asked);
      Assert.IsTrue(main.IsVisible, "the dialog closed: the main window stays");
      Assert.AreEqual(1, main.OpenResultsWindows.Count);
      // the dialog itself: only "OK" (closing it is the way back)
      var ask = new ConfirmWindow("Close windows", "2 result windows are about to be closed", yes: "OK", cancel: false);
      ask.Show(main);
      Pump();
      var buttons = ask.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string).ToList();
      CollectionAssert.AreEqual(new[] { "OK" }, buttons);
      ask.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-results-closewindows-dialog.png"));
      ask.Close();
      dialogs.Answer = true;
      main.Close();
      Pump();
      await Task.Delay(50);
      Pump();
      Assert.AreEqual(0, main.OpenResultsWindows.Count, "OK: the results windows close with it");
      Assert.IsFalse(main.IsVisible);
      return true;
    }, CancellationToken.None);
  }
}
