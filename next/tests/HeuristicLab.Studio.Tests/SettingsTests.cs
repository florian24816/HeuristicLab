using Avalonia.Headless;
using HeuristicLab.Studio.Services;
using HeuristicLab.Studio.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

[TestClass]
public class SettingsTests {
  private static HeadlessUnitTestSession session = null!;
  private static string LegacyGaTsp => Path.Combine(AppContext.BaseDirectory, "TestData", "GA_TSP.hl");

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  [TestMethod]
  public async Task RecentFilesSurviveARestart() {
    var path = Path.Combine(Path.GetTempPath(), $"studio-settings-{Guid.NewGuid():N}", "settings.json");
    try {
      var (recent, menu, reopened) = await session.Dispatch(async () => {
        var first = new MainViewModel(null, null, new JsonSettingsStore(path));
        await first.Workspace.OpenExperimentAsync(LegacyGaTsp);

        var second = new MainViewModel(null, null, new JsonSettingsStore(path));
        var entry = second.Workspace.RecentMenu.Single();
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)entry.Command).ExecuteAsync(null);
        return (second.Workspace.RecentFiles.ToList(), entry.Display, second.Workspace.Experiments.Count);
      }, CancellationToken.None);
      CollectionAssert.AreEqual(new[] { Path.GetFullPath(LegacyGaTsp) }, recent);
      Assert.AreEqual("GA_TSP.hl", menu);
      Assert.AreEqual(1, reopened, "choosing a recent file opens it");
    } finally {
      Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }
  }

  [TestMethod]
  public void CorruptSettingsGiveDefaults() {
    var path = Path.Combine(Path.GetTempPath(), $"studio-settings-{Guid.NewGuid():N}.json");
    File.WriteAllText(path, "{ not json");
    try {
      var settings = new JsonSettingsStore(path).Load();
      Assert.AreEqual(0, settings.RecentFiles.Count);
      Assert.AreEqual(1, settings.ResourceThreads);
    } finally { File.Delete(path); }
  }
}
