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

/// <summary>
/// Clicks on empty space: they leave a text field (any window), and in the Experiments tab they unselect the selected
/// block; the containers' add buttons are low; the eject dialog ticks by sweeping with the right mouse button held.
/// </summary>
[TestClass]
public class StudioEmptyClickTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  private static Point Center(Control c, Visual to) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), to)!.Value;

  private static void Click(Window window, Point at) {
    window.MouseDown(at, MouseButton.Left);
    window.MouseUp(at, MouseButton.Left);
    Pump();
  }

  [TestMethod]
  [Timeout(300_000)]
  public async Task EmptySpaceLeavesFieldsAndUnselectsBlocks() {
    await session.Dispatch(() => {
      var vm = new MainViewModel(null, null);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
      window.Show();
      var experiment = ws.NewExperiment();
      var ga = ws.AddAlgorithm(experiment, Catalog.Find(Catalog.Algorithms(), "GeneticAlgorithm")!);
      ws.Selected = experiment;
      Pump();
      var tree = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "Tree");
      var empty = tree.TranslatePoint(new Point(tree.Bounds.Width / 2, tree.Bounds.Height - 20), window)!.Value;
      Assert.IsFalse(window.GetVisualsAt(empty).Any(v => v is Button or TextBox), "empty space below the experiments");

      // a text field (the experiment's name in its detail): typing, then a click on empty space leaves it
      var field = window.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible && t.IsEffectivelyEnabled);
      field.Focus();
      Pump();
      Assert.AreSame(field, window.FocusManager!.GetFocusedElement());
      Click(window, empty);
      Assert.IsNotInstanceOfType(window.FocusManager!.GetFocusedElement(), typeof(TextBox), "the field is left");

      // a click on empty space unselects the selected block
      ws.Selected = ga;
      Pump();
      Assert.IsTrue(ga.IsSelected);
      Click(window, empty);
      Assert.IsNull(ws.Selected, "nothing selected");
      Assert.IsFalse(ga.IsSelected);

      // the containers' add buttons: low (2 px above and 3 below the text)
      var add = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("addblock") && b.IsEffectivelyVisible);
      Assert.AreEqual(new Thickness(12, 2, 12, 3), add.Padding);
      Assert.IsTrue(add.Bounds.Height < 28, $"low: {add.Bounds.Height}");
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-add-button-low.png"));

      // the Results tab: a filter's value field is left by a click on empty space too
      vm.SelectedTab = MainViewModel.ResultsTab;
      vm.RunAnalysis.AddFilterCommand.Execute(null);
      Pump();
      var value = window.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible && t.DataContext is FilterViewModel);
      value.Focus();
      Pump();
      Assert.AreSame(value, window.FocusManager!.GetFocusedElement());
      var status = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible && t.Text == vm.RunAnalysis.Status);
      Click(window, Center(status, window));
      Assert.IsNotInstanceOfType(window.FocusManager!.GetFocusedElement(), typeof(TextBox));
      window.Close();
      return true;
    }, CancellationToken.None);
  }

  [TestMethod]
  [Timeout(300_000)]
  public async Task EjectTicksBySweepingWithTheRightButton() {
    await session.Dispatch(() => {
      var eject = new EjectWindow(["A", "B", "C", "D"], [false, false, false, false]);
      eject.Show();
      Pump();
      var boxes = eject.GetVisualDescendants().OfType<CheckBox>().Where(b => b.Content is TextBlock).ToList();
      Assert.AreEqual(4, boxes.Count);
      Point At(int i) => Center(boxes[i], eject);
      // right button held on A, moved over B and C: all ticked; D not reached
      eject.MouseDown(At(0), MouseButton.Right);
      eject.MouseMove(At(1), RawInputModifiers.RightMouseButton);
      eject.MouseMove(At(2), RawInputModifiers.RightMouseButton);
      eject.MouseUp(At(2), MouseButton.Right);
      Pump();
      CollectionAssert.AreEqual(new bool?[] { true, true, true, false }, boxes.Select(b => b.IsChecked).ToList());
      Assert.IsTrue(eject.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Eject selected").IsEnabled);
      // starting on a ticked box: unticks those passed
      eject.MouseDown(At(2), MouseButton.Right);
      eject.MouseMove(At(1), RawInputModifiers.RightMouseButton);
      eject.MouseUp(At(1), MouseButton.Right);
      Pump();
      CollectionAssert.AreEqual(new bool?[] { true, false, false, false }, boxes.Select(b => b.IsChecked).ToList());
      // moving without the button: nothing changes
      eject.MouseMove(At(3));
      Pump();
      Assert.AreEqual(false, boxes[3].IsChecked);
      // a left click still ticks one
      Click(eject, At(3));
      Assert.AreEqual(true, boxes[3].IsChecked);
      eject.Close();
      return true;
    }, CancellationToken.None);
  }
}
