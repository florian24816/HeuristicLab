using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;
using HeuristicLab.Studio.Services;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>The notifications button: when on, finished runs send a message to the notification center.</summary>
[TestClass]
public class StudioNotificationTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private sealed class FakeNotifications : IDesktopNotifications {
    public List<(string Title, string Message)> Sent { get; } = [];
    public Task<string?> SendAsync(string title, string message) {
      Sent.Add((title, message));
      return Task.FromResult<string?>(null);
    }
  }

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  private static IProblem Ch130() =>
    (IProblem)Setups.Create(new SetupRequest("LocalSearch") { Problem = "TravelingSalesmanProblem", Instance = "ch130" }).Algorithm.Problem!.Clone();

  [TestMethod]
  [Timeout(300_000)]
  public async Task FinishedRunsAreAnnouncedWhenNotificationsAreOn() {
    var path = Path.Combine(Path.GetTempPath(), $"studio-notify-{Guid.NewGuid():N}", "settings.json");
    try {
      await session.Dispatch(async () => {
        var fake = new FakeNotifications();
        var main = new MainViewModel(null, null, new JsonSettingsStore(path), fake);
        var ws = main.Workspace;
        var window = new MainWindow { DataContext = main, Width = 1400, Height = 900 };
        window.Show();
        Pump();

        // the button: square right of the light/dark one, light grey when off
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("notify"));
        var theme = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("theme"));
        Assert.AreEqual(button.Bounds.Width, button.Bounds.Height);
        Assert.IsTrue(button.Bounds.Left > theme.Bounds.Right, "right of the light/dark button");
        var firstTab = window.GetVisualDescendants().OfType<TabItem>().First();
        Assert.IsTrue(firstTab.TranslatePoint(default, window)!.Value.X >= button.Bounds.Right, "the tabs start right of it");
        Assert.IsFalse(main.IsNotifying, "off by default");
        Assert.AreEqual(Color.Parse("#E0E0E0"), ((ISolidColorBrush)button.Background!).Color);
        main.IsDarkMode = true;
        Pump();
        Assert.AreEqual(Color.Parse("#6B6B6B"), ((ISolidColorBrush)button.Background!).Color, "darker grey in dark mode");
        main.IsDarkMode = false;

        var experiment = ws.NewExperiment();
        var ls = ws.AddAlgorithm(experiment, Catalog.Find(Catalog.Algorithms(), "LocalSearch")!);
        ws.SetProblem(ls, Ch130());
        ParameterEditor.Apply(ls.Algorithm, ["MaximumIterations=5"]);

        // off: nothing is sent
        await ws.StartAllCommand.ExecuteAsync(null);
        Assert.AreEqual(0, fake.Sent.Count);

        // on: experiments, a single algorithm and a detailed analysis run each send one message
        button.Command!.Execute(null);
        Pump();
        Assert.IsTrue(main.IsNotifying);
        Assert.AreEqual(Color.Parse("#F7931D"), ((ISolidColorBrush)button.Background!).Color, "orange when on");
        window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-notifications-on.png"));

        await ws.StartSelectedCommand.ExecuteAsync(null);  // nothing ticked: nothing started
        experiment.IsChecked = true;
        await ws.StartSelectedCommand.ExecuteAsync(null);
        Assert.AreEqual(1, fake.Sent.Count);
        StringAssert.Contains(fake.Sent[0].Title, experiment.Name);
        StringAssert.Contains(fake.Sent[0].Message, "Completed");

        ws.Selected = ls;
        var detail = (AlgorithmDetailViewModel)ws.Detail!;
        detail.StartCommand.Execute(null);
        await detail.WaitAsync();
        Assert.AreEqual(2, fake.Sent.Count, string.Join(" | ", fake.Sent));
        StringAssert.Contains(fake.Sent[1].Title, ls.Name);

        await detail.Analysis.StartAsync();
        Assert.AreEqual(3, fake.Sent.Count, string.Join(" | ", fake.Sent));
        StringAssert.Contains(fake.Sent[2].Title, "Detailed analysis");
        StringAssert.Contains(fake.Sent[2].Message, "Finished");
        window.Close();

        var again = new MainViewModel(null, null, new JsonSettingsStore(path), fake);
        Assert.IsTrue(again.IsNotifying, "remembered");
        return true;
      }, CancellationToken.None);
    } finally {
      if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }
  }
}
