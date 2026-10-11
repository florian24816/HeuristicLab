using Avalonia;
using Avalonia.Headless;
using Avalonia.VisualTree;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

public static class TestAppBuilder {
  public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont();
}

/// <summary>
/// Studio view model and window, run on Avalonia's headless UI thread so progress updates are
/// delivered through the dispatcher exactly as in the real application.
/// </summary>
[TestClass]
public class StudioTests {
  private static HeadlessUnitTestSession session = null!;
  private static string LegacyGaTsp => Path.Combine(AppContext.BaseDirectory, "TestData", "GA_TSP.hl");

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  // No ClassCleanup: HeadlessUnitTestSession.Dispose() blocks forever waiting for the dispatcher
  // thread (Avalonia.Headless 12.1.3); the session ends with the test process instead.

  private static Task<T> OnUiThread<T>(Func<Task<T>> action) => session.Dispatch(action, CancellationToken.None);

  /// <summary>
  /// No toolbar: a file opens in the Experiments tab, runs with the seed in its own parameters
  /// (Start all), and its runs show in the Run tab, the run's tour in the Solution tab.
  /// </summary>
  [TestMethod]
  public async Task OpenedFileRunsWithItsOwnSeedAndShowsInTheRunTab() {
    var (best, runsShown, solutions, buttons, shot) = await OnUiThread(async () => {
      var vm = new MainViewModel();
      var window = new MainWindow { DataContext = vm, Width = 1440, Height = 880 };
      window.Show();
      var experiment = (await vm.Workspace.OpenExperimentAsync(LegacyGaTsp))!;
      var block = (AlgorithmBlockViewModel)experiment.Children.Single();
      ParameterEditor.Apply(block.Algorithm, ["SetSeedRandomly=false", "Seed=0"]);
      await vm.Workspace.StartAllCommand.ExecuteAsync(null);

      // "Show runs" on the algorithm: the Run tab with its newest run selected
      block.ShowRunsCommand.Execute(null);
      Assert.AreEqual(MainViewModel.RunTab, vm.SelectedTab);
      Assert.AreSame(block.Algorithm, vm.Runs.SelectedSource!.Optimizer);
      var run = vm.Runs.SelectedRun!;
      Assert.AreSame(block.Algorithm.Runs.Last(), run);
      Assert.IsTrue(vm.Runs.RunParameters.Any(p => p.Name == "PopulationSize" && p.Value == "100"));
      var result = vm.Runs.RunResults.Single(r => r.Name == "CurrentBestQuality");
      Avalonia.Threading.Dispatcher.UIThread.RunJobs();
      var file = Path.Combine(AppContext.BaseDirectory, "studio-run-tab.png");
      window.CaptureRenderedFrame()!.Save(file);
      var buttons = window.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Select(b => b.Content as string).OfType<string>().ToList();
      window.Close();
      return (result.Summary, vm.Runs.Runs!.Rows.Count, vm.Solution.Solutions.Count, buttons, file);
    });
    Assert.AreEqual("12332", best, "seed 0 gives what HeuristicLab gives");
    Assert.IsTrue(runsShown >= 1);
    Assert.AreEqual(1, solutions, "the run's best tour");
    CollectionAssert.DoesNotContain(buttons, "Results folder...", "no toolbar");
    CollectionAssert.DoesNotContain(buttons, "Open...");
    Assert.IsTrue(new FileInfo(shot).Length > 10_000, "screenshot looks empty");
  }
}
