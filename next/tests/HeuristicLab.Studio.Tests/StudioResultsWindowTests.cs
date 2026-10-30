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
/// bar ("HL-Results: n", changed live), ticked as the main tab; the selection state box: "All", "All w/o singles",
/// "Only singles", "None" (full orange when that is the state), "Custom" when none of them is, in a further tab
/// "Same as main results"; Refresh in a cut-out of the box, level with a further tab's name; rows reaching edge to edge.
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

      // the main tab: the experiment ticked, not the single runs - "All w/o singles" is full orange, in a box of its own
      var analysis = vm.RunAnalysis;
      analysis.Sources.First().IsChecked = true;
      Pump();
      Assert.IsTrue(analysis.AllButSingleTicked);
      Assert.AreEqual(Orange, Fill(Shortcut(main, "All w/o singles")));
      Assert.AreEqual(Pale, Fill(Shortcut(main, "All")));
      Assert.AreEqual(new CornerRadius(0), Shortcut(main, "All").CornerRadius, "square corners");
      var box = main.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("selectionstate") && b.Child is HeuristicLab.Studio.Controls.JustifiedWrapPanel && b.IsEffectivelyVisible);
      Assert.AreEqual(Color.Parse("#D5CFC6"), ((ISolidColorBrush)box.Background!).Color);
      // Refresh in a cut-out at the box's top left, "Selection state" beside it on the box's color
      var header = main.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("selectionstate") && b.Child is TextBlock { Text: "Selection state" } && b.IsEffectivelyVisible);
      var refresh = Shortcut(main, "Refresh");
      double Left(Visual v) => v.TranslatePoint(default, main)!.Value.X;
      double Top(Visual v) => v.TranslatePoint(default, main)!.Value.Y;
      Assert.AreEqual(Top(refresh), Top(header), 1, "level with Refresh");
      Assert.IsTrue(Left(header) > Left(refresh) + refresh.Bounds.Width, "right of Refresh");
      Assert.AreEqual(Top(header) + header.Bounds.Height, Top(box), 1, "one piece with the box below");
      Assert.AreEqual(Left(refresh), Left(box), 1, "the box reaches under Refresh");
      Assert.AreEqual(Left(header) + header.Bounds.Width, Left(box) + box.Bounds.Width, 1);
      // its rows of buttons reach from edge to edge, at any width of the source list
      void RowsJustified() {
        var cells = box.GetVisualDescendants().OfType<Control>().Where(c => (c.Classes.Contains("ticks") || c.Classes.Contains("tickstate")) && c.IsEffectivelyVisible).ToList();
        double right = cells.Max(c => Left(c) + c.Bounds.Width);
        foreach (var line in cells.GroupBy(c => Math.Round(Top(c))))
          Assert.AreEqual(right, line.Max(c => Left(c) + c.Bounds.Width), 1, $"a row ends at the right edge (row at {line.Key})");
        Assert.AreEqual(Left(box) + box.Bounds.Width - right, cells.Min(Left) - Left(box), 1, "as far from the right edge as from the left");
      }
      RowsJustified();
      var split = main.GetVisualDescendants().OfType<Grid>().First(g => g.ColumnDefinitions.Count == 3 && g.ColumnDefinitions[0].Width.Value == 280 && g.IsEffectivelyVisible);
      foreach (var width in new[] { 140, 420, 900 }) {
        split.ColumnDefinitions[0].Width = new GridLength(width);
        Pump();
        RowsJustified();
        main.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, $"studio-selection-state-{width}.png"));
      }
      split.ColumnDefinitions[0].Width = new GridLength(280);
      Pump();
      Assert.IsFalse(main.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Same as main results" && b.IsEffectivelyVisible),
        "only in further tabs");
      Border Custom() => box.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("tickstate"));
      Assert.AreEqual(Color.Parse("#E3E3E3"), ((ISolidColorBrush)Custom().Background!).Color, "grey: not the state, cannot be pressed");
      // low buttons: the text close to the edge, not touching it; Custom as high as the buttons
      var allButton = Shortcut(main, "All");
      var allText = allButton.GetVisualDescendants().OfType<TextBlock>().Single();
      double textTop = allText.TranslatePoint(default, allButton)!.Value.Y;
      Assert.IsTrue(textTop >= 1 && textTop <= 4, $"text {textTop:0.#} below the top edge");
      double textBelow = allButton.Bounds.Height - textTop - allText.Bounds.Height;
      Assert.IsTrue(textBelow >= 1 && textBelow <= 5, $"text {textBelow:0.#} above the bottom edge");
      Assert.AreEqual(allButton.Bounds.Height, Custom().Bounds.Height, 1);
      // only the single runs, then one of two experiment sources by hand: "Custom"
      Shortcut(main, "Only singles").Command!.Execute(null);
      Pump();
      Assert.IsTrue(analysis.OnlySinglesTicked);
      Assert.AreEqual(Orange, Fill(Shortcut(main, "Only singles")));
      Assert.AreEqual(1, analysis.Table.Rows.Count, "the single run");
      analysis.Sources.First().IsChecked = true;
      Pump();
      Assert.IsTrue(analysis.AllTicked);
      Assert.AreEqual(Orange, Fill(Shortcut(main, "All")));
      analysis.Sources.Single(s => s.Name == "Single runs").IsChecked = false;
      analysis.Sources.First().IsChecked = false;
      Pump();
      Assert.AreEqual(Orange, Fill(Shortcut(main, "None")));
      Assert.IsFalse(analysis.IsCustom);
      // a second algorithm (no runs yet): only the first one ticked is none of the states
      ws.AddAlgorithm(experiment, Catalog.Find(Catalog.Algorithms(), "TabuSearch")!);
      analysis.RefreshSources();
      analysis.Sources.First().Children.First().IsChecked = true;
      Pump();
      Assert.IsTrue(analysis.IsCustom);
      Assert.AreEqual(Orange, ((ISolidColorBrush)Custom().Background!).Color);
      Assert.IsFalse(new[] { "All", "All w/o singles", "Only singles", "None" }.Any(t => Fill(Shortcut(main, t)) == Orange));
      main.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-selection-state.png"));
      Shortcut(main, "All w/o singles").Command!.Execute(null);
      Pump();
      Assert.IsTrue(analysis.AllButSingleTicked);

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
      Assert.AreEqual(Orange, Fill(Shortcut(main, "Same as main results")));
      Assert.AreEqual(Orange, Fill(Shortcut(main, "All w/o singles")), "both: the main tab's ticks are All w/o singles");
      // the window's name, level with Refresh
      Assert.AreEqual(Top(Shortcut(main, "Refresh")), Top(main.GetVisualDescendants().OfType<TextBox>().Single(t => t.Classes.Contains("windowname") && t.IsEffectivelyVisible)), 1);
      // the main tab ticked by hand, the further tab the same: "Custom" and "Same as main results" both full orange
      analysis.Sources.First().Children.First().IsChecked = false;
      first.Analysis.CopySelectionFromMain();
      Pump();
      Assert.IsTrue(analysis.IsCustom);
      Assert.AreEqual(Orange, Fill(Shortcut(main, "Same as main results")));
      Assert.AreEqual(Orange, ((ISolidColorBrush)main.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("tickstate") && b.IsEffectivelyVisible).Background!).Color);
      Assert.IsTrue(Left(Shortcut(main, "Same as main results")) > Left(main.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("tickstate") && b.IsEffectivelyVisible))
                    || Top(Shortcut(main, "Same as main results")) > Top(main.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("tickstate") && b.IsEffectivelyVisible)), "last");
      main.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-selection-state-window.png"));
      vm.SelectResults(null);
      Pump();
      Shortcut(main, "All w/o singles").Command!.Execute(null);
      vm.SelectResults(first);
      first.Analysis.CopySelectionFromMain();
      Pump();
      // "Main results" is a rectangle (first), further tabs parallelograms with italic names and a small cross
      var mainTab = main.GetVisualDescendants().OfType<Panel>().Single(p => p.Classes.Contains("resultstab") && p.Classes.Contains("main"));
      Assert.IsFalse(Skewed(Shape(mainTab)));
      var tab = Tab(main, first);
      Assert.IsTrue(Skewed(Shape(tab)), "a parallelogram while docked");
      Assert.AreEqual(FontStyle.Italic, tab.GetVisualDescendants().OfType<TextBlock>().First().FontStyle);
      Assert.IsTrue(tab.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "✕"));
      Assert.IsTrue(mainTab.TranslatePoint(default, main)!.Value.X < tab.TranslatePoint(default, main)!.Value.X, "Main results first");
      Assert.AreEqual(Orange, ((ISolidColorBrush)Shape(tab).Background!).Color, "the selected tab");
      // its name, live in the tab
      var name = main.GetVisualDescendants().OfType<TextBox>().Single(t => t.Classes.Contains("windowname") && t.IsEffectivelyVisible);
      name.Text = "GA only";
      Pump();
      Assert.AreEqual("GA only", first.Name);
      Assert.IsTrue(tab.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "GA only"));
      main.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-results-tabs.png"));

      // its own selection: select all / unselect all / copy from the main tab
      var other = first.Analysis;
      Shortcut(main, "All").Command!.Execute(null);
      Pump();
      Assert.IsTrue(other.AllTicked);
      Assert.AreEqual(analysis.Table.Rows.Count + 1, other.Table.Rows.Count, "with the single run");
      Assert.AreEqual(1, analysis.Table.Rows.Count, "the main tab keeps its own selection");
      Assert.AreEqual(Pale, Fill(Shortcut(main, "Same as main results")));
      Shortcut(main, "Same as main results").Command!.Execute(null);
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
      // in the main Results tab only, pale red, square, shown only while results windows are open
      Assert.IsFalse(main.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Close all result windows" && b.IsEffectivelyVisible), "no windows yet");
      vm.AddResultsWindowCommand.Execute(null);
      vm.AddResultsWindowCommand.Execute(null);
      vm.Undock(vm.ResultsWindows[1]);
      vm.SelectResults(null);
      Pump();
      var close = Shortcut(main, "Close all result windows");  // the main tab's view is built again when it is shown again
      Assert.AreEqual(Color.Parse("#F6CACA"), Fill(close));
      Assert.AreEqual(new CornerRadius(0), close.CornerRadius);
      Assert.IsTrue(close.Command!.CanExecute(null));
      Assert.AreEqual(1, main.OpenResultsWindows.Count, "one docked tab, one window");
      Assert.IsFalse(main.OpenResultsWindows[0].GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Close all result windows" && b.IsEffectivelyVisible),
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
      Assert.IsFalse(close.IsEffectivelyVisible, "gone with the last window");

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
      var ask = new ConfirmWindow("Close all result windows", "2 result windows are about to be closed", yes: "OK", cancel: false);
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
