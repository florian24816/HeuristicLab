using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>A block menu closed without choosing anything (a click elsewhere) leaves the Experiments tab where it was.</summary>
[TestClass]
public class StudioMenuScrollTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  private static Point Center(Control control, TopLevel top) =>
    control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;

  [TestMethod]
  [Timeout(300_000)]
  public async Task ClosingAMenuWithoutChoosingKeepsTheScrollPosition() {
    await session.Dispatch(() => {
      var vm = new MainViewModel(null, null);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1200, Height = 600 };
      window.Show();
      var ga = Catalog.Find(Catalog.Algorithms(), "GeneticAlgorithm")!;
      var blocks = new List<AlgorithmBlockViewModel>();
      for (int e = 0; e < 4; e++) {
        var experiment = ws.NewExperiment();
        for (int i = 0; i < 4; i++) blocks.Add(ws.AddAlgorithm(experiment, ga));
      }
      ws.Selected = null;
      Pump();
      var tree = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "Tree");
      Assert.IsTrue(tree.Extent.Height > tree.Viewport.Height + 200, "the tab scrolls");
      // the last control focused: "+ New experiment / open..." at the bottom (used to make these experiments)
      var add = window.GetVisualDescendants().OfType<Button>().Last(b => b.Flyout != null && tree.IsVisualAncestorOf(b));
      tree.Offset = new Vector(0, 0);
      Pump();
      Border Name(BlockViewModel block) => window.GetVisualDescendants().OfType<Border>()
        .First(x => x.Classes.Contains("name") && x.DataContext == block && x.IsEffectivelyVisible);
      Border Block(BlockViewModel block) => window.GetVisualDescendants().OfType<Border>()
        .First(x => x.Classes.Contains("block") && x.DataContext == block);

      foreach (var multiple in new[] { false, true }) {
        if (multiple) {
          window.MouseDown(Center(Name(blocks[1]), window), MouseButton.Left, RawInputModifiers.Control);
          window.MouseUp(Center(Name(blocks[1]), window), MouseButton.Left, RawInputModifiers.Control);
          window.MouseDown(Center(Name(blocks[2]), window), MouseButton.Left, RawInputModifiers.Control);
          window.MouseUp(Center(Name(blocks[2]), window), MouseButton.Left, RawInputModifiers.Control);
          Pump();
          Assert.IsTrue(blocks[1].IsPicked && blocks[2].IsPicked);
        }
        // focused, the button is scrolled into view (the jump); back to the top while it keeps the focus
        Assert.IsTrue(add.Focus());
        Pump();
        Assert.IsTrue(tree.Offset.Y > 100, "focusing it scrolls down to it");
        tree.Offset = new Vector(0, 0);
        Pump();
        var before = tree.Offset;
        var at = Center(Name(blocks[2]), window);
        window.MouseDown(at, MouseButton.Right);
        // closed without choosing, the menu gives the focus back to the control focused before it
        Assert.AreSame(tree, window.FocusManager!.GetFocusedElement(), "the tab's scroll area has the focus, not the button");
        window.MouseUp(at, MouseButton.Right);
        Pump();
        Assert.IsTrue(Block(blocks[2]).ContextMenu!.IsOpen);
        // a click elsewhere (empty space right of the blocks) closes it without choosing
        var empty = new Point(window.Width - 520, at.Y + 4);
        window.MouseDown(empty, MouseButton.Left);
        window.MouseUp(empty, MouseButton.Left);
        Pump();
        Assert.IsFalse(Block(blocks[2]).ContextMenu!.IsOpen);
        Assert.AreEqual(before.Y, tree.Offset.Y, 1, $"stays where it was (multiple: {multiple})");
      }
      window.Close();
      return true;
    }, CancellationToken.None);
  }
}
