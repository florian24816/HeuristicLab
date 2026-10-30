using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
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
/// The Results table's column titles: a sort button (none, ascending, descending - one column at a time; a double-click
/// on the title does the same) and size buttons (Source/Origin/Run: collapse and expand, three sizes, the logo orange;
/// the others: one switch, regular or expanded up to 120 characters); the sorted column framed green to red.
/// </summary>
[TestClass]
public class StudioTableHeaderTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  private static Color Face(Button button) => ((ISolidColorBrush)button.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("face")).Background!).Color;

  [TestMethod]
  [Timeout(600_000)]
  public async Task TitlesSortAndResizeTheirColumns() {
    await session.Dispatch(async () => {
      var vm = new MainViewModel(null, null);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1500, Height = 960 };
      window.Show();
      var experiment = ws.NewExperiment();
      var ga = ws.AddAlgorithm(experiment, Catalog.Find(Catalog.Algorithms(), "GeneticAlgorithm")!);
      ws.SetProblem(ga, (IProblem)Setups.Create(new SetupRequest("GeneticAlgorithm") { Problem = "TravelingSalesmanProblem", Instance = "ch130" }).Algorithm.Problem!.Clone());
      ParameterEditor.Apply(ga.Algorithm, ["PopulationSize=10", "MaximumGenerations=2"]);
      await ws.StartAllCommand.ExecuteAsync(null);
      // six runs, one with a name of 150 characters
      var run = ga.Algorithm.Runs.Single();
      for (int i = 1; i < 6; i++) {
        var copy = (IRun)run.Clone();
        copy.Name = i == 3 ? new string('W', 150) : $"Run {i + 1}";
        copy.Results["BestQuality"] = new HeuristicLab.Data.DoubleValue(1000 * i);
        ga.Algorithm.Runs.Add(copy);
      }
      vm.SelectedTab = MainViewModel.ResultsTab;
      var analysis = vm.RunAnalysis;
      analysis.RefreshSources();
      analysis.Sources.First().IsChecked = true;
      analysis.SelectedView = RunAnalysisViewModel.TableView;
      Pump();
      var order = analysis.Rows.Select(r => r.Row.Run).ToList();

      TableGridView View() => window.GetVisualDescendants().OfType<TableGridView>().First(v => v.IsEffectivelyVisible);
      Control Title(string key) {
        var grid = analysis.Grid!;
        int index = grid.Views.ToList().FindIndex(v => v.Key == key);
        return View().GetVisualDescendants().OfType<Control>().Where(c => c.DataContext is GridCell { Row: null } cell && cell == grid.Header[index] && c.Classes.Contains("cell")).First();
      }
      List<Button> Buttons(string key) => Title(key).GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
      GridColumnView Column(string key) => analysis.Grid!.Views.Single(v => v.Key == key);
      void Click(Button b) {
        var at = b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Pump();
      }
      Button SortButton(string key) => Buttons(key).Single(b => b.Classes.Contains("sort"));

      // titles: Source/Origin/Run sort, collapse, expand - orange; the others sort and a size switch
      Assert.IsTrue(analysis.Grid!.HeaderButtons);
      CollectionAssert.AreEqual(new[] { "sort", "narrow", "widen" }, Buttons("run").Select(b => b.Classes.First(c => c is "sort" or "narrow" or "widen")).ToList());
      Assert.IsTrue(Buttons("run").All(b => Face(b) == Color.Parse("#F7931D")));
      const string quality = "result:BestQuality";
      CollectionAssert.AreEqual(new[] { "sort", "size" }, Buttons(quality).Select(b => b.Classes.First(c => c is "sort" or "size")).ToList());
      Assert.IsTrue(Buttons(quality).All(b => b.Bounds.Width == 18 && b.Bounds.Height == 18 && b.CornerRadius == default), "square");
      Assert.AreEqual(Color.Parse("#F2F2F2"), Face(SortButton(quality)), "not sorted");

      // sort button: ascending (green), framed green at the top to red at the bottom
      Click(SortButton(quality));
      Assert.AreEqual(SortOrder.Ascending, Column(quality).Sort);
      var best = analysis.Rows.Select(r => r.Row.Number(quality)!.Value).ToList();
      CollectionAssert.AreEqual(best.Order().ToList(), best);
      Assert.AreEqual(TableGridViewModel.FrameGreen, Face(SortButton(quality)));
      var grid = analysis.Grid!;
      int q = grid.Views.ToList().FindIndex(v => v.Key == quality);
      var top = (IGradientBrush)grid.Header[q].FrameBrush!;
      Assert.AreEqual(TableGridViewModel.FrameGreen, top.GradientStops[0].Color, "green at the top");
      Assert.AreEqual(new Thickness(3, 3, 3, 0), grid.Header[q].FrameThickness);
      var last = grid.ShownRows[^1].Cells[q];
      Assert.AreEqual(new Thickness(3, 0, 3, 3), last.FrameThickness, "the bottom line under the last row");
      Assert.AreEqual(TableGridViewModel.FrameRed, ((IGradientBrush)last.FrameBrush!).GradientStops[1].Color, "red at the bottom");
      Assert.IsTrue(grid.Views.Where(v => v.Key != quality).All(v => v.Sort == SortOrder.None));
      Assert.IsTrue(grid.Rows[0].Cells.Where((_, i) => i != q).All(c => c.FrameBrush == null), "only the sorted column");
      Pump();
      window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(AppContext.BaseDirectory, "studio-table-sorted-ascending.png"));
      // descending (red): red at the top
      Click(SortButton(quality));
      Assert.AreEqual(SortOrder.Descending, Column(quality).Sort);
      Assert.AreEqual(TableGridViewModel.FrameRed, Face(SortButton(quality)));
      Assert.AreEqual(TableGridViewModel.FrameRed, ((IGradientBrush)analysis.Grid!.Header[q].FrameBrush!).GradientStops[0].Color);
      best = analysis.Rows.Select(r => r.Row.Number(quality)!.Value).ToList();
      CollectionAssert.AreEqual(best.OrderDescending().ToList(), best);
      window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(AppContext.BaseDirectory, "studio-table-sorted-descending.png"));
      // not sorted: the order before, no frame
      Click(SortButton(quality));
      Assert.AreEqual(SortOrder.None, Column(quality).Sort);
      CollectionAssert.AreEqual(order, analysis.Rows.Select(r => r.Row.Run).ToList());
      Assert.IsTrue(analysis.Grid!.Header.All(c => c.FrameBrush == null));

      // a double-click on the title sorts too; another column's sorting ends the first one's
      var title = Title(quality);
      var at = title.TranslatePoint(new Point(20, title.Bounds.Height / 2), window)!.Value;
      window.MouseDown(at, MouseButton.Left);
      window.MouseUp(at, MouseButton.Left);
      window.MouseDown(at, MouseButton.Left);
      window.MouseUp(at, MouseButton.Left);
      Pump();
      Assert.AreEqual(SortOrder.Ascending, Column(quality).Sort, "double-click on the title");
      Click(SortButton("run"));
      Assert.AreEqual(SortOrder.Ascending, Column("run").Sort);
      Assert.AreEqual(SortOrder.None, Column(quality).Sort, "one column at a time");
      Assert.IsTrue(analysis.Grid!.Header.All(c => c.FrameBrush == null), "a fixed column is not framed");
      Assert.AreEqual(Color.Parse("#B85F00"), Face(SortButton("run")), "orange, darker while sorting");
      Click(SortButton("run"));
      Click(SortButton("run"));
      Assert.AreEqual(SortOrder.None, Column("run").Sort);

      // Run: collapse (two buttons, no title), expand (regular, three), expand (120 characters, expand greyed out)
      Assert.AreEqual(150, Title("run").Bounds.Width, 0.5);
      Click(Buttons("run").Single(b => b.Classes.Contains("narrow")));
      Assert.AreEqual(TableGridViewModel.CollapsedWidth, Title("run").Bounds.Width, 0.5);
      CollectionAssert.AreEqual(new[] { "sort", "widen" }, Buttons("run").Select(b => b.Classes.First(c => c is "sort" or "narrow" or "widen")).ToList());
      Assert.IsFalse(Title("run").GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Run" && t.IsEffectivelyVisible), "no room for the title");
      Assert.IsTrue(Buttons("run").All(b => b.TranslatePoint(new Point(b.Bounds.Width, 0), Title("run"))!.Value.X <= TableGridViewModel.CollapsedWidth), "the buttons fit");
      window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(AppContext.BaseDirectory, "studio-table-run-collapsed.png"));
      Click(Buttons("run").Single(b => b.Classes.Contains("widen")));
      Assert.AreEqual(150, Title("run").Bounds.Width, 0.5);
      Assert.AreEqual(3, Buttons("run").Count);
      Assert.IsTrue(Buttons("run").All(b => b.IsEffectivelyEnabled));
      Click(Buttons("run").Single(b => b.Classes.Contains("widen")));
      double wide = Title("run").Bounds.Width;
      double Natural(string text) {
        using var layout = new Avalonia.Media.TextFormatting.TextLayout(text, new Typeface(FontFamily.Default), 14, null);
        return layout.WidthIncludingTrailingWhitespace;
      }
      Assert.IsTrue(wide > Natural(new string('W', 118)) && wide < Natural(new string('W', 125)), $"as wide as 120 characters: {wide}");
      var expand = Buttons("run").Single(b => b.Classes.Contains("widen"));
      Assert.IsFalse(expand.IsEffectivelyEnabled);
      Assert.AreEqual(Color.Parse("#D0D0D0"), Face(expand), "greyed out");
      // the sizes stay when the table is made again (sorted)
      Click(SortButton(quality));
      Assert.AreEqual(ColumnSize.Expanded, Column("run").Size);
      Assert.AreEqual(wide, Title("run").Bounds.Width, 0.5);
      window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(AppContext.BaseDirectory, "studio-table-run-expanded.png"));
      Click(Buttons("run").Single(b => b.Classes.Contains("narrow")));
      Assert.AreEqual(150, Title("run").Bounds.Width, 0.5);

      // a column that is not fixed: one switch, regular or expanded (dark while expanded)
      var size = Buttons(quality).Single(b => b.Classes.Contains("size"));
      Click(size);
      Assert.AreEqual(ColumnSize.Expanded, Column(quality).Size);
      Assert.AreEqual(Color.Parse("#4A4A4A"), Face(Buttons(quality).Single(b => b.Classes.Contains("size"))));
      Assert.IsTrue(Title(quality).Bounds.Width >= 150);
      Click(Buttons(quality).Single(b => b.Classes.Contains("size")));
      Assert.AreEqual(ColumnSize.Regular, Column(quality).Size);
      Assert.AreEqual(150, Title(quality).Bounds.Width, 0.5);
      window.Close();
      return true;
    }, CancellationToken.None);
  }
}
