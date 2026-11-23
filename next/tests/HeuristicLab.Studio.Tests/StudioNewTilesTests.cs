using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>
/// New / open under the experiments (user's request 2026-11-13): tiles - a pale logo orange square with a pictogram and
/// the text below it, hovered the square full logo orange and the text field pale orange; "Open recent" shows its list
/// when hovered. No "Samples..." (they are offered on the right).
/// </summary>
[TestClass]
public class StudioNewTilesTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static string LegacyGaTsp => Path.Combine(AppContext.BaseDirectory, "TestData", "GA_TSP.hl");

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  private static Point Center(Control control, TopLevel top) =>
    control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;

  private static Color Fill(Border border) => ((ISolidColorBrush)border.Background!).Color;

  [TestMethod]
  [Timeout(300_000)]
  public async Task NewAndOpenAreTiles() {
    await session.Dispatch(async () => {
      var vm = new MainViewModel(null, null);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1200, Height = 700 };
      window.Show();
      Pump();
      List<Button> Tiles() => window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("newtile") && b.IsEffectivelyVisible).ToList();
      string Label(Button tile) => tile.GetVisualDescendants().OfType<TextBlock>().Single().Text!;
      Border Square(Button tile) => tile.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("tilesquare"));
      Border LabelBox(Button tile) => tile.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("tilelabel"));

      var tiles = Tiles();
      CollectionAssert.AreEqual(new[] { "New experiment", "New benchmark instance", "Open...", "Open recent" }, tiles.Select(Label).ToList());
      Assert.IsFalse(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "+ New experiment / open..."), "no list button any more");
      CollectionAssert.AreEqual(new[] { "experiment_logo", "Algorith_logo", "open_from_file", "open_recent" },
        tiles.Select(t => t.GetVisualDescendants().OfType<HeuristicLab.Studio.Controls.Pictogram>().Single().Symbol).ToList());
      foreach (var tile in tiles) {
        var square = Square(tile);
        Assert.AreEqual(LabelBox(tile).Bounds.Width, square.Bounds.Width, 0.5, "a rectangle as wide as the text field below it");
        Assert.AreEqual(64, square.Bounds.Height, 0.5);
        var picture = tile.GetVisualDescendants().OfType<HeuristicLab.Studio.Controls.Pictogram>().Single();
        Assert.AreEqual(36, picture.Bounds.Width, 0.5, "the pictogram at its first size");
        Assert.AreEqual(square.Bounds.Width / 2, picture.Bounds.X + picture.Bounds.Width / 2, 0.5, "in the middle");
        Assert.IsTrue(square.TranslatePoint(default, tile)!.Value.Y < LabelBox(tile).TranslatePoint(default, tile)!.Value.Y, "the text below it");
      }
      Assert.AreEqual(Color.Parse("#FDE5C8"), Fill(Square(tiles[0])), "pale logo orange");
      var plainLabel = Fill(LabelBox(tiles[0]));

      // hovered: the square full logo orange, the text field another color
      window.MouseMove(Center(Square(tiles[2]), window));
      Pump();
      Assert.AreEqual(Color.Parse("#F7931D"), Fill(Square(tiles[2])));
      Assert.AreNotEqual(plainLabel, Fill(LabelBox(tiles[2])));
      Assert.AreEqual(Color.Parse("#FDE5C8"), Fill(Square(tiles[0])), "the others stay as they are");
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-new-tiles.png"));

      // "Open recent": nothing recent - disabled; with a recent file its list shows when hovered, no click needed
      Assert.IsFalse(tiles[3].IsEffectivelyEnabled);
      await ws.OpenExperimentAsync(LegacyGaTsp);
      Pump();
      Assert.IsTrue(tiles[3].IsEffectivelyEnabled);
      window.MouseMove(new Point(2, 2));
      Pump();
      window.MouseMove(Center(Square(tiles[3]), window));
      Pump();
      var flyout = (HeuristicLab.Studio.Controls.HoverMenuFlyout)tiles[3].Flyout!;
      Assert.IsTrue(flyout.IsOpen, "shown when hovered");
      Assert.IsTrue(tiles[3].Classes.Contains("open"), "the tile keeps its hover look while the list is shown");
      Assert.AreEqual(Color.Parse("#F7931D"), Fill(Square(tiles[3])));
      // the pointer off the tile for an instant (that flickered: the list is a window of its own) and back: stays open
      var list = window.GetVisualDescendants().OfType<MenuItem>().First(m => m.Header is MenuChoice);
      window.MouseMove(new Point(2, 2));
      Pump();
      window.MouseMove(Center(Square(tiles[3]), window));
      Pump();
      flyout.CheckNow();
      Assert.IsTrue(flyout.IsOpen, "back within the grace period: still open, not opened again");
      // on the list: stays open
      window.MouseMove(Center(list, window));
      Pump();
      flyout.CheckNow();
      Assert.IsTrue(flyout.IsOpen, "over the list");
      Assert.AreEqual("GA_TSP.hl", ((MenuChoice)flyout.Items.Cast<object>().Single()).Display);
      Assert.IsTrue(window.GetVisualDescendants().OfType<MenuItem>().Any(m => m.HeaderTemplate != null && m.Header is MenuChoice { Display: "GA_TSP.hl" }),
        "the name in a TextBlock: its '_' is no access key");
      Assert.IsTrue(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "GA_TSP.hl" && t.IsEffectivelyVisible), "GA_TSP.hl, not GATSP.hl");
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-new-tiles-recent.png"));
      vm.IsDarkMode = true;
      Pump();
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-new-tiles-dark.png"));
      // off the tile and the list for the grace period: closed
      flyout.Hide();
      Pump();
      window.MouseMove(new Point(2, 2));
      Pump();
      window.MouseMove(Center(Square(tiles[3]), window));
      Pump();
      Assert.IsTrue(flyout.IsOpen, "hovered again: open again");
      window.MouseMove(new Point(2, 2));
      Pump();
      flyout.CheckNow();
      Assert.IsFalse(flyout.IsOpen, "closed once the pointer left both");
      Assert.IsFalse(tiles[3].Classes.Contains("open"));
      window.Close();
      return true;
    }, CancellationToken.None);
  }
}
