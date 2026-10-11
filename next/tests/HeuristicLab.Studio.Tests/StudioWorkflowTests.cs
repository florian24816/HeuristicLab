using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless;
using HeuristicLab.Algorithms.GeneticAlgorithm;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Problems.TravelingSalesman;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

[TestClass]
public class StudioWorkflowTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
  // no ClassCleanup: HeadlessUnitTestSession.Dispose() hangs (see StudioTests)

  private static Task<T> OnUiThread<T>(Func<Task<T>> action) => session.Dispatch(action, CancellationToken.None);
  private static Task<T> OnUiThread<T>(Func<T> action) => session.Dispatch(() => Task.FromResult(action()), CancellationToken.None);

  private static string Shot(string name) => Path.Combine(AppContext.BaseDirectory, name);

  private static SetupResult SmallGaTsp() => Setups.Create(new SetupRequest("GeneticAlgorithm") {
    Problem = "TravelingSalesmanProblem", Instance = "berlin52", Settings = ["PopulationSize=20", "MaximumGenerations=10"]
  });

  /// <summary>One list as before, but grouped by HeuristicLab's categories: a heading and a tint per category.</summary>
  [TestMethod]
  public async Task AlgorithmPickerGroupsAndTintsByCategory() {
    await OnUiThread(() => {
      var vm = new CatalogPickerViewModel("Add algorithm", Catalog.Algorithms());
      var headings = vm.Visible.Where(i => i.StartsGroup).Select(i => i.Category).ToList();
      CollectionAssert.AllItemsAreUnique(headings, "each category is one block");
      CollectionAssert.Contains(headings, "Algorithms / Population Based");
      CollectionAssert.Contains(headings, "Algorithms / Single Solution");
      Assert.AreEqual(headings.Count, vm.Visible.Select(i => i.Tint).Distinct().Count(), "one color per category");
      var ga = vm.Visible.Single(i => i.Display == "Genetic Algorithm (GA)");
      Assert.AreEqual("Algorithms / Population Based", ga.Category);
      Assert.AreSame(CategoryColors.For(ga.Category).Tint, ga.Tint);

      var window = new CatalogPickerWindow { DataContext = vm };
      window.Show();
      window.CaptureRenderedFrame()!.Save(Shot("studio-algorithm-picker.png"));
      vm.Filter = "tabu";
      Assert.IsTrue(vm.Visible.All(i => i.Category == "Algorithms / Single Solution") && vm.Visible[0].StartsGroup, "a search keeps the headings");
      Assert.AreEqual("Tabu Search (TS)", vm.Selected!.Name);
      window.CaptureRenderedFrame()!.Save(Shot("studio-algorithm-picker-search.png"));
      window.Close();
      return 0;
    });
  }

  /// <summary>Problem and benchmark instance side by side, both searchable; problems grouped and tinted by category.</summary>
  [TestMethod]
  public async Task ProblemDialogSearchesProblemsAndInstances() {
    var (created, file) = await OnUiThread(() => {
      var vm = new NewSetupViewModel(null, Catalog.Find(Catalog.Algorithms(), "GeneticAlgorithm"));
      Assert.IsTrue(vm.VisibleProblems.All(i => i.Category != Choice.DefaultCategory), "a GA brings no problem of its own");
      CollectionAssert.AllItemsAreUnique(vm.VisibleProblems.Where(i => i.StartsGroup).Select(i => i.Category).ToList(), "each category is one block");
      CollectionAssert.Contains(vm.VisibleProblems.Where(i => i.StartsGroup).Select(i => i.Category).ToList(), "Problems / Combinatorial");
      var window = new NewSetupWindow { DataContext = vm };
      window.Show();
      vm.ProblemFilter = "salesman";
      Assert.IsTrue(vm.VisibleProblems.Count >= 1 && vm.VisibleProblems.All(i => i.Display.Contains("Salesman", StringComparison.OrdinalIgnoreCase)));
      vm.SelectedProblemItem = vm.VisibleProblems.Single(i => ((Choice)i.Value!).Entry?.Type == typeof(TravelingSalesmanProblem));
      Assert.AreEqual(typeof(TravelingSalesmanProblem), vm.SelectedProblem!.Entry!.Type);
      Assert.IsTrue(vm.HasInstances);
      vm.InstanceFilter = "berlin";
      Assert.AreEqual("(none)", vm.VisibleInstances[0], "no instance stays choosable");
      vm.SelectedInstance = vm.VisibleInstances.Single(i => i.EndsWith("/berlin52"));
      var path = Shot("studio-problem-dialog.png");
      window.CaptureRenderedFrame()!.Save(path);
      window.Close();
      return (vm.Create() ? vm.Result : null, path);
    });
    Assert.AreEqual(52, ((TravelingSalesmanProblem)((GeneticAlgorithm)created!.Algorithm).Problem).Coordinates.Rows);
    Assert.IsTrue(new FileInfo(file).Length > 10_000);
  }

  [TestMethod]
  public async Task NewDialogBuildsGaForBenchmarkInstance() {
    var (created, instances, file) = await OnUiThread(() => {
      var vm = new NewSetupViewModel();
      vm.SelectedAlgorithm = vm.Algorithms.Single(a => a.Entry?.Type == typeof(GeneticAlgorithm));
      vm.SelectedProblem = vm.Problems.Single(p => p.Entry?.Type == typeof(TravelingSalesmanProblem));
      vm.SelectedInstance = vm.Instances.Single(i => i.EndsWith("/berlin52"));
      var window = new NewSetupWindow { DataContext = vm };
      window.Show();
      var frame = window.CaptureRenderedFrame();
      var path = Shot("studio-new-dialog.png");
      frame!.Save(path);
      window.Close();
      return (vm.Create() ? vm.Result : null, vm.Instances.Count, path);
    });
    Assert.IsNotNull(created);
    Assert.AreEqual(52, ((TravelingSalesmanProblem)((GeneticAlgorithm)created.Algorithm).Problem).Coordinates.Rows);
    Assert.IsTrue(instances > 100);
    Assert.IsTrue(new FileInfo(file).Length > 5_000);
  }

  [TestMethod]
  public async Task NewDialogLoadsDataFileForRegression() {
    var csv = Path.Combine(Path.GetTempPath(), $"studio-data-{Guid.NewGuid():N}.csv");
    File.WriteAllLines(csv, new[] { "x1,x2,y" }.Concat(Enumerable.Range(0, 40).Select(i => FormattableString.Invariant($"{i},{i % 7},{2 * i + i % 7}"))));
    try {
      var (ok, error, columns, accepts) = await OnUiThread(() => {
        var vm = new NewSetupViewModel();
        vm.SelectedAlgorithm = vm.Algorithms.Single(a => a.Display == "Linear Regression (LR)");
        vm.SelectedProblem = vm.Problems.Single(p => p.Display == "Regression Problem");
        bool acceptsData = vm.AcceptsData;
        vm.DataFile = csv;
        var cols = vm.Columns.ToList();
        vm.Target = "y";
        return (vm.Create(), vm.Error, cols, acceptsData);
      });
      Assert.IsTrue(accepts);
      CollectionAssert.AreEqual(new[] { "x1", "x2", "y" }, columns);
      Assert.IsTrue(ok, error);
    } finally { File.Delete(csv); }
  }

  [TestMethod]
  public async Task NewDialogRejectsInstanceAndDataTogether() {
    var csv = Path.Combine(Path.GetTempPath(), $"studio-data-{Guid.NewGuid():N}.csv");
    File.WriteAllLines(csv, ["x,y", "1,2", "2,4", "3,6"]);
    try {
      var error = await OnUiThread(() => {
        var vm = new NewSetupViewModel();
        vm.SelectedAlgorithm = vm.Algorithms.Single(a => a.Display == "Linear Regression (LR)");
        vm.SelectedProblem = vm.Problems.Single(p => p.Display == "Regression Problem");
        vm.SelectedInstance = vm.Instances.First(i => i != "(none)");
        vm.DataFile = csv;
        return vm.Create() ? null : vm.Error;
      });
      StringAssert.Contains(error, "either a benchmark instance or a data file");
    } finally { File.Delete(csv); }
  }

  [TestMethod]
  public async Task ParametersAreEditableAndInvalidValuesAreReverted() {
    var (population, selector, status, shown) = await OnUiThread(() => {
      var setup = SmallGaTsp();
      var ga = (GeneticAlgorithm)setup.Algorithm;
      string status = "";
      var rows = ParameterEditor.Describe(ga).Select(p => new ParameterRowViewModel(ga, p, m => status = m)).ToList();
      rows.Single(p => p.Name == "PopulationSize").Value = "35";
      rows.Single(p => p.Name == "Selector").Value = "TournamentSelector";
      var bad = rows.Single(p => p.Name == "MaximumGenerations");
      bad.Value = "lots";
      return (ga.PopulationSize.Value, ga.Selector.GetType().Name, status, bad.Value);
    });
    Assert.AreEqual(35, population);
    Assert.AreEqual("TournamentSelector", selector);
    StringAssert.Contains(status, "lots");
    Assert.AreEqual("10", shown);
  }

  [TestMethod]
  public async Task NewSetupOpensAsAnExperiment() {
    var (name, algorithm, problem) = await OnUiThread(() => {
      var ws = new ExperimentWorkspaceViewModel();
      var experiment = ws.OpenSetup(SmallGaTsp())!;
      var block = (AlgorithmBlockViewModel)experiment.Children.Single();
      return (experiment.Name, block.Algorithm.GetType().Name, block.Algorithm.Problem?.GetType().Name);
    });
    Assert.IsFalse(string.IsNullOrEmpty(name));
    Assert.AreEqual("GeneticAlgorithm", algorithm);
    Assert.AreEqual("TravelingSalesmanProblem", problem);
  }

  private sealed class TextDialogs(string answer) : HeuristicLab.Studio.Services.IDialogService {
    public string? Asked { get; private set; }
    public Task<SetupResult?> NewSetupAsync() => Task.FromResult<SetupResult?>(null);
    public Task<CatalogEntry?> PickAlgorithmAsync() => Task.FromResult<CatalogEntry?>(null);
    public Task<HeuristicLab.Optimization.IProblem?> PickProblemAsync(HeuristicLab.Optimization.IAlgorithm algorithm) => Task.FromResult<HeuristicLab.Optimization.IProblem?>(null);
    public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
    public Task<string?> AskTextAsync(string title, string message, string initial) { Asked = initial; return Task.FromResult<string?>(answer); }
  }

  /// <summary>"Rename file..." next to "Save as...": the experiment's .hl file gets a new name in its folder.</summary>
  [TestMethod]
  public async Task RenameFileRenamesTheExperimentsFile() {
    var folder = Path.Combine(Path.GetTempPath(), $"studio-rename-{Guid.NewGuid():N}");
    Directory.CreateDirectory(folder);
    try {
      var original = Path.Combine(folder, "first.hl");
      HeuristicLab.Next.Runtime.Documents.Save(new HeuristicLab.Optimization.Experiment { Name = "My experiment" }, original);
      File.WriteAllText(Path.Combine(folder, "taken.hl"), "");
      await OnUiThread(async () => {
        var dialogs = new TextDialogs("second");
        var ws = new ExperimentWorkspaceViewModel(dialogs);
        var unsaved = ws.NewExperiment();
        Assert.IsFalse(unsaved.RenameFileCommand.CanExecute(null), "no file yet");
        var block = (await ws.OpenExperimentAsync(original))!;
        Assert.IsTrue(block.RenameFileCommand.CanExecute(null));
        Assert.IsTrue(block.Menu.Any(m => m.Display == "Rename file..."));
        await block.RenameFileCommand.ExecuteAsync(null);
        Assert.AreEqual("first.hl", dialogs.Asked, "the dialog starts with the current file name");
        var renamed = Path.Combine(folder, "second.hl");
        Assert.AreEqual(renamed, block.FilePath);
        Assert.IsTrue(File.Exists(renamed) && !File.Exists(original), ws.Status);
        Assert.AreEqual("My experiment", block.Name, "the experiment keeps its name");
        Assert.AreEqual(Path.GetFullPath(renamed), ws.RecentFiles[0]);
        CollectionAssert.DoesNotContain(ws.RecentFiles.ToList(), Path.GetFullPath(original));

        Assert.IsFalse(ws.RenameFile(block, "taken"), "never over another file");
        StringAssert.Contains(ws.Status, "already exists");
        Assert.IsFalse(ws.RenameFile(block, "a/b"), "a name, not a path");
        Assert.AreEqual(renamed, block.FilePath);
        Assert.IsTrue(((HeuristicLab.Core.IItem)HeuristicLab.Next.Runtime.Documents.Load(renamed)) is HeuristicLab.Optimization.Experiment, "the file is intact");

        // HeuristicLab's icon on the main window and on dialogs
        var window = new MainWindow { DataContext = new MainViewModel() };
        var dialog = new CatalogPickerWindow { DataContext = new CatalogPickerViewModel("Add algorithm", Catalog.Algorithms()) };
        window.Show();
        dialog.Show();
        Assert.IsNotNull(window.Icon);
        Assert.IsNotNull(dialog.Icon, "dialogs too");
        dialog.Close();
        window.Close();
        return 0;
      });
    } finally {
      Directory.Delete(folder, recursive: true);
    }
  }

  private sealed class ManyFiles(params string[] paths) : HeuristicLab.Studio.Services.IFileDialogService {
    public Task<string?> OpenHlFileAsync() => Task.FromResult<string?>(paths.FirstOrDefault());
    public Task<IReadOnlyList<string>> OpenHlFilesAsync() => Task.FromResult<IReadOnlyList<string>>(paths);
    public Task<string?> SaveHlFileAsync(string suggestedName) => Task.FromResult<string?>(null);
    public Task<string?> OpenDataFileAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    public Task<string?> SaveCsvFileAsync(string suggestedName) => Task.FromResult<string?>(null);
  }

  /// <summary>"Open..." takes several .hl files at once; each becomes an experiment (shown in a gunmetal area).</summary>
  [TestMethod]
  public async Task OpenTakesSeveralFilesAtOnce() {
    var folder = Path.Combine(Path.GetTempPath(), $"studio-open-{Guid.NewGuid():N}");
    Directory.CreateDirectory(folder);
    try {
      var files = new[] { "a", "b", "c" }.Select(n => {
        var path = Path.Combine(folder, n + ".hl");
        HeuristicLab.Next.Runtime.Documents.Save(new HeuristicLab.Optimization.Experiment { Name = "Experiment " + n }, path);
        return path;
      }).ToArray();
      var shot = await OnUiThread(async () => {
        var vm = new MainViewModel(new ManyFiles(files));
        var window = new MainWindow { DataContext = vm, Width = 1300, Height = 800 };
        window.Show();
        await vm.Workspace.OpenExperimentCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { "Experiment a", "Experiment b", "Experiment c" }, vm.Workspace.Experiments.Select(e => e.Name).ToList());
        CollectionAssert.AreEqual(files, vm.Workspace.Experiments.Select(e => e.FilePath).ToList());
        Assert.AreEqual("Opened 3 files", vm.Workspace.Status);
        var area = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("experiment"));
        Assert.AreEqual(Avalonia.Media.Color.Parse("#3E474D"), ((Avalonia.Media.ISolidColorBrush)area.Background!).Color, "filled gunmetal grey");
        Assert.AreEqual(0, area.BorderThickness.Left, "no border");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var path = Shot("studio-experiments-gunmetal.png");
        window.CaptureRenderedFrame()!.Save(path);
        window.Close();
        return path;
      });
      Assert.IsTrue(new FileInfo(shot).Length > 10_000);
    } finally {
      Directory.Delete(folder, recursive: true);
    }
  }
}
