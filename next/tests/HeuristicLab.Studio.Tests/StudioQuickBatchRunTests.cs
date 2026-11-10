using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;
using HeuristicLab.Studio.Services;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>
/// Start on one algorithm makes a quick batch run (QBR: 10 runs, the Options tab's size): kept apart from the experiment's
/// runs, listed under "Quick batch runs" > experiment > algorithm > "QBR k" in the Runs and Results tabs (ticked only when
/// wanted), saved next to the experiment's file; older single runs are read as QBRs of one run. The Options tab sets the
/// size and a working directory where file dialogs open.
/// </summary>
[TestClass]
public class StudioQuickBatchRunTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  private sealed class MemorySettings(StudioSettings settings) : ISettingsStore {
    public StudioSettings Saved { get; private set; } = settings;
    public StudioSettings Load() => Saved;
    public void Save(StudioSettings settings) => Saved = settings;
  }

  private sealed class Folders(string folder) : IFileDialogService {
    public string? WorkingDirectory { get; set; }
    public Task<string?> OpenHlFileAsync() => Task.FromResult<string?>(null);
    public Task<string?> SaveHlFileAsync(string suggestedName) => Task.FromResult<string?>(null);
    public Task<string?> OpenDataFileAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(folder);
    public Task<string?> SaveCsvFileAsync(string suggestedName) => Task.FromResult<string?>(null);
  }

  [TestMethod]
  [Timeout(600_000)]
  public async Task QuickBatchRunsAreKeptApartFromTheExperiment() {
    var folder = Path.Combine(Path.GetTempPath(), $"studio-qbr-{Guid.NewGuid():N}");
    Directory.CreateDirectory(folder);
    try {
      await session.Dispatch(async () => {
        var vm = new MainViewModel(null, null);
        var ws = vm.Workspace;
        Assert.AreEqual(10, ws.QuickBatchSize, "10 runs at first");
        var window = new MainWindow { DataContext = vm, Width = 1500, Height = 960 };
        window.Show();
        var experiment = ws.NewExperiment();
        var batch = ws.AddContainer(experiment, ExperimentTree.ContainerTypes().Single(e => e.Type == typeof(BatchRun)));
        batch.RepetitionsText = "2";
        var ga = ws.AddAlgorithm(batch, Catalog.Find(Catalog.Algorithms(), "GeneticAlgorithm")!);
        ws.SetProblem(ga, (IProblem)Setups.Create(new SetupRequest("GeneticAlgorithm") { Problem = "TravelingSalesmanProblem", Instance = "ch130" }).Algorithm.Problem!.Clone());
        ParameterEditor.Apply(ga.Algorithm, ["PopulationSize=20", "MaximumGenerations=5"]);
        await ws.StartAllCommand.ExecuteAsync(null);
        Assert.AreEqual(2, experiment.Experiment.Runs.Count, "the experiment's own runs");

        // Start on the algorithm: a quick batch run of 10 runs, none of them the batch run's or the experiment's
        ws.Selected = ga;
        var detail = (AlgorithmDetailViewModel)ws.Detail!;
        detail.StartCommand.Execute(null);
        await detail.WaitAsync();
        Pump();
        Assert.AreEqual(2, experiment.Experiment.Runs.Count, "the experiment is as it was made");
        Assert.AreEqual(2, ((BatchRun)batch.Optimizer).Runs.Count);
        Assert.AreEqual(10, experiment.QuickBatchRuns.Count);
        CollectionAssert.AreEqual(Enumerable.Range(1, 10).Select(j => $"QBR 1, run {j}").ToList(), experiment.QuickBatchRuns.Runs.Select(r => r.Name).ToList());
        Assert.AreEqual(10, experiment.QuickBatchRuns.Runs.Select(r => ((HeuristicLab.Data.IntValue)r.Parameters["Seed"]).Value).Distinct().Count(), "each run its own seed");
        StringAssert.Contains(detail.Message, "Quick batch runs");
        Assert.IsNull(detail.BatchProgress, "done");
        detail.Runs.Refresh();
        Assert.AreEqual(10, detail.Runs.Rows.Count(r => r.Name.StartsWith("QBR 1,")), "the algorithm's Runs tab shows them too");
        Assert.AreEqual("Runs (2, 10 in QBRs)", ga.RunsText, "its 2 in the batch run, 10 in its quick batch run");

        // the Options tab's size: the next Start makes QBR 2 of 3 runs
        vm.Options.QuickBatchSize = 3;
        Assert.AreEqual(3, ws.QuickBatchSize);
        detail.StartCommand.Execute(null);
        await detail.WaitAsync();
        Pump();
        Assert.AreEqual(13, experiment.QuickBatchRuns.Count);
        var group = experiment.QuickBatchRuns.Groups.Single();
        CollectionAssert.AreEqual(new[] { "QBR 1", "QBR 2" }, group.Batches.Select(b => b.Name).ToList());
        Assert.AreEqual(3, group.Batches[1].Runs.Count);

        // Runs tab: "Quick batch runs" > experiment > algorithm > QBR k
        vm.SelectedTab = MainViewModel.RunsTab;
        vm.Runs.Refresh();
        var qbrs = vm.Runs.Sources.Last();
        Assert.AreEqual("Quick batch runs", qbrs.Name);
        var algorithmNode = qbrs.Children.Single().Children.Single();
        Assert.AreEqual(ga.Algorithm.Name, algorithmNode.Name);
        CollectionAssert.AreEqual(new[] { "QBR 1", "QBR 2" }, algorithmNode.Children.Select(c => c.Name).ToList());
        vm.Runs.SelectedSource = algorithmNode.Children[1];
        CollectionAssert.AreEqual(new[] { "QBR 2, run 1", "QBR 2, run 2", "QBR 2, run 3" }, vm.Runs.Runs!.Rows.Select(r => r.Name).ToList());
        Pump();
        window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-qbr-runs-tab.png"));

        // Results tab: the experiment alone, then with the quick batch runs, one QBR, only the QBRs
        vm.SelectedTab = MainViewModel.ResultsTab;
        var analysis = vm.RunAnalysis;
        analysis.RefreshSources();
        analysis.Sources.First().IsChecked = true;
        Assert.AreEqual(2, analysis.Table.Rows.Count, "quick batch runs are not ticked at first");
        Assert.IsTrue(analysis.AllButQbrsTicked);
        var qbrSource = analysis.Sources.Single(s => s.Name == "Quick batch runs");
        Assert.AreEqual(false, qbrSource.IsChecked);
        var second = qbrSource.Children.Single().Children.Single().Children.Single(c => c.Name == "QBR 2");
        second.IsChecked = true;
        Assert.AreEqual(5, analysis.Table.Rows.Count, "the experiment's 2 and QBR 2's 3");
        qbrSource.IsChecked = true;
        Assert.AreEqual(15, analysis.Table.Rows.Count);
        Assert.AreEqual(13, analysis.Table.Rows.Count(r => r.Text("source") == "Quick batch runs"));
        Assert.IsTrue(analysis.Table.Rows.All(r => r.Text("param:Resources: CPU cores") != ""), "the QBRs' runs are tagged with their resources too");
        analysis.SelectOnlyQbrsCommand.Execute(null);
        Assert.IsTrue(analysis.OnlyQbrsTicked);
        Assert.AreEqual(13, analysis.Table.Rows.Count);
        Pump();
        var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string).ToList();
        CollectionAssert.IsSubsetOf(new[] { "All w/o QBRs", "Only QBRs" }, buttons);
        Assert.IsFalse(buttons.Any(b => b?.Contains("single", StringComparison.OrdinalIgnoreCase) == true));
        window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-qbr-results.png"));

        // saved next to the experiment, back when it is opened again
        var path = Path.Combine(folder, "exp.hl");
        experiment.FilePath = path;
        await ws.SaveChangesAsync(experiment);
        var side = QuickBatchRuns.FileFor(path);
        Assert.AreEqual("exp.quick-batch-runs.hl", Path.GetFileName(side));
        Assert.IsTrue(File.Exists(side));
        Assert.IsInstanceOfType<RunCollection>(Documents.Load(side), "a run collection HeuristicLab opens");
        var again = new MainViewModel(null, null);
        var reopened = await again.Workspace.OpenExperimentAsync(path);
        Assert.AreEqual(2, reopened!.Experiment.Runs.Count);
        Assert.AreEqual(13, reopened.QuickBatchRuns.Count);
        Assert.AreEqual(2, reopened.QuickBatchRuns.Groups.Single().Batches.Count);

        // an older experiment's single runs (exp2.single-runs.hl): each a QBR of one run
        var oldPath = Path.Combine(folder, "exp2.hl");
        Documents.Save(experiment.Experiment, oldPath);
        var oldRuns = new RunCollection();
        for (int k = 1; k <= 2; k++) {
          var run = (IRun)experiment.QuickBatchRuns.Runs.First().Clone();
          run.Name = $"Single run {k}";
          oldRuns.Add(run);
        }
        Documents.Save(oldRuns, QuickBatchRuns.OldFileFor(oldPath));
        var old = await again.Workspace.OpenExperimentAsync(oldPath);
        CollectionAssert.AreEqual(new[] { "QBR 1, run 1", "QBR 2, run 1" }, old!.QuickBatchRuns.Runs.Select(r => r.Name).ToList());
        window.Close();
        return true;
      }, CancellationToken.None);
    } finally {
      Directory.Delete(folder, recursive: true);
    }
  }

  [TestMethod]
  [Timeout(300_000)]
  public async Task OptionsTabSetsTheSizeAndTheWorkingDirectory() {
    var folder = Path.Combine(Path.GetTempPath(), $"studio-options-{Guid.NewGuid():N}");
    Directory.CreateDirectory(folder);
    try {
      await session.Dispatch(async () => {
        var settings = new MemorySettings(new StudioSettings { QuickBatchSize = 7 });
        var files = new Folders(folder);
        var vm = new MainViewModel(files, null, settings);
        Assert.AreEqual(7, vm.Workspace.QuickBatchSize, "remembered");
        Assert.IsNull(files.WorkingDirectory);
        var window = new MainWindow { DataContext = vm, Width = 1300, Height = 800 };
        window.Show();
        vm.SelectedTab = MainViewModel.OptionsTab;
        Pump();
        var tabs = window.GetVisualDescendants().OfType<TabItem>().Select(t => t.Header as string).ToList();
        Assert.AreEqual(tabs.IndexOf("Resources") + 1, tabs.IndexOf("Options"), "right after Resources");
        var size = window.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "QbrSize" && n.IsEffectivelyVisible);
        Assert.AreEqual(7m, size.Value);

        // the size: saved, used by the workspace; out of range is clamped, "Back to 10"
        vm.Options.QuickBatchSize = 25;
        Assert.AreEqual(25, vm.Workspace.QuickBatchSize);
        Assert.AreEqual(25, settings.Saved.QuickBatchSize);
        vm.Options.QuickBatchSize = 0;
        Assert.AreEqual(1, vm.Workspace.QuickBatchSize);
        vm.Options.DefaultQuickBatchSizeCommand.Execute(null);
        Assert.AreEqual(10, vm.Workspace.QuickBatchSize);

        // the working directory: chosen, handed to the file dialogs, saved; then none again
        await vm.Options.ChooseWorkingDirectoryCommand.ExecuteAsync(null);
        Assert.AreEqual(folder, vm.Options.WorkingDirectory);
        Assert.AreEqual(folder, files.WorkingDirectory, "every file dialog opens there");
        Assert.AreEqual(folder, settings.Saved.WorkingDirectory);
        Pump();
        Assert.AreEqual(folder, window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "WorkingDirectoryPath").Text);
        window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-options.png"));
        var next = new MainViewModel(new Folders(folder), null, settings);
        Assert.AreEqual(folder, next.Options.WorkingDirectory, "remembered");
        vm.Options.ClearWorkingDirectoryCommand.Execute(null);
        Assert.IsNull(files.WorkingDirectory);
        Assert.IsNull(settings.Saved.WorkingDirectory);
        window.Close();
        return true;
      }, CancellationToken.None);
    } finally {
      Directory.Delete(folder, recursive: true);
    }
  }
}
