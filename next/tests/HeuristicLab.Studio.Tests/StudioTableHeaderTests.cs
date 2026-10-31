using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;
using HeuristicLab.Studio.Controls;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>
/// The Results table's columns: a double-click on a title sorts (ascending, descending, not sorted - one column at a
/// time), a click on a column's right edge expands it (up to 120 characters) or makes it regular again,
/// Source/Origin/Run have one orange button collapsing them to its size; the sorted column is framed orange to black;
/// "Reload table" reads the runs again, not sorted, every column regular.
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
      int clicks = 0;
      void DoubleClick(string key) {
        var title = Title(key);
        // somewhere else on the title each time: two double-clicks in one place would count as more clicks
        var at = title.TranslatePoint(new Point(8 + 6 * (clicks++ % 4), title.Bounds.Height / 2), window)!.Value;
        for (int i = 0; i < 2; i++) {
          window.MouseDown(at, MouseButton.Left);
          window.MouseUp(at, MouseButton.Left);
        }
        Pump();
      }
      List<Border> Edges(string key) {
        int index = analysis.Grid!.Views.ToList().FindIndex(v => v.Key == key);
        return View().GetVisualDescendants().OfType<Border>()
          .Where(b => b.Classes.Contains("columnedge") && b.DataContext is GridCell cell && cell.Column == analysis.Grid.Views[index] && b.IsEffectivelyVisible).ToList();
      }
      int edgeClicks = 0;
      void ClickEdge(string key) {
        // the title's edge and the rows' edges in turn (all of them work; one place twice would be a double-click)
        var edges = Edges(key).OrderBy(e => e.TranslatePoint(default, window)!.Value.Y).ToList();
        var edge = edges[edgeClicks++ % edges.Count];
        var at = edge.TranslatePoint(new Point(edge.Bounds.Width / 2, edge.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Pump();
      }
      const string quality = "result:BestQuality";

      // not sorted = the order the runs were read in: as in the Run column
      CollectionAssert.AreEqual(new[] { "Run 2", "Run 3", new string('W', 150), "Run 5", "Run 6" }, order.Skip(1).Select(r => r.Name).ToList());

      // titles: only Source/Origin/Run have a button (orange, collapse_table); the others none
      Assert.IsTrue(analysis.Grid!.HeaderButtons);
      foreach (var key in new[] { "source", "origin", "run" }) {
        var button = Buttons(key).Single();
        Assert.AreEqual(Color.Parse("#F7931D"), Face(button));
        Assert.AreEqual("collapse_table", button.GetVisualDescendants().OfType<Pictogram>().Single().Symbol);
        Assert.IsTrue(button.Bounds.Width == 18 && button.Bounds.Height == 18);
      }
      Assert.AreEqual(0, Buttons(quality).Count, "no buttons on the other columns");

      // a double-click on a title: ascending (framed orange at the top to black at the bottom), descending, not sorted
      DoubleClick(quality);
      Assert.AreEqual(SortOrder.Ascending, Column(quality).Sort);
      var best = analysis.Rows.Select(r => r.Row.Number(quality)!.Value).ToList();
      CollectionAssert.AreEqual(best.Order().ToList(), best);
      var grid = analysis.Grid!;
      int q = grid.Views.ToList().FindIndex(v => v.Key == quality);
      Assert.AreEqual(TableGridViewModel.FrameOrange, ((IGradientBrush)grid.Header[q].FrameBrush!).GradientStops[0].Color, "orange at the top");
      Assert.AreEqual(new Thickness(3, 3, 3, 0), grid.Header[q].FrameThickness);
      var last = grid.ShownRows[^1].Cells[q];
      Assert.AreEqual(new Thickness(3, 0, 3, 3), last.FrameThickness, "the bottom line under the last row");
      Assert.AreEqual(TableGridViewModel.FrameBlack, ((IGradientBrush)last.FrameBrush!).GradientStops[1].Color, "black at the bottom");
      Assert.IsTrue(grid.Rows[0].Cells.Where((_, i) => i != q).All(c => c.FrameBrush == null), "only the sorted column");
      window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(AppContext.BaseDirectory, "studio-table-sorted-ascending.png"));
      DoubleClick(quality);
      Assert.AreEqual(SortOrder.Descending, Column(quality).Sort);
      Assert.AreEqual(TableGridViewModel.FrameBlack, ((IGradientBrush)analysis.Grid!.Header[q].FrameBrush!).GradientStops[0].Color, "black at the top");
      best = analysis.Rows.Select(r => r.Row.Number(quality)!.Value).ToList();
      CollectionAssert.AreEqual(best.OrderDescending().ToList(), best);
      window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(AppContext.BaseDirectory, "studio-table-sorted-descending.png"));
      DoubleClick(quality);
      Assert.AreEqual(SortOrder.None, Column(quality).Sort);
      CollectionAssert.AreEqual(order, analysis.Rows.Select(r => r.Row.Run).ToList(), "the order the runs were read in again");
      Assert.IsTrue(analysis.Grid!.Header.All(c => c.FrameBrush == null));

      // a fixed column sorts and is framed too; one column at a time
      DoubleClick(quality);
      DoubleClick("run");
      Assert.AreEqual(SortOrder.Ascending, Column("run").Sort);
      Assert.AreEqual(SortOrder.None, Column(quality).Sort, "one column at a time");
      int r = analysis.Grid!.Views.ToList().FindIndex(v => v.Key == "run");
      Assert.IsNotNull(analysis.Grid.Header[r].FrameBrush, "Source/Origin/Run are framed too");
      window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(AppContext.BaseDirectory, "studio-table-sorted-run.png"));
      DoubleClick("run");
      DoubleClick("run");
      Assert.AreEqual(SortOrder.None, Column("run").Sort);

      // the right edge: the pointer <-|-> (expand), a click expands - on the title or any row; then ->|<- and back
      Assert.IsTrue(Edges(quality).Count > 2, "the title's and every row's edge");
      Assert.AreSame(ColumnCursors.Expand, Edges(quality)[0].Cursor);
      ClickEdge(quality);
      Assert.AreEqual(ColumnSize.Expanded, Column(quality).Size);
      Assert.AreSame(ColumnCursors.Shrink, Edges(quality)[0].Cursor);
      Assert.IsFalse(analysis.Grid!.Rows.Any(x => x.IsSelected), "a click on an edge selects no row");
      ClickEdge(quality);
      Assert.AreEqual(ColumnSize.Regular, Column(quality).Size);
      Assert.AreEqual(150, Title(quality).Bounds.Width, 0.5);
      ClickEdge(quality);
      ClickEdge(quality);
      Assert.AreEqual(ColumnSize.Regular, Column(quality).Size, "a row's edge works as the title's");

      // Run: the button collapses it to the button's size (expand_table then), the button makes it regular again
      Assert.AreEqual(150, Title("run").Bounds.Width, 0.5);
      Click(Buttons("run").Single());
      Assert.AreEqual(ColumnSize.Collapsed, Column("run").Size);
      Assert.AreEqual(TableGridViewModel.CollapsedWidth, Title("run").Bounds.Width, 0.5);
      var collapsedButton = Buttons("run").Single();
      Assert.AreEqual("expand_table", collapsedButton.GetVisualDescendants().OfType<Pictogram>().Single().Symbol);
      Assert.IsTrue(collapsedButton.TranslatePoint(new Point(18, 0), Title("run"))!.Value.X <= TableGridViewModel.CollapsedWidth, "the button fits");
      Assert.IsFalse(Title("run").GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Run" && t.IsEffectivelyVisible), "no title");
      window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(AppContext.BaseDirectory, "studio-table-run-collapsed.png"));
      Click(collapsedButton);
      Assert.AreEqual(ColumnSize.Regular, Column("run").Size);
      Assert.AreEqual("collapse_table", Buttons("run").Single().GetVisualDescendants().OfType<Pictogram>().Single().Symbol);
      // collapsed, then its edge: expanded (up to 120 characters); the edge again: regular
      Click(Buttons("run").Single());
      ClickEdge("run");
      Assert.AreEqual(ColumnSize.Expanded, Column("run").Size);
      double wide = Title("run").Bounds.Width;
      double Natural(string text) {
        using var layout = new Avalonia.Media.TextFormatting.TextLayout(text, new Typeface(FontFamily.Default), 14, null);
        return layout.WidthIncludingTrailingWhitespace;
      }
      Assert.IsTrue(wide > Natural(new string('W', 118)) && wide < Natural(new string('W', 125)), $"as wide as 120 characters: {wide}");
      Assert.AreEqual("collapse_table", Buttons("run").Single().GetVisualDescendants().OfType<Pictogram>().Single().Symbol);
      window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(AppContext.BaseDirectory, "studio-table-run-expanded.png"));
      // the sizes stay when the table is made again (sorted; the other titles are out of view now)
      DoubleClick("source");
      Assert.AreEqual(SortOrder.Ascending, Column("source").Sort);
      Assert.AreEqual(ColumnSize.Expanded, Column("run").Size);
      Assert.AreEqual(wide, Title("run").Bounds.Width, 0.5);
      // wider than the view, its right edge is out of reach: the button collapses it, the button again makes it regular
      Click(Buttons("run").Single());
      Click(Buttons("run").Single());
      Assert.AreEqual(ColumnSize.Regular, Column("run").Size);
      // a fixed column that fits: its edge expands it and makes it regular again
      ClickEdge("origin");
      Assert.AreEqual(ColumnSize.Expanded, Column("origin").Size);
      ClickEdge("origin");
      Assert.AreEqual(ColumnSize.Regular, Column("origin").Size);

      // "Reload table": not sorted, every column regular, the runs in the order they were read
      ClickEdge(quality);
      Click(Buttons("origin").Single());
      Assert.AreEqual(SortOrder.Ascending, Column("source").Sort);
      Assert.AreEqual(ColumnSize.Expanded, Column(quality).Size);
      Assert.AreEqual(ColumnSize.Collapsed, Column("origin").Size);
      var reload = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Reload table" && b.IsEffectivelyVisible);
      Click(reload);
      Assert.IsTrue(analysis.Grid!.Views.All(v => v.Sort == SortOrder.None && v.Size == ColumnSize.Regular));
      CollectionAssert.AreEqual(order, analysis.Rows.Select(x => x.Row.Run).ToList());
      window.Close();
      return true;
    }, CancellationToken.None);
  }
}
