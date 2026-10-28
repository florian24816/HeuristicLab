using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>
/// Several items at once - Ctrl+click, Shift+click, press and sweep - shown with inverted colors: entries of the
/// "Delete runs" dialog, sources of the Results tab (then ticked or unticked together), blocks of the
/// Experiments tab (moved together; a rectangle on empty space picks them).
/// </summary>
[TestClass]
public class StudioMultiSelectTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  private static Point Center(Control control, TopLevel top) =>
    control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;

  private static void Click(Window window, Point at, RawInputModifiers keys = RawInputModifiers.None) {
    window.MouseDown(at, MouseButton.Left, keys);
    window.MouseUp(at, MouseButton.Left, keys);
    Pump();
  }

  private static void Sweep(Window window, Point from, Point to) {
    window.MouseDown(from, MouseButton.Left);
    window.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), RawInputModifiers.LeftMouseButton);
    window.MouseMove(to, RawInputModifiers.LeftMouseButton);
    window.MouseUp(to, MouseButton.Left);
    Pump();
  }

  private static Color Fill(Border border) => ((ISolidColorBrush)border.Background!).Color;

  private static List<Border> SelectRows(Visual root) =>
    root.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("selectrow") && b.IsEffectivelyVisible)
      .OrderBy(b => b.TranslatePoint(default, (Visual)root)!.Value.Y).ToList();

  // every test of the class has a timeout: a test without one before a test with one hangs the headless session
  [TestMethod]
  [Timeout(300_000)]
  public async Task TheDeleteDialogTakesSeveralEntries() {
    await session.Dispatch(() => {
      // the dialog over a window, as in Studio
      var host = new Window { Width = 800, Height = 600 };
      host.Show();
      var chooser = new ChooseWindow("Delete runs", "Choose.", ["A", "B", "C", "D", "E"], "Delete runs");
      chooser.Show(host);
      Pump();
      var rows = SelectRows(chooser);
      Assert.AreEqual(5, rows.Count);
      var delete = chooser.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Delete runs");
      Assert.IsFalse(delete.IsEnabled);

      // press on A and sweep down to C; Ctrl+click adds E and takes B out again
      Sweep(chooser, Center(rows[0], chooser), Center(rows[2], chooser));
      CollectionAssert.AreEqual(new[] { true, true, true, false, false }, chooser.Rows.Select(r => r.IsSelected).ToList());
      Click(chooser, Center(rows[4], chooser), RawInputModifiers.Control);
      Click(chooser, Center(rows[1], chooser), RawInputModifiers.Control);
      CollectionAssert.AreEqual(new[] { true, false, true, false, true }, chooser.Rows.Select(r => r.IsSelected).ToList());
      Assert.IsTrue(delete.IsEnabled);
      // Shift+click: from the last one clicked (B) to D
      Click(chooser, Center(rows[3], chooser), RawInputModifiers.Shift);
      CollectionAssert.AreEqual(new[] { false, true, true, true, false }, chooser.Rows.Select(r => r.IsSelected).ToList());

      // inverted colors: dark rows with light writing in light mode, light rows with dark writing in dark mode
      Assert.AreEqual(Color.Parse("#1E1E1E"), Fill(rows[1]));
      Assert.AreEqual(Colors.White, ((ISolidColorBrush)((TextBlock)rows[1].Child!).Foreground!).Color);
      Assert.AreNotEqual(Color.Parse("#1E1E1E"), Fill(rows[0]), "not selected: not inverted");
      chooser.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-multiselect-dialog-light.png"));
      Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
      Pump();
      Assert.AreEqual(Color.Parse("#F2F2F2"), Fill(rows[1]));
      Assert.AreEqual(Color.Parse("#1E1E1E"), ((ISolidColorBrush)((TextBlock)rows[1].Child!).Foreground!).Color);
      chooser.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-multiselect-dialog-dark.png"));
      Application.Current.RequestedThemeVariant = ThemeVariant.Light;
      chooser.Close();
      host.Close();
      return true;
    }, CancellationToken.None);
  }

  private sealed class Dialogs : HeuristicLab.Studio.Services.IDialogService {
    public IReadOnlyList<int>? Answers { get; set; }
    public Task<SetupResult?> NewSetupAsync() => Task.FromResult<SetupResult?>(null);
    public Task<CatalogEntry?> PickAlgorithmAsync() => Task.FromResult<CatalogEntry?>(null);
    public Task<IProblem?> PickProblemAsync(IAlgorithm algorithm) => Task.FromResult<IProblem?>(null);
    public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
    public Task<IReadOnlyList<int>?> ChooseAsync(string title, string message, IReadOnlyList<string> items, string action) => Task.FromResult(Answers);
  }

  private static IProblem Tsp() =>
    (IProblem)Setups.Create(new SetupRequest("GeneticAlgorithm") { Problem = "TravelingSalesmanProblem", Instance = "ch130" }).Algorithm.Problem!.Clone();

  [TestMethod]
  [Timeout(300_000)]
  public async Task SeveralSourcesAndBlocksAtOnce() {
    await session.Dispatch(async () => {
      var dialogs = new Dialogs();
      var vm = new MainViewModel(null, dialogs);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
      window.Show();
      var first = ws.NewExperiment();
      var ga = Catalog.Find(Catalog.Algorithms(), "GeneticAlgorithm")!;
      var a = ws.AddAlgorithm(first, ga);
      var b = ws.AddAlgorithm(first, ga);
      var c = ws.AddAlgorithm(first, ga);
      var second = ws.NewExperiment();
      var d = ws.AddAlgorithm(second, ga);
      foreach (var block in new[] { a, b, c, d }) {
        ws.SetProblem(block, Tsp());
        ParameterEditor.Apply(block.Algorithm, ["PopulationSize=10", "MaximumGenerations=2"]);
      }
      a.Name = "A"; b.Name = "B"; c.Name = "C"; d.Name = "D";
      await ws.StartAllCommand.ExecuteAsync(null);
      ws.Selected = null;
      Pump();
      // ---- "Delete runs": two entries at once
      dialogs.Answers = [0, 1];
      await ws.DeleteRunsAsync();
      Assert.AreEqual(0, first.Experiment.Runs.Count);
      Assert.AreEqual(0, second.Experiment.Runs.Count, "both experiments' runs deleted in one go");
      await ws.StartAllCommand.ExecuteAsync(null);
      // ---- Experiments tab: Ctrl+click picks blocks (inverted name fields), dragging one moves them all
      Border Name(BlockViewModel block) => window.GetVisualDescendants().OfType<Border>()
        .First(x => x.Classes.Contains("name") && x.DataContext == block && x.IsEffectivelyVisible);
      Click(window, Center(Name(a), window), RawInputModifiers.Control);
      Click(window, Center(Name(c), window), RawInputModifiers.Control);
      Assert.IsTrue(a.IsPicked && c.IsPicked && !b.IsPicked);
      Assert.AreEqual(Color.Parse("#1E1E1E"), Fill(Name(a)), "picked: the name field inverted");
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-multiselect-blocks.png"));
      Assert.IsTrue(ws.BeginDrag(a));
      ws.PreviewDrop(new DropTarget(second, 1));
      Assert.IsTrue(a.IsMoving && c.IsMoving, "both leave their places while dragged");
      Assert.IsTrue(ws.EndDrag(drop: true));
      CollectionAssert.AreEqual(new[] { "D", "A", "C" }, second.Children.Select(x => x.Name).ToList(), "moved together, in their order");
      CollectionAssert.AreEqual(new[] { "B" }, first.Children.Select(x => x.Name).ToList());
      Assert.AreEqual(3, second.Experiment.Optimizers.Count, "in HeuristicLab's experiment too");
      Pump();
      // a click on a block without Ctrl picks nothing more; a rectangle on empty space picks what it covers
      Click(window, Center(Name(b), window));
      Assert.IsFalse(ws.AllBlocks().Any(x => x.IsPicked));
      var rowA = Name(a);
      var rowC = Name(c);
      var area = window.GetVisualDescendants().OfType<Border>().First(x => x.Classes.Contains("experiment") && x.DataContext == second);
      var left = area.TranslatePoint(new Point(4, 0), window)!.Value.X;
      var top = rowA.TranslatePoint(new Point(0, -2), window)!.Value.Y;
      var bottom = rowC.TranslatePoint(new Point(rowC.Bounds.Width + 30, rowC.Bounds.Height + 2), window)!.Value;
      Sweep(window, new Point(left, top), bottom);
      CollectionAssert.AreEquivalent(new[] { "A", "C" }, ws.AllBlocks().Where(x => x.IsPicked).Select(x => x.Name).ToList());
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-multiselect-band.png"));
      ws.ClearPicks();
      // ---- Results tab: select sources (click, Ctrl+click), then tick / untick / only them
      vm.SelectedTab = MainViewModel.ResultsTab;
      var analysis = vm.RunAnalysis;
      analysis.RefreshSources();
      Pump();
      Border Row(string label) => SelectRows(window).First(r => r.DataContext is RunSourceViewModel s && s.Name == label);
      Click(window, Center(Row(first.Name), window));
      Click(window, Center(Row(second.Name), window), RawInputModifiers.Control);
      var selected = analysis.AllSourceNodes.Where(s => s.IsSelected).Select(s => s.Name).ToList();
      CollectionAssert.AreEquivalent(new[] { first.Name, second.Name }, selected);
      Assert.AreEqual(Color.Parse("#1E1E1E"), Fill(Row(first.Name)), "selected sources inverted");
      Assert.IsTrue(analysis.OnlySelectedCommand.CanExecute(null));
      analysis.TickSelectedCommand.Execute(null);
      Assert.AreEqual(true, analysis.Sources.First(s => s.Name == first.Name).IsChecked);
      Assert.AreEqual(true, analysis.Sources.First(s => s.Name == second.Name).IsChecked);
      int both = analysis.Table.Rows.Count;
      Assert.IsTrue(both > 0);
      // only the second experiment: select it alone, "Only selected"
      Click(window, Center(Row(second.Name), window));
      analysis.OnlySelectedCommand.Execute(null);
      Assert.AreEqual(false, analysis.Sources.First(s => s.Name == first.Name).IsChecked);
      Assert.AreEqual(true, analysis.Sources.First(s => s.Name == second.Name).IsChecked);
      Assert.IsTrue(analysis.Table.Rows.Count < both);
      analysis.UntickSelectedCommand.Execute(null);
      Assert.AreEqual(0, analysis.Table.Rows.Count);
      // the tick box still ticks, without changing what is selected
      var box = window.GetVisualDescendants().OfType<CheckBox>().First(x => x.DataContext is RunSourceViewModel s && s.Name == first.Name && x.IsEffectivelyVisible);
      Click(window, Center(box, window));
      Assert.AreEqual(true, analysis.Sources.First(s => s.Name == first.Name).IsChecked);
      Assert.IsTrue(analysis.Sources.First(s => s.Name == second.Name).IsSelected);
      Assert.IsFalse(analysis.Sources.First(s => s.Name == first.Name).IsSelected);
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-multiselect-results.png"));
      window.Close();
      return true;
    }, CancellationToken.None);
  }
}
