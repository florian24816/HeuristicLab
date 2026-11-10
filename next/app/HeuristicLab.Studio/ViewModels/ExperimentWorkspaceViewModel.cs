using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeuristicLab.Common;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;
using HeuristicLab.Scripting;
using HeuristicLab.Studio.Services;

namespace HeuristicLab.Studio.ViewModels;

/// <summary>A position among a container's children: Index 0 is first, Children.Count after the last.</summary>
public sealed record DropTarget(ContainerBlockViewModel Container, int Index);

/// <summary>
/// Experiments tab: several experiments side by side, each a tree of building blocks (algorithm +
/// problem, batch run, time-limit run) that is a plain HeuristicLab Experiment underneath.
/// Add buttons belong to their container; start buttons are enabled only when every experiment
/// they would start is complete (each algorithm has a problem, no container is empty).
/// </summary>
public partial class ExperimentWorkspaceViewModel : ViewModelBase {
  private readonly IDialogService? dialogs;
  private readonly IFileDialogService? fileDialogs;
  private readonly Action<IOptimizer> showRuns;
  private readonly Action<CSharpScript> openScript;
  private readonly Action recentFilesChanged;
  private const int MaxRecentFiles = 10;
  private CancellationTokenSource? stopSource;
  /// <summary>Free places for experiments at the same time (Resources tab), shared by overlapping starts.</summary>
  private SemaphoreSlim? slots;
  private int experimentCounter, batchCounter, timeLimitCounter;

  public ExperimentWorkspaceViewModel(IDialogService? dialogs = null, IFileDialogService? fileDialogs = null,
      Action<IOptimizer>? showRuns = null, Action<CSharpScript>? openScript = null,
      IEnumerable<string>? recentFiles = null, Action? recentFilesChanged = null) {
    this.dialogs = dialogs;
    this.fileDialogs = fileDialogs;
    this.showRuns = showRuns ?? (_ => { });
    this.openScript = openScript ?? (_ => { });
    this.recentFilesChanged = recentFilesChanged ?? (() => { });
    foreach (var file in (recentFiles ?? []).Where(File.Exists).Take(MaxRecentFiles)) RecentFiles.Add(file);
    Samples = new SamplesViewModel(OpenSampleAsync);
    Detail = Samples;
    _ = Samples.LoadAsync();
    liveTimer = new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(500), Avalonia.Threading.DispatcherPriority.Background, (_, _) => RefreshLive());
  }

  public ObservableCollection<ExperimentBlockViewModel> Experiments { get; } = [];

  public bool HasExperiments => Experiments.Count > 0;

  [ObservableProperty]
  public partial BlockViewModel? Selected { get; set; }

  partial void OnDetailChanged(ViewModelBase? value) {
    if (value is AlgorithmDetailViewModel detail) detail.SyncEditability();
  }

  partial void OnSelectedChanged(BlockViewModel? oldValue, BlockViewModel? newValue) {
    if (oldValue != null) { oldValue.IsSelected = false; oldValue.IsEditingName = false; }
    if (newValue != null) newValue.IsSelected = true;
    if (MultiEditing is { } multi && newValue is AlgorithmBlockViewModel { IsMultiMember: true } member) {
      // editing across algorithms: straight to the same field in this algorithm, with the keyboard there
      Detail = member.Detail;
      member.Detail.Reveal(multi.Key);
      RefreshMultiEdit();
      FocusMultiFieldRequested?.Invoke();
      return;
    }
    Detail = newValue switch {
      AlgorithmBlockViewModel a => a.ShowDetail(AlgorithmDetailViewModel.AlgorithmTab),
      ProblemBlockViewModel p => p.Owner.ShowDetail(AlgorithmDetailViewModel.ProblemTab),
      null => Samples,
      _ => newValue
    };
    if (newValue == null) _ = Samples.LoadAsync();
    RefreshMultiEdit();
  }

  // ---- editing one field across algorithms (Shift+click on a value or choice)

  /// <summary>The field edited across the algorithms of one type in one experiment; null: off.</summary>
  [ObservableProperty]
  public partial MultiEditState? MultiEditing { get; set; }

  /// <summary>Changes whenever editing across algorithms starts or ends: editors built before are outdated.</summary>
  public int MultiVersion { get; private set; }

  /// <summary>Asks the view to put the keyboard focus into the purple field (and open a choice's list).</summary>
  public event Action? FocusMultiFieldRequested;

  /// <summary>The key of the field edited across algorithms, for this algorithm (if it takes part).</summary>
  public string? MultiEditKeyFor(AlgorithmBlockViewModel block) => MultiEditing is { } m && block.IsMultiMember ? m.Key : null;

  private AlgorithmBlockViewModel? ShownAlgorithm => Selected switch {
    AlgorithmBlockViewModel a => a,
    ProblemBlockViewModel p => p.Owner,
    _ => null
  };

  /// <summary>
  /// Shift+click on a field: edit it across all algorithms of this type in this experiment, or (on the
  /// purple field) stop. The algorithms taking part are tinted purple; clicking one shows the field.
  /// </summary>
  public bool ToggleMultiEdit(AlgorithmBlockViewModel block, string key) {
    if (block.Root is not { } root || !CanChange(block)) return false;
    bool stop = MultiEditing is { } m && m.Key == key && block.IsMultiMember;
    MultiEditing = stop ? null : new MultiEditState(root, block.Algorithm.GetType(), key);
    MultiVersion++;
    if (ShownAlgorithm != block) Selected = block;
    RefreshMultiEdit();
    block.Detail.Reveal(key);
    Status = stop ? "Stopped editing across algorithms."
      : $"Editing {Describe(key)} across {AllBlocks(Experiments).Count(b => b.IsMultiMember)} algorithms: click a purple algorithm to go to its field; Shift+click a purple surface to stop.";
    if (!stop) FocusMultiFieldRequested?.Invoke();
    return true;
  }

  /// <summary>Stops editing across algorithms (Shift+click on a purple block).</summary>
  public void ExitMultiEdit() {
    if (MultiEditing is not { } m) return;
    MultiEditing = null;
    MultiVersion++;
    RefreshMultiEdit();
    ShownAlgorithm?.Detail.Reveal(m.Key);
    Status = "Stopped editing across algorithms.";
  }

  private static string Describe(string key) {
    var last = MultiEdit.Steps(key).Last(s => s.StartsWith("p:", StringComparison.Ordinal))[2..];
    return MultiEdit.IsChoice(key) ? $"the choice of {last}" : last;
  }

  /// <summary>Which algorithm blocks take part (same type, same experiment, the field exists) and which one is shown.</summary>
  public void RefreshMultiEdit() {
    if (MultiEditing is { } m && !Experiments.Contains(m.Root)) {
      MultiEditing = null;
      MultiVersion++;
    }
    var shown = ShownAlgorithm;
    foreach (var block in AllBlocks(Experiments).OfType<AlgorithmBlockViewModel>()) {
      bool member = MultiEditing is { } multi && block.Root == multi.Root && block.Algorithm.GetType() == multi.AlgorithmType
                    && MultiEdit.Resolves(block.Algorithm, multi.Key);
      block.MultiLevel = !member ? 0 : block == shown ? 2 : 1;
    }
  }

  /// <summary>
  /// What the right-hand side shows: an algorithm's detail (tabs), a container's settings, or - with
  /// nothing selected - the samples (HeuristicLab's start page).
  /// </summary>
  [ObservableProperty]
  public partial ViewModelBase? Detail { get; set; }

  /// <summary>How many runs Start on one algorithm makes: a quick batch run (the Options tab).</summary>
  public int QuickBatchSize { get; set; } = QuickBatchRuns.DefaultSize;

  /// <summary>
  /// Anything runs (experiments, a quick batch run, a detailed analysis; also paused): live monitoring is on. Only the
  /// experiments that run are locked (ExperimentBlockViewModel.IsLocked); the others and new ones can be worked on
  /// (user's request 2026-11-10).
  /// </summary>
  [ObservableProperty]
  [NotifyCanExecuteChangedFor(nameof(StartAllCommand), nameof(StartSelectedCommand), nameof(StopCommand), nameof(EjectCommand))]
  public partial bool IsRunning { get; set; }

  /// <summary>Experiments started with the start buttons are running.</summary>
  [ObservableProperty]
  [NotifyCanExecuteChangedFor(nameof(StopCommand))]
  public partial bool IsRunningExperiments { get; set; }

  /// <summary>What runs on one algorithm (its quick batch run, its detailed analysis), by who started it: locks its experiment.</summary>
  private readonly Dictionary<object, AlgorithmBlockViewModel> algorithmsRunning = new(System.Collections.Generic.ReferenceEqualityComparer.Instance);

  /// <summary>Experiment runs started with the start buttons and not done yet (several starts may overlap).</summary>
  private int experimentRuns;

  /// <summary>An algorithm's quick batch run or detailed analysis (<paramref name="key"/>: who started it) runs, is paused, or ended.</summary>
  public void SetAlgorithmRunning(AlgorithmBlockViewModel block, object key, bool running) {
    var experiment = block.Root;
    // told again (a paused algorithm refreshes): nothing changes - refreshing here would refresh it again, endlessly
    if (running) {
      if (algorithmsRunning.TryGetValue(key, out var known) && known == block) return;
      algorithmsRunning[key] = block;
    } else if (!algorithmsRunning.Remove(key)) return;
    UpdateRunning();
    if (experiment != null) LockChanged(experiment);
    if (!running) RunsFinished?.Invoke();
  }

  /// <summary>Whether a quick batch run or detailed analysis runs in this experiment.</summary>
  internal bool RunsAlgorithmIn(ExperimentBlockViewModel experiment) => algorithmsRunning.Values.Any(b => b.Root == experiment);

  private void UpdateRunning() {
    IsRunningExperiments = experimentRuns > 0;
    IsRunning = experimentRuns > 0 || algorithmsRunning.Count > 0;
  }

  /// <summary>Raised when runs were made (an experiment run, a quick batch run or an analysis ended): the runs and results views read them again.</summary>
  public event Action? RunsFinished;

  /// <summary>Whether this block can be changed now: not while its experiment runs (other experiments can).</summary>
  public bool CanChange(BlockViewModel? block) => block?.Root is not { } root || !root.IsLocked;

  /// <summary>An experiment started or stopped running: its blocks offer changes or not, the buttons follow.</summary>
  internal void LockChanged(ExperimentBlockViewModel experiment) {
    experiment.RefreshLocked();
    foreach (var block in AllBlocks([experiment])) {
      block.RefreshEditable();
      if (block is AlgorithmBlockViewModel { Problem: { } problem }) problem.RefreshEditable();
    }
    RefreshLive();
    RefreshState();
  }

  /// <summary>Something started here finished (experiments, an algorithm, a detailed analysis run): title, message.</summary>
  public event Action<string, string>? Finished;

  public void NotifyFinished(string title, string message) => Finished?.Invoke(title, message);

  partial void OnIsRunningChanged(bool value) {
    if (value) liveTimer.Start();
    RefreshLive();
    if (!value) liveTimer.Stop();
  }

  private readonly Avalonia.Threading.DispatcherTimer liveTimer;

  /// <summary>Whether live monitoring is active (anything is running).</summary>
  public bool IsLive => liveTimer.IsEnabled;

  /// <summary>
  /// Live monitoring while anything runs: the shown detail (results, selected result's
  /// visualization, runs), the experiments' run tables and the tree's counters. Called by a timer
  /// every half second; reads are tolerant of the running algorithm changing the data.
  /// </summary>
  public void RefreshLive() {
    switch (Detail) {
      case AlgorithmDetailViewModel a: a.Refresh(); break;
      case ContainerBlockViewModel c: c.RefreshRuns(); break;
    }
    foreach (var block in AllBlocks(Experiments)) {
      switch (block) {
        case AlgorithmBlockViewModel a: a.RefreshCounts(); a.RefreshStatus(); break;
        case ContainerBlockViewModel c: c.RefreshBadge(); break;
      }
    }
  }

  [ObservableProperty]
  public partial string Status { get; set; } = "";

  // ---- creating and opening experiments

  /// <summary>HeuristicLab's start page samples.</summary>
  public SamplesViewModel Samples { get; }

  [RelayCommand]
  private void ShowSamples() {
    Selected = null;
    Detail = Samples;
    _ = Samples.LoadAsync();
  }

  /// <summary>An algorithm sample becomes an experiment; a script sample opens in the Scripts tab.</summary>
  public async Task OpenSampleAsync(SampleInfo sample) {
    try {
      Status = $"Opening {sample.Name} ...";
      var content = await Task.Run(() => HeuristicLab.Next.Runtime.Samples.Load(sample.Id));
      Open(content, sample.Name, null);
      if (sample.Limitation != null) Status += $" Note: {sample.Limitation}";
    } catch (Exception e) {
      Status = $"Could not open {sample.Name}: {e.Message}";
    }
  }

  [RelayCommand]
  private void CreateExperiment() => NewExperiment();

  public ExperimentBlockViewModel NewExperiment() {
    var experiment = new Experiment { Name = $"New experiment {++experimentCounter}" };
    return AddExperiment(experiment, null);
  }

  /// <summary>Most recently opened or saved files, newest first.</summary>
  public ObservableCollection<string> RecentFiles { get; } = [];

  public bool HasRecentFiles => RecentFiles.Count > 0;

  /// <summary>"Open recent" menu: file names, the full path as tooltip.</summary>
  public IReadOnlyList<MenuChoice> RecentMenu =>
    RecentFiles.Select(f => new MenuChoice(Path.GetFileName(f), new AsyncRelayCommand(() => OpenExperimentAsync(f)), f)).ToList();

  private void Remember(string path) {
    var full = Path.GetFullPath(path);
    RecentFiles.Remove(full);
    RecentFiles.Insert(0, full);
    while (RecentFiles.Count > MaxRecentFiles) RecentFiles.RemoveAt(RecentFiles.Count - 1);
    OnPropertyChanged(nameof(HasRecentFiles));
    OnPropertyChanged(nameof(RecentMenu));
    recentFilesChanged();
  }

  /// <summary>HeuristicLab's "new" setup (algorithm, problem, benchmark instance or CSV/Parquet data) as a new experiment.</summary>
  [RelayCommand]
  private async Task NewFromSetupAsync() {
    if (dialogs != null && await dialogs.NewSetupAsync() is SetupResult setup) OpenSetup(setup);
  }

  public ExperimentBlockViewModel? OpenSetup(SetupResult setup) {
    var block = Open(setup.Algorithm, setup.Algorithm.Name, null);
    if (setup.Messages.Count > 0) Status += " " + string.Join("; ", setup.Messages);
    return block;
  }

  [RelayCommand]
  private async Task OpenExperimentAsync() {
    // several files can be chosen at once: each opens as its own experiment
    if (fileDialogs == null) return;
    var paths = await fileDialogs.OpenHlFilesAsync();
    int opened = 0;
    foreach (var path in paths)
      if (await OpenExperimentAsync(path) != null) opened++;
    if (paths.Count > 1 && opened == paths.Count) Status = $"Opened {opened} files";
    else if (paths.Count > 1) Status = $"Opened {opened} of {paths.Count} files; last message: {Status}";
  }

  /// <summary>Opens a .hl file: an experiment as it is, any other optimizer inside a new experiment.</summary>
  public async Task<ExperimentBlockViewModel?> OpenExperimentAsync(string path) {
    try {
      var content = await Task.Run(() => Documents.Load(path));
      Remember(path);
      return Open(content, Path.GetFileName(path), path);
    } catch (Exception e) {
      Status = $"Could not open {Path.GetFileName(path)}: {e.Message}";
      return null;
    }
  }

  /// <summary>An experiment as it is, any other optimizer inside a new experiment, a script in the Scripts tab.</summary>
  private ExperimentBlockViewModel? Open(object content, string name, string? path) {
    switch (content) {
      case Experiment experiment:
        Status = $"Opened {name}";
        return AddExperiment(experiment, path);
      case IOptimizer optimizer:
        var wrapper = new Experiment { Name = path != null ? Path.GetFileNameWithoutExtension(path) : name };
        wrapper.Optimizers.Add(optimizer);
        Status = $"Opened {name} ({optimizer.ItemName}) as a new experiment.";
        return AddExperiment(wrapper, null);
      case CSharpScript script:
        openScript(script);
        Status = $"Opened {name} in the Scripts tab.";
        return null;
      default:
        Status = $"{name} does not contain an experiment, batch run, algorithm or script.";
        return null;
    }
  }

  private ExperimentBlockViewModel AddExperiment(Experiment experiment, string? path) {
    var block = new ExperimentBlockViewModel(this, experiment, path);
    if (path != null) {
      try {
        block.QuickBatchRuns.Load(path);
      } catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException) {
        Status = $"Its quick batch runs ({Path.GetFileName(QuickBatchRuns.FileFor(path))}) could not be read: {e.Message}";
      }
    }
    Experiments.Add(block);
    Selected = block;
    OnPropertyChanged(nameof(HasExperiments));
    RefreshState();
    return block;
  }

  internal async Task SaveAsync(ExperimentBlockViewModel block) {
    if (fileDialogs == null) return;
    if (!CanChange(block)) {
      Status = $"{block.Name} can be saved when it does not run.";
      return;
    }
    var path = await fileDialogs.SaveHlFileAsync(Path.GetFileName(block.FilePath) ?? block.Name + ".hl");
    if (path == null) return;
    await Task.Run(() => { Documents.Save(block.Experiment, path); block.QuickBatchRuns.Save(path); });
    block.FilePath = path;
    block.IsModified = false;
    Remember(path);
    Status = $"Saved {block.Name} to {Path.GetFileName(path)}";
  }

  /// <summary>
  /// Saves an experiment's changes: to the file it was opened from or last saved to, or - if it has
  /// none yet - to a file chosen as with "Save as...".
  /// </summary>
  public async Task SaveChangesAsync(ExperimentBlockViewModel block) {
    if (block.FilePath == null) {
      await SaveAsync(block);
      return;
    }
    if (!CanChange(block)) {
      Status = $"{block.Name} can be saved when it does not run.";
      return;
    }
    try {
      await Task.Run(() => { Documents.Save(block.Experiment, block.FilePath); block.QuickBatchRuns.Save(block.FilePath); });
      block.IsModified = false;
      Remember(block.FilePath);
      Status = $"Saved {block.Name} to {Path.GetFileName(block.FilePath)}";
    } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
      Status = $"Could not save {block.Name}: {e.Message}";
    }
  }

  /// <summary>
  /// Renames an experiment's .hl file on disk (same folder); the experiment keeps its name. Not while
  /// anything runs, not over another file.
  /// </summary>
  internal async Task RenameFileAsync(ExperimentBlockViewModel block) {
    if (block.FilePath is not { } path || dialogs == null) return;
    if (!CanChange(block)) {
      Status = $"The file of {block.Name} can be renamed when it does not run.";
      return;
    }
    var answer = await dialogs.AskTextAsync("Rename file", $"New name of {Path.GetFileName(path)} (in {Path.GetDirectoryName(path)}):", Path.GetFileName(path));
    if (string.IsNullOrWhiteSpace(answer)) return;
    RenameFile(block, answer.Trim());
  }

  /// <summary>Renames the file to this name (".hl" added if missing); false (with a status) if that is not possible.</summary>
  public bool RenameFile(ExperimentBlockViewModel block, string newName) {
    if (block.FilePath is not { } path) return false;
    if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || newName is "." or "..") {
      Status = $"'{newName}' is not a file name.";
      return false;
    }
    if (!newName.EndsWith(".hl", StringComparison.OrdinalIgnoreCase)) newName += ".hl";
    var target = Path.Combine(Path.GetDirectoryName(path) ?? "", newName);
    if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(path), StringComparison.Ordinal)) return false;
    if (File.Exists(target)) {
      Status = $"{newName} already exists; choose another name.";
      return false;
    }
    try {
      File.Move(path, target);
      QuickBatchRuns.Move(path, target);
    } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
      Status = $"Could not rename {Path.GetFileName(path)}: {e.Message}";
      return false;
    }
    block.FilePath = target;
    var full = Path.GetFullPath(path);
    if (RecentFiles.Remove(full)) OnPropertyChanged(nameof(HasRecentFiles));
    Remember(target);
    Status = $"Renamed {Path.GetFileName(path)} to {newName}";
    return true;
  }

  /// <summary>
  /// Runs tab's "Delete runs...": the user picks an experiment, then confirms; all its runs (also those
  /// of its batch runs, time-limit runs and algorithms) are deleted. Never quick batch runs, never while running.
  /// </summary>
  public async Task DeleteRunsAsync() {
    var idle = Experiments.Where(e => !e.IsLocked).ToList();
    if (dialogs == null || Experiments.Count == 0) return;
    if (idle.Count == 0) {
      Status = "Runs can be deleted of experiments that do not run.";
      return;
    }
    // per experiment: its runs, or only its quick batch runs (Start on one algorithm); all quick batch runs at once
    var choices = new List<(string Text, Action Delete)>();
    static string Runs(int n) => $"{n} run{(n == 1 ? "" : "s")}";
    foreach (var e in idle) {
      choices.Add(($"{e.Name}: the experiment's runs  ({Runs(e.Experiment.Runs.Count)})", () => DeleteRuns(e)));
      if (e.QuickBatchRuns.Count > 0) choices.Add(($"{e.Name}: only its quick batch runs  ({Runs(e.QuickBatchRuns.Count)})", () => DeleteQuickBatchRuns(e)));
    }
    var withQbrs = idle.Where(e => e.QuickBatchRuns.Count > 0).ToList();
    if (withQbrs.Count > 1)
      choices.Add(($"All quick batch runs of all experiments  ({Runs(withQbrs.Sum(e => e.QuickBatchRuns.Count))})", () => {
        int n = withQbrs.Sum(DeleteQuickBatchRuns);
        Status = $"Deleted all {Runs(n)} of the quick batch runs";
      }));
    var picked = await dialogs.ChooseAsync("Delete runs",
      "Choose what to delete, then press \"Delete runs\": an experiment's runs (of everything inside it; its quick batch runs stay), " +
      "or only quick batch runs (QBRs, made by Start on one algorithm). The experiments themselves stay.",
      choices.Select(c => c.Text).ToList(), "Delete runs");
    if (picked == null) return;
    var statuses = new List<string>();
    foreach (int i in picked.Where(i => i >= 0 && i < choices.Count).Distinct()) {
      choices[i].Delete();
      statuses.Add(Status);
    }
    if (statuses.Count > 1) Status = string.Join("; ", statuses);
  }

  /// <summary>Deletes the quick batch runs of this experiment (Start on one algorithm); returns how many runs there were.</summary>
  public int DeleteQuickBatchRuns(ExperimentBlockViewModel experiment) {
    if (!CanChange(experiment)) return 0;
    int count = experiment.QuickBatchRuns.Count;
    experiment.QuickBatchRuns.Runs.Clear();
    if (count > 0) experiment.MarkModified();
    RefreshLive();
    Status = $"Deleted {count} quick batch run{(count == 1 ? "" : "s")} of {experiment.Name}";
    return count;
  }

  /// <summary>Deletes all runs of this experiment; returns how many there were.</summary>
  public int DeleteRuns(ExperimentBlockViewModel experiment) {
    if (!CanChange(experiment)) return 0;
    int count = ExperimentTree.DeleteRuns(experiment.Experiment);
    if (count > 0) experiment.MarkModified();
    RefreshLive();
    Status = $"Deleted {count} run{(count == 1 ? "" : "s")} of {experiment.Name}";
    return count;
  }

  /// <summary>Shows the block's runs in the Runs tab.</summary>
  internal void ShowRuns(BlockViewModel block) {
    var optimizer = block switch {
      ContainerBlockViewModel c => c.Optimizer,
      AlgorithmBlockViewModel a => (IOptimizer)a.Algorithm,
      _ => null
    };
    if (optimizer != null) showRuns(optimizer);
  }

  // ---- building blocks

  internal BlockViewModel CreateBlock(IOptimizer optimizer, ContainerBlockViewModel parent) =>
    optimizer is IAlgorithm algorithm && !ExperimentTree.IsContainer(optimizer)
      ? new AlgorithmBlockViewModel(this, parent, algorithm)
      : new ContainerBlockViewModel(this, parent, optimizer);

  internal async Task AddAlgorithmAsync(ContainerBlockViewModel container) {
    if (!CanChange(container)) return;
    if (dialogs != null && await dialogs.PickAlgorithmAsync() is CatalogEntry entry) AddAlgorithm(container, entry);
  }

  /// <summary>Adds a new algorithm (without problem) at the end of this container.</summary>
  public AlgorithmBlockViewModel AddAlgorithm(ContainerBlockViewModel container, CatalogEntry entry) {
    if (!CanChange(container)) throw new InvalidOperationException($"{container.Root?.Name} runs: nothing can be added now.");
    var algorithm = (IAlgorithm)entry.CreateInstance();
    ExperimentTree.Add(container.Optimizer, algorithm);
    var block = new AlgorithmBlockViewModel(this, container, algorithm);
    AddChild(container, block);
    return block;
  }

  /// <summary>Adds a new batch run or time-limit run at the end of this container.</summary>
  public ContainerBlockViewModel AddContainer(ContainerBlockViewModel container, CatalogEntry entry) {
    if (!CanChange(container)) throw new InvalidOperationException($"{container.Root?.Name} runs: nothing can be added now.");
    var optimizer = (IOptimizer)entry.CreateInstance();
    optimizer.Name = optimizer switch {
      BatchRun => $"Batch run {++batchCounter}",
      TimeLimitRun => $"Time-limit run {++timeLimitCounter}",
      _ => optimizer.Name
    };
    ExperimentTree.Add(container.Optimizer, optimizer);
    var block = new ContainerBlockViewModel(this, container, optimizer);
    AddChild(container, block);
    return block;
  }

  private void AddChild(ContainerBlockViewModel container, BlockViewModel block) {
    container.Children.Add(block);
    container.MarkModified();
    container.ChildrenChanged();
    Selected = block;
    RefreshState();
  }

  internal async Task ChooseProblemAsync(AlgorithmBlockViewModel block) {
    if (!CanChange(block)) return;
    if (dialogs != null && await dialogs.PickProblemAsync(block.Algorithm) is IProblem problem) SetProblem(block, problem);
  }

  public void SetProblem(AlgorithmBlockViewModel block, IProblem? problem) {
    if (!CanChange(block)) return;
    block.SetProblem(problem);
    if (block.Problem != null) Selected = block.Problem;
    RefreshState();
  }

  // ---- ejecting experiments

  /// <summary>Experiments that do not run can be ejected (the dialog offers only those).</summary>
  private bool CanEject() => Experiments.Any(e => !e.IsLocked);

  /// <summary>Asks which experiments to eject (all or ticked) and whether to autosave, then ejects them.</summary>
  [RelayCommand(CanExecute = nameof(CanEject))]
  private async Task EjectAsync() {
    if (dialogs == null || !CanEject()) return;
    // only experiments that do not run can be ejected
    var all = Experiments.Where(e => !e.IsLocked).ToList();
    var choice = await dialogs.ChooseEjectAsync(all.Select(e => e.Name).ToList(), all.Select(e => !e.IsSaved).ToList());
    if (choice == null) return;
    await EjectAsync(choice.Experiments.Where(i => i >= 0 && i < all.Count).Select(i => all[i]).ToList(), choice.Autosave);
  }

  /// <summary>
  /// Takes experiments out of the workspace (their files stay). An unsaved one is saved first with
  /// autosave (to its file, or a file chosen as with "Save as..."); without, the user decides for each:
  /// save, eject without saving, or keep it. One that could not be saved stays. Returns the ejected ones.
  /// </summary>
  public async Task<IReadOnlyList<ExperimentBlockViewModel>> EjectAsync(IReadOnlyList<ExperimentBlockViewModel> experiments, bool autosave) {
    var ejected = new List<ExperimentBlockViewModel>();
    var kept = new List<string>();
    var running = experiments.Where(e => e.IsLocked).ToList();
    experiments = experiments.Where(e => !e.IsLocked).ToList();
    if (running.Count > 0) kept.AddRange(running.Select(e => $"{e.Name} (runs)"));
    foreach (var experiment in experiments) {
      if (!experiment.IsSaved) {
        var answer = autosave ? UnsavedChoice.Save : dialogs == null ? UnsavedChoice.Keep : await dialogs.AskUnsavedAsync(experiment.Name);
        if (answer == UnsavedChoice.Save) await SaveChangesAsync(experiment);
        if (answer == UnsavedChoice.Keep || (answer == UnsavedChoice.Save && !experiment.IsSaved)) { kept.Add(experiment.Name); continue; }
      }
      if (Selected != null && IsWithin(Selected, experiment)) Selected = null;
      Experiments.Remove(experiment);
      ejected.Add(experiment);
    }
    OnPropertyChanged(nameof(HasExperiments));
    RefreshState();
    Status = $"Ejected {ejected.Count} experiment{(ejected.Count == 1 ? "" : "s")}"
             + (kept.Count > 0 ? $"; kept (not saved): {string.Join(", ", kept)}" : "") + ".";
    return ejected;
  }

  /// <summary>
  /// Deletes a block. Containers go with all their children, after a confirmation when they have
  /// any; a problem block only clears its algorithm's problem; an experiment leaves the workspace
  /// (its file stays on disk).
  /// </summary>
  public async Task DeleteAsync(BlockViewModel block) {
    if (!CanChange(block)) return;
    if (block is ContainerBlockViewModel { Children.Count: > 0 } container && dialogs != null) {
      var what = block is ExperimentBlockViewModel ? "Remove the experiment" : $"Delete {block.Name}";
      if (!await dialogs.ConfirmAsync("Delete", $"{what} and its {container.Children.Count} building block(s)?")) return;
    }
    DeleteNow(block);
  }

  private void DeleteNow(BlockViewModel block) {
    if (!CanChange(block)) return;
    block.IsPicked = false;
    if (Selected != null && IsWithin(Selected, block)) Selected = null;
    switch (block) {
      case ProblemBlockViewModel problem:
        problem.Owner.SetProblem(null);
        break;
      case ExperimentBlockViewModel experiment:
        Experiments.Remove(experiment);
        OnPropertyChanged(nameof(HasExperiments));
        break;
      default:
        var parent = block.Parent!;
        ExperimentTree.Remove(parent.Optimizer, (IOptimizer)block.Item);
        parent.MarkModified();
        parent.Children.Remove(block);
        parent.ChildrenChanged();
        break;
    }
    RefreshState();
  }

  // ---- moving (drag and drop) and copying

  /// <summary>What moves when a block is dragged: an algorithm with its problem (also when dragged by
  /// the problem), a batch run or time-limit run with everything inside; experiments stay.</summary>
  public static BlockViewModel? Movable(BlockViewModel block) => block switch {
    ProblemBlockViewModel p => p.Owner,
    ExperimentBlockViewModel => null,
    _ => block
  };

  /// <summary>Whether the block may go into this container: not into itself or its own content; a
  /// time-limit run holds one algorithm.</summary>
  public bool CanDrop(BlockViewModel block, ContainerBlockViewModel container) {
    if (!CanChange(block) || !CanChange(container) || Movable(block) is not { } moving) return false;
    if (moving is ContainerBlockViewModel c && ExperimentTree.Contains(c.Optimizer, container.Optimizer)) return false;
    return container.Optimizer is not TimeLimitRun
      || moving is AlgorithmBlockViewModel && (container.Children.Count == 0 || container.Children[0] == moving);
  }

  /// <summary>Position after the block left its old place; null when it would stay where it is.</summary>
  private static int? Landing(BlockViewModel block, DropTarget target) {
    int index = Math.Clamp(target.Index, 0, target.Container.Children.Count);
    if (block.Parent == target.Container) {
      int old = target.Container.Children.IndexOf(block);
      if (old < index) index--;
      if (index == old) return null;
    }
    return index;
  }

  /// <summary>Moves a block (with its content) to a position in a container, in the tree and in HeuristicLab's experiment.</summary>
  public bool Move(BlockViewModel block, ContainerBlockViewModel container, int index) {
    if (Movable(block) is not { Parent: not null } moving || !CanDrop(moving, container)) return false;
    if (Landing(moving, new DropTarget(container, index)) is not int at) return false;
    MoveNow(moving, container, at);
    Selected = moving;
    RefreshState();
    return true;
  }

  /// <summary>Takes a block out of its container and puts it at this position (the position after taking it out).</summary>
  private void MoveNow(BlockViewModel moving, ContainerBlockViewModel container, int at) {
    var from = moving.Parent!;
    var optimizer = (IOptimizer)moving.Item;
    from.MarkModified();
    ExperimentTree.Remove(from.Optimizer, optimizer);
    from.Children.Remove(moving);
    from.ChildrenChanged();
    ExperimentTree.Insert(container.Optimizer, at, optimizer);
    container.Children.Insert(at, moving);
    moving.Parent = container;
    if (moving is AlgorithmBlockViewModel { Problem: { } problem }) problem.Parent = container;
    container.ChildrenChanged();
    container.MarkModified();
  }

  // ---- several blocks picked (Ctrl+click, a rectangle) and moved together

  /// <summary>Every block of the workspace, in the tree's order (an algorithm before its problem).</summary>
  public IEnumerable<BlockViewModel> AllBlocks() {
    IEnumerable<BlockViewModel> Walk(BlockViewModel block) {
      yield return block;
      if (block is AlgorithmBlockViewModel { Problem: { } problem }) yield return problem;
      if (block is ContainerBlockViewModel container)
        foreach (var child in container.Children.ToList()) foreach (var b in Walk(child)) yield return b;
    }
    return Experiments.ToList().SelectMany(Walk);
  }

  /// <summary>The picked blocks that move, in tree order; a block inside a picked batch / time-limit run goes with it.</summary>
  public IReadOnlyList<BlockViewModel> PickedGroup() {
    var picked = AllBlocks().Where(b => b.IsPicked && Movable(b) == b).ToList();
    return picked.Where(b => !picked.Any(other => other != b && other is ContainerBlockViewModel c && IsWithin(b, c))).ToList();
  }

  /// <summary>What picking this block picks: a problem itself (its algorithm stays as it is), experiments nothing.</summary>
  public static BlockViewModel? Pickable(BlockViewModel block) => block is ProblemBlockViewModel ? block : Movable(block);

  /// <summary>
  /// Adds a block to the picked ones or takes it out (Ctrl+click); an algorithm takes its problem along, a problem alone
  /// is picked without its algorithm (a problem picked with its algorithm goes with it); experiments are not picked.
  /// Picked blocks belong to one experiment: a block of another one starts anew there.
  /// </summary>
  public void TogglePick(BlockViewModel block) {
    if (Pickable(block) is not { } pickable) return;
    if (pickable is ProblemBlockViewModel { Owner.IsPicked: true } problem) pickable = problem.Owner;
    if (!pickable.IsPicked) foreach (var other in AllBlocks().Where(b => b.IsPicked && b.Root != pickable.Root)) other.IsPicked = false;
    pickable.IsPicked = !pickable.IsPicked;
  }

  /// <summary>
  /// Picks these blocks (a rectangle), only those of one experiment (<paramref name="within"/>, else the first
  /// one's); <paramref name="add"/> keeps the ones picked before in that experiment.
  /// </summary>
  public void Pick(IEnumerable<BlockViewModel> blocks, bool add, ExperimentBlockViewModel? within = null) {
    var chosen = InOneExperiment(blocks, within);
    var experiment = chosen.FirstOrDefault()?.Root ?? within;
    foreach (var block in AllBlocks()) {
      bool on = chosen.Contains(block) || block is ProblemBlockViewModel p && chosen.Contains(p.Owner)
        || add && block.IsPicked && block.Root == experiment;
      if (block.IsPicked != on) block.IsPicked = on;
    }
  }

  /// <summary>
  /// The pickable blocks among these that belong to one experiment (the given one, else the first block's); a problem
  /// whose algorithm is among them goes with it, a problem alone stays alone.
  /// </summary>
  public HashSet<BlockViewModel> InOneExperiment(IEnumerable<BlockViewModel> blocks, ExperimentBlockViewModel? within) {
    var pickable = blocks.Select(Pickable).OfType<BlockViewModel>().Distinct().ToList();
    var experiment = within ?? pickable.FirstOrDefault()?.Root;
    var chosen = pickable.Where(b => b.Root == experiment).ToHashSet();
    chosen.RemoveWhere(b => b is ProblemBlockViewModel p && chosen.Contains(p.Owner));
    return chosen;
  }

  /// <summary>The blocks the rectangle being drawn covers (a blue frame on them, on an algorithm's problem too; null: none).</summary>
  public void CoverWithBand(IEnumerable<BlockViewModel>? blocks) {
    var covered = blocks == null ? [] : blocks.ToHashSet();
    foreach (var algorithm in covered.OfType<AlgorithmBlockViewModel>().ToList()) if (algorithm.Problem != null) covered.Add(algorithm.Problem);
    foreach (var block in AllBlocks()) if (block.IsBandCovered != covered.Contains(block)) block.IsBandCovered = covered.Contains(block);
  }

  /// <summary>The picked blocks changed: the menus offer the actions for all of them, or for one block again.</summary>
  internal void PicksChanged() {
    foreach (var block in AllBlocks()) block.RefreshMenu();
  }

  // ---- the menu of a picked block: delete or copy all picked ones

  /// <summary>Problems picked without their algorithms, in tree order.</summary>
  public IReadOnlyList<ProblemBlockViewModel> PickedProblems() =>
    AllBlocks().OfType<ProblemBlockViewModel>().Where(p => p.IsPicked && !p.Owner.IsPicked).ToList();

  /// <summary>Only problems are picked (no algorithm or run): their menu changes or removes all of them.</summary>
  public bool OnlyProblemsPicked() => PickedGroup().Count == 0 && PickedProblems().Count > 0;

  /// <summary>
  /// "Change selected problem blocks to...": one problem chosen in the problem dialog (offered for the first one's
  /// algorithm) goes to every picked problem's algorithm, a copy each; algorithms that cannot take it keep theirs.
  /// </summary>
  public async Task ChangeSelectedProblemsAsync() {
    var problems = PickedProblems();
    if (problems.Count == 0 || !CanChange(problems[0]) || dialogs == null) return;
    if (await dialogs.PickProblemAsync(problems[0].Owner.Algorithm) is not IProblem problem) return;
    ChangeProblems(problems.Select(p => p.Owner).ToList(), problem);
  }

  /// <summary>Gives every one of these algorithms a copy of the problem (those that take its type); returns how many took it.</summary>
  public int ChangeProblems(IReadOnlyList<AlgorithmBlockViewModel> algorithms, IProblem problem) {
    if (algorithms.Any(a => !CanChange(a))) return 0;
    ClearPicks();
    var refused = new List<string>();
    foreach (var algorithm in algorithms) {
      if (!algorithm.Algorithm.ProblemType.IsInstanceOfType(problem)) { refused.Add(algorithm.Name); continue; }
      algorithm.SetProblem((IProblem)problem.Clone());
    }
    RefreshState();
    int changed = algorithms.Count - refused.Count;
    Status = $"Changed {changed} problem(s) to {problem.Name}."
      + (refused.Count > 0 ? $" Not for {string.Join(", ", refused)}: the algorithm cannot take a {problem.ItemName}." : "");
    return changed;
  }

  /// <summary>Deletes all picked blocks (asked once); only problems picked: removes those problems.</summary>
  public async Task DeleteSelectedAsync() {
    if (OnlyProblemsPicked()) {
      var problems = PickedProblems();
      if (!CanChange(problems[0])) return;
      if (dialogs != null && !await dialogs.ConfirmAsync("Remove problems", $"Remove the {problems.Count} selected problems from their algorithms?")) return;
      foreach (var problem in problems) DeleteNow(problem);
      Status = $"Removed {problems.Count} problems.";
      return;
    }
    var group = PickedGroup();
    if (group.Count == 0 || !CanChange(group[0])) return;
    if (dialogs != null && !await dialogs.ConfirmAsync("Delete", $"Delete the {group.Count} selected building blocks (with what is inside them)?")) return;
    foreach (var block in group) DeleteNow(block);
    Status = $"Deleted {group.Count} building blocks.";
  }

  /// <summary>Each picked block can be copied (none inside a time-limit run).</summary>
  public bool CanCopySelected() => PickedGroup() is { Count: > 0 } group && group.All(CanCopy);

  /// <summary>Copies every picked block <paramref name="count"/> times, the copies right after each one.</summary>
  public void CopySelected(int count) {
    var group = PickedGroup();
    if (count < 1 || !CanCopySelected() || !CanChange(group[0])) return;
    ClearPicks();
    foreach (var block in group) Copy(block, count);
    Status = count == 1 ? $"Copied {group.Count} building blocks." : $"Made {count} copies each of {group.Count} building blocks.";
  }

  internal async Task CopySelectedManyAsync() {
    var group = PickedGroup();
    if (dialogs != null && CanCopySelected()
        && await dialogs.AskNumberAsync("Copy n times", $"How many copies of each of the {group.Count} selected building blocks?", 2, 1, MaxCopies) is int n)
      CopySelected(n);
  }

  public void ClearPicks() {
    foreach (var block in AllBlocks()) if (block.IsPicked) block.IsPicked = false;
  }

  /// <summary>What a drag moves: all picked blocks when the dragged one is picked, else just it.</summary>
  private IReadOnlyList<BlockViewModel> DraggedGroup =>
    Dragged is { IsPicked: true } && PickedGroup() is { Count: > 1 } group ? group : Dragged != null ? [Dragged] : [];

  /// <summary>Moves several blocks to a position, one after the other, in their tree order.</summary>
  public bool MoveGroup(IReadOnlyList<BlockViewModel> group, ContainerBlockViewModel container, int index) {
    if (group.Count == 1) return Move(group[0], container, index);
    if (group.Any(b => !CanDrop(b, container)) || container.Optimizer is TimeLimitRun) return false;
    int at = Math.Clamp(index, 0, container.Children.Count);
    foreach (var block in group) {
      if (block.Parent == container && container.Children.IndexOf(block) < at) at--;
      MoveNow(block, container, at);
      at = container.Children.IndexOf(block) + 1;
    }
    Selected = group[^1];
    RefreshState();
    return true;
  }

  /// <summary>The block being dragged (null when nothing is).</summary>
  public BlockViewModel? Dragged { get; private set; }

  /// <summary>Where the dragged block would land now (shown faded there); null: nowhere.</summary>
  public DropTarget? DropPreview { get; private set; }

  public bool BeginDrag(BlockViewModel block) {
    EndDrag(false);
    Dragged = CanChange(block) ? Movable(block) : null;
    return Dragged != null;
  }

  /// <summary>Drop target for the pointer over a block: the upper half of a row means before it, the
  /// lower half after it - or, for a batch / time-limit run that takes it, first inside it.</summary>
  public DropTarget? TargetAt(BlockViewModel block, bool upperHalf) {
    if (block is ProblemBlockViewModel p) block = p.Owner;
    if (block is ExperimentBlockViewModel experiment) return new DropTarget(experiment, 0);
    if (block.Parent is not { } parent) return null;
    int index = parent.Children.IndexOf(block);
    if (upperHalf) return new DropTarget(parent, index);
    if (block is ContainerBlockViewModel container && Dragged != null && CanDrop(Dragged, container)) return new DropTarget(container, 0);
    return new DropTarget(parent, index + 1);
  }

  /// <summary>Shows the dragged block faded at the target (or nowhere if it cannot or would not move there).</summary>
  public void PreviewDrop(DropTarget? target) {
    var group = DraggedGroup;
    if (Dragged == null || target != null && !Fits(group, target)) target = null;
    if (target == DropPreview) return;
    DropPreview?.Container.SetPreview(null, null);
    DropPreview = target;
    target?.Container.SetPreview(target.Index, Dragged);
    // only one of the two at a time: the faded preview at the new place, or the blocks at their old places
    foreach (var block in group) block.IsMoving = target != null;
  }

  private bool Fits(IReadOnlyList<BlockViewModel> group, DropTarget target) =>
    group.Count == 1 ? CanDrop(group[0], target.Container) && Landing(group[0], target) != null
    : target.Container.Optimizer is not TimeLimitRun && group.All(b => CanDrop(b, target.Container));

  /// <summary>Ends dragging: drops the block where the preview shows it, or (cancelled) leaves it.</summary>
  public bool EndDrag(bool drop) {
    var target = DropPreview;
    var block = Dragged;
    var group = DraggedGroup;
    PreviewDrop(null);
    foreach (var b in group) b.IsMoving = false;
    Dragged = null;
    return drop && block != null && target != null && MoveGroup(group, target.Container, target.Index);
  }

  /// <summary>A copy goes next to the original: not for experiments, nor inside a time-limit run (it holds one algorithm).</summary>
  public bool CanCopy(BlockViewModel block) => Movable(block) is { Parent: { } parent } && parent.Optimizer is not TimeLimitRun;

  /// <summary>
  /// Copies of a block right after it, each independent (own problem, parameters, content) and unrun,
  /// named "Name (copy k)".
  /// </summary>
  public System.Collections.Generic.IReadOnlyList<BlockViewModel> Copy(BlockViewModel block, int count) {
    if (!CanChange(block) || count < 1 || !CanCopy(block) || Movable(block) is not { Parent: { } parent } original) return [];
    int index = parent.Children.IndexOf(original);
    string baseName = Regex.Replace(original.Name, @" \(copy \d+\)$", "");
    var names = parent.Children.Select(c => c.Name).ToHashSet();
    int k = 0;
    var copies = new System.Collections.Generic.List<BlockViewModel>();
    for (int i = 1; i <= count; i++) {
      var copy = ExperimentTree.Copy((IOptimizer)original.Item);
      do k++; while (names.Contains($"{baseName} (copy {k})"));
      copy.Name = $"{baseName} (copy {k})";
      ExperimentTree.Insert(parent.Optimizer, index + i, copy);
      var copyBlock = CreateBlock(copy, parent);
      parent.Children.Insert(index + i, copyBlock);
      copies.Add(copyBlock);
    }
    parent.ChildrenChanged();
    parent.MarkModified();
    Selected = copies[^1];
    RefreshState();
    Status = count == 1 ? $"Copied {original.Name}." : $"Made {count} copies of {original.Name}.";
    return copies;
  }

  public const int MaxCopies = 1000;

  internal async Task CopyManyAsync(BlockViewModel block) {
    if (dialogs != null && CanCopy(block)
        && await dialogs.AskNumberAsync("Copy n times", $"How many copies of {Movable(block)!.Name}?", 2, 1, MaxCopies) is int n)
      Copy(block, n);
  }

  private static bool IsWithin(BlockViewModel candidate, BlockViewModel ancestor) {
    for (BlockViewModel? b = candidate is ProblemBlockViewModel p ? p.Owner : candidate; b != null; b = b.Parent)
      if (b == ancestor || candidate == ancestor) return true;
    return false;
  }

  // ---- starting

  /// <summary>Recomputes what is missing per experiment and the start buttons' state.</summary>
  public void RefreshState() {
    foreach (var e in Experiments) e.Missing = string.Join(" ", ExperimentTree.Problems(e.Experiment));
    RefreshMultiEdit();
    StartAllCommand.NotifyCanExecuteChanged();
    StartSelectedCommand.NotifyCanExecuteChanged();
    EjectCommand.NotifyCanExecuteChanged();
  }

  /// <summary>"Start all experiments": every experiment that does not run yet (the running ones go on).</summary>
  [RelayCommand(CanExecute = nameof(CanStartAll))]
  private Task StartAllAsync() => StartAsync(Experiments.Where(e => !e.IsLocked).ToList());
  private bool CanStartAll() => CanStart(Experiments.Where(e => !e.IsLocked).ToList());

  /// <summary>"Start selected experiments": the ticked ones that do not run yet.</summary>
  [RelayCommand(CanExecute = nameof(CanStartSelected))]
  private Task StartSelectedAsync() => StartAsync(Experiments.Where(e => e.IsChecked && !e.IsLocked).ToList());
  private bool CanStartSelected() => CanStart(Experiments.Where(e => e.IsChecked && !e.IsLocked).ToList());

  private static bool CanStart(System.Collections.Generic.IReadOnlyList<ExperimentBlockViewModel> experiments) =>
    experiments.Count > 0 && experiments.All(e => e.IsReady);

  [RelayCommand(CanExecute = nameof(IsRunningExperiments))]
  private void Stop() {
    // what is stopped now did not finish (status "stopped before it finished", not "finished")
    foreach (var a in AllBlocks(Experiments.Where(e => e.IsRunning)).OfType<AlgorithmBlockViewModel>()) a.StopRequested();
    stopSource?.Cancel();
  }

  /// <summary>
  /// Runs the experiments at the same time, each through OptimizerRunner. No seed is imposed:
  /// a fixed seed would make every repetition of a batch run identical, so each algorithm keeps
  /// its own seed setting (random by default).
  /// </summary>
  private async Task StartAsync(System.Collections.Generic.IReadOnlyList<ExperimentBlockViewModel> experiments) {
    experiments = experiments.Where(e => !e.IsLocked).ToList();
    if (experiments.Count == 0) return;
    // runs started while others run share their Stop and the Resources tab's limit of experiments at the same time
    if (experimentRuns == 0 || stopSource is not { IsCancellationRequested: false }) {
      stopSource?.Dispose();
      stopSource = new CancellationTokenSource();
    }
    if (experimentRuns == 0 || slots == null) {
      int concurrent = Resources.Current.ConcurrentExperiments;
      slots = new SemaphoreSlim(concurrent > 0 ? concurrent : 4096);
    }
    var slotGate = slots;
    var token = stopSource.Token;
    experimentRuns++;
    UpdateRunning();
    Status = experiments.Count == 1 ? $"Running {experiments[0].Name} ..." : $"Running {experiments.Count} experiments ...";
    try {
      foreach (var e in experiments) {
        e.IsRunning = true;
        LockChanged(e);
      }
      var started = await Task.WhenAll(experiments.Select(async e => {
        e.RunStatus = "Waiting for a free slot ...";
        try {
          await slotGate.WaitAsync(token);
        } catch (OperationCanceledException) {
          e.RunStatus = "Not started (stopped)";
          e.IsRunning = false;
          LockChanged(e);
          return null;
        }
        try {
          Resources.Configure(e.Experiment);
          e.RunStatus = "Running ...";
          var progress = new Progress<RunProgress>(p => {
            if (e.RunStatus.StartsWith("Running", StringComparison.Ordinal)) e.RunStatus = $"Running ... {p.Elapsed:hh\\:mm\\:ss}";
          });
          var report = await OptimizerRunner.RunAsync(e.Experiment,
            new RunOptions { ProgressInterval = TimeSpan.FromMilliseconds(500), Labels = new System.Collections.Generic.Dictionary<string, string> { ["experiment"] = e.Name } },
            e.FilePath, progress, token);
          e.RunStatus = report.Outcome switch {
            RunOutcome.Completed => $"Completed: {report.Runs.Count} runs in {TimeSpan.FromSeconds(report.ExecutionSeconds):hh\\:mm\\:ss}",
            RunOutcome.Stopped => $"Stopped: {report.Runs.Count} runs",
            _ => $"Failed: {report.Error?.Split('\n')[0]}"
          };
          Resources.Tag(Resources.RunsOf(e.Experiment));
          return report;
        } finally {
          e.IsRunning = false;
          slotGate.Release();
          LockChanged(e);
        }
      }));
      var reports = started.OfType<RunReport>().ToArray();
      Status = $"{reports.Count(r => r.Outcome == RunOutcome.Completed)} of {reports.Length} experiments completed, " +
               $"{reports.Sum(r => r.Runs.Count)} runs";
      NotifyFinished(experiments.Count == 1 ? $"{experiments[0].Name} finished" : $"{experiments.Count} experiments finished",
        experiments.Count == 1 ? experiments[0].RunStatus : Status);
    } finally {
      experimentRuns--;
      UpdateRunning();
      RefreshLive();
      RefreshState();
      RunsFinished?.Invoke();
    }
  }

  private static System.Collections.Generic.IEnumerable<BlockViewModel> AllBlocks(System.Collections.Generic.IEnumerable<BlockViewModel> blocks) =>
    blocks.SelectMany(b => b is ContainerBlockViewModel c ? AllBlocks(c.Children).Prepend(b) : [b]);

  // ---- formatting

  public static string DisplayName(CatalogEntry entry) =>
    entry.Type == typeof(BatchRun) ? "Batch run" : entry.Type == typeof(TimeLimitRun) ? "Time-limit run" : entry.Name;

  public static string FormatDuration(TimeSpan t) =>
    t.TotalSeconds < 60 && t.TotalSeconds == Math.Floor(t.TotalSeconds) ? $"{t.TotalSeconds:0} s"
    : t.TotalMinutes < 60 && t.Seconds == 0 && t.Milliseconds == 0 ? $"{t.TotalMinutes:0} min"
    : t.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

  /// <summary>"90", "90s", "5m", "5 min", "1h" or "hh:mm:ss"; null if not a duration.</summary>
  public static TimeSpan? ParseDuration(string text) {
    text = text.Trim();
    var m = Regex.Match(text, @"^(\d+(?:\.\d+)?)\s*(s|sec|m|min|h)?$", RegexOptions.IgnoreCase);
    if (m.Success) {
      var value = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
      return m.Groups[2].Value.ToLowerInvariant() switch {
        "m" or "min" => TimeSpan.FromMinutes(value),
        "h" => TimeSpan.FromHours(value),
        _ => TimeSpan.FromSeconds(value)
      };
    }
    return TimeSpan.TryParseExact(text, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out var span) ? span : null;
  }
}
