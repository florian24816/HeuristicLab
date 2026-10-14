using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HeuristicLab.Studio.Controls;
using HeuristicLab.Studio.Services;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>Light and dark mode (the button at the top left), and the Problem tab's tables.</summary>
[TestClass]
public class StudioThemeTests {
  private static HeadlessUnitTestSession session = null!;
  private static string LegacyGaTsp => Path.Combine(AppContext.BaseDirectory, "TestData", "GA_TSP.hl");

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  [TestMethod]
  public async Task TheButtonSwitchesModesAndTheChoiceIsRemembered() {
    var path = Path.Combine(Path.GetTempPath(), $"studio-theme-{Guid.NewGuid():N}", "settings.json");
    try {
      // a value to return: an async lambda without one would run as async void (failures lost)
      await session.Dispatch(async () => {
        var main = new MainViewModel(null, null, new JsonSettingsStore(path));
        Assert.IsFalse(main.IsDarkMode, "light by default");
        Assert.AreEqual(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);

        var window = new MainWindow { DataContext = main, Width = 1400, Height = 900 };
        window.Show();
        await main.Workspace.OpenExperimentAsync(LegacyGaTsp);
        var algorithm = main.Workspace.Experiments[0].Children.OfType<AlgorithmBlockViewModel>().First();
        main.Workspace.Selected = algorithm;
        var detail = (AlgorithmDetailViewModel)main.Workspace.Detail!;
        detail.SelectedTab = 0;
        detail.ProblemParameters!.Selected = detail.ProblemParameters.Parameters.First(p => p.Name == "Coordinates");
        Pump();

        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("theme"));
        var tabs = window.GetVisualDescendants().OfType<TabControl>().First(t => t.Classes.Contains("main"));
        var firstTab = tabs.GetVisualDescendants().OfType<TabItem>().First();
        Assert.IsTrue(firstTab.TranslatePoint(default, window)!.Value.X >= button.Bounds.Right, "the tabs start right of the button");
        Assert.AreEqual(button.Bounds.Width, button.Bounds.Height, "square");
        Assert.AreEqual("darkmode", ShownPictogram(button));
        Assert.AreEqual(Color.Parse("#BDBDBD"), ((ISolidColorBrush)button.Background!).Color);

        // the Problem tab's cells: square, columns alternate, every other row weaker
        var cells = window.GetVisualDescendants().OfType<TextBox>().Where(t => t.Classes.Contains("cell") && t.IsEffectivelyVisible).ToList();
        Assert.IsTrue(cells.Count >= 4);
        Assert.AreEqual(new CornerRadius(0), cells[0].CornerRadius);
        var shades = cells.Take(4).Select(c => ((ISolidColorBrush)((Border)c.Parent!).Background!).Color).ToList();
        Assert.AreEqual(CellViewModel.Grey, shades[0]);
        Assert.AreEqual(CellViewModel.LightGrey, shades[1]);
        Assert.AreNotEqual(shades[0], shades[2], "the second row is weaker");
        window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-theme-light.png"));

        // a matrix too wide to edit (52 columns): the same cells, read-only
        var problem = algorithm.Algorithm.Problem!;
        var parameter = (HeuristicLab.Core.IValueParameter)problem.Parameters["DistanceMatrix"];
        var distances = new double[52, 52];
        for (int i = 0; i < 52; i++) for (int j = 0; j < 52; j++) distances[i, j] = Math.Abs(i - j) * 17;
        parameter.Value = (HeuristicLab.Core.IItem)Activator.CreateInstance(parameter.DataType, distances)!;
        detail.ProblemParameters.Selected = detail.ProblemParameters.Parameters.First(p => p.Name == "DistanceMatrix");
        Pump();
        var wide = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("cell") && t.IsEffectivelyVisible).ToList();
        Assert.IsTrue(wide.Count >= 4, "the distance matrix is shown in cells");
        Assert.AreEqual(CellViewModel.Grey, ((ISolidColorBrush)((Border)wide[0].Parent!).Background!).Color);
        Assert.AreEqual(CellViewModel.LightGrey, ((ISolidColorBrush)((Border)wide[1].Parent!).Background!).Color);
        Assert.IsFalse(window.GetVisualDescendants().OfType<TextBox>().Any(t => t.Classes.Contains("cell") && t.IsEffectivelyVisible), "read-only");
        window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-theme-light-distances.png"));
        var area = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("experiment"));
        Assert.AreEqual(Color.Parse("#D5CFC6"), ((ISolidColorBrush)area.Background!).Color);

        Click(window, button);
        Assert.IsTrue(main.IsDarkMode);
        Assert.AreEqual(ThemeVariant.Dark, Application.Current.RequestedThemeVariant);
        Assert.AreEqual("lightmode", ShownPictogram(button));
        Assert.AreEqual(Color.Parse("#F7931D"), ((ISolidColorBrush)button.Background!).Color);
        Assert.AreEqual(Color.Parse("#36322E"), ((ISolidColorBrush)area.Background!).Color);
        window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-theme-dark-distances.png"));
        detail.ProblemParameters.Selected = detail.ProblemParameters.Parameters.First(p => p.Name == "Coordinates");
        Pump();
        window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-theme-dark.png"));

        main.SelectedTab = MainViewModel.ResultsTab;
        Pump();
        window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-theme-dark-results.png"));
        main.SelectedTab = MainViewModel.ResourcesTab;
        Pump();
        window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-theme-dark-resources.png"));
        window.Close();

        var again = new MainViewModel(null, null, new JsonSettingsStore(path));
        Assert.IsTrue(again.IsDarkMode, "dark mode is remembered");
        again.ToggleThemeCommand.Execute(null);
        Assert.AreEqual(ThemeVariant.Light, Application.Current.RequestedThemeVariant);
        return true;
      }, CancellationToken.None);
    } finally {
      if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }
  }

  private static string? ShownPictogram(Button button) =>
    button.GetVisualDescendants().OfType<Pictogram>().Single(p => p.IsEffectivelyVisible).Symbol;

  private static void Click(Window window, Control control) {
    var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    window.MouseDown(at, MouseButton.Left);
    window.MouseUp(at, MouseButton.Left);
    Pump();
  }
}
