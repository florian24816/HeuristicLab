using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>
/// A container holding a block shows an orange line instead of its add buttons, over the same room; hovering it shows
/// the buttons (user's request 2026-11-14). Empty containers show the buttons.
/// </summary>
[TestClass]
public class StudioAddLineTests {
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
  public async Task AddButtonsFoldIntoALine() {
    await session.Dispatch(async () => {
      var vm = new MainViewModel(null, null);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1200, Height = 700 };
      window.Show();
      var experiment = ws.NewExperiment();
      ws.Selected = null;
      Pump();
      Panel Area(ContainerBlockViewModel container) => window.GetVisualDescendants().OfType<Panel>()
        .First(p => p.Classes.Contains("addarea") && p.DataContext == container);
      Border Line(ContainerBlockViewModel container) => Area(container).Children.OfType<Border>().Single(b => b.Classes.Contains("addline"));
      ItemsControl Buttons(ContainerBlockViewModel container) => Area(container).Children.OfType<ItemsControl>().Single();

      // empty: the buttons, no line
      Assert.IsFalse(Line(experiment).IsVisible);
      Assert.AreEqual(1, Buttons(experiment).Opacity);
      var room = Area(experiment).Bounds.Size;

      // a block in it: the line over the same room, the buttons folded
      var ga = ws.AddAlgorithm(experiment, Catalog.Find(Catalog.Algorithms(), "GeneticAlgorithm")!);
      Pump();
      Assert.IsTrue(Line(experiment).IsEffectivelyVisible, "an orange line");
      Assert.AreEqual(Avalonia.Media.Color.Parse("#F7931D"), ((Avalonia.Media.ISolidColorBrush)Line(experiment).Background!).Color);
      Assert.AreEqual(0, Buttons(experiment).Opacity, "the buttons folded");
      Assert.AreEqual(room, Area(experiment).Bounds.Size, "the same room: nothing jumps");
      Assert.AreEqual(room.Width, Line(experiment).Bounds.Width, 0.5, "as long as the buttons");
      var below = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("missing") && t.DataContext == experiment);
      var belowY = below.TranslatePoint(default, window)!.Value.Y;
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-add-line.png"));

      // hovered: the buttons, no line, nothing moved
      window.MouseMove(Center(Line(experiment), window));
      Pump();
      Assert.AreEqual(1, Buttons(experiment).Opacity);
      Assert.IsFalse(Line(experiment).IsVisible);
      Assert.AreEqual(belowY, below.TranslatePoint(default, window)!.Value.Y, "nothing moved");
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-add-line-hovered.png"));
      // a click there adds: "+ batch run"
      var batchButton = Buttons(experiment).GetVisualDescendants().OfType<Button>().Single(b => (string?)b.Content == "+ batch run");
      window.MouseDown(Center(batchButton, window), MouseButton.Left);
      window.MouseUp(Center(batchButton, window), MouseButton.Left);
      Pump();
      Assert.AreEqual(2, experiment.Children.Count);
      var batch = (ContainerBlockViewModel)experiment.Children[1];
      window.MouseMove(new Point(2, 2));
      Pump();
      Assert.AreEqual(0, Buttons(experiment).Opacity, "the pointer gone: folded again");

      // the new, empty batch run shows its buttons; with a block its own line
      Assert.AreEqual(1, Buttons(batch).Opacity);
      Assert.IsFalse(Line(batch).IsVisible);
      ws.AddAlgorithm(batch, Catalog.Find(Catalog.Algorithms(), "GeneticAlgorithm")!);
      Pump();
      Assert.IsTrue(Line(batch).IsEffectivelyVisible);
      // all blocks gone: the buttons again
      foreach (var child in experiment.Children.ToList()) await ws.DeleteAsync(child);
      Pump();
      Assert.AreEqual(0, experiment.Children.Count);
      Assert.IsFalse(Line(experiment).IsVisible);
      Assert.AreEqual(1, Buttons(experiment).Opacity);
      vm.IsDarkMode = true;
      ws.AddAlgorithm(experiment, Catalog.Find(Catalog.Algorithms(), "GeneticAlgorithm")!);
      Pump();
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-add-line-dark.png"));
      window.Close();
      return true;
    }, CancellationToken.None);
  }
}
