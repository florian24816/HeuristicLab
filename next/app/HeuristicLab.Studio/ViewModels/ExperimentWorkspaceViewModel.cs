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
    if (block.Root is not { } root || !IsEditable) return false;
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

  /// <summary>Experiments or a single algorithm are running (or paused): the tree cannot change.</summary>
  [ObservableProperty]
  [NotifyCanExecuteChangedFor(nameof(StartAllCommand), nameof(StartSelectedCommand), nameof(StopCommand))]
  [NotifyPropertyChangedFor(nameof(IsEditable))]
  public partial bool IsRunning { get; set; }

  /// <summary>Experiments started with the start buttons are running.</summary>
  [ObservableProperty]
  [NotifyCanExecuteChangedFor(nameof(StopCommand))]
  public partial bool IsRunningExperiments { get; set; }

  private bool algorithmRunning;

  /// <summary>An algorithm started from its detail view runs or is paused.</summary>
  public void SetAlgorithmRunning(bool running) {
    algorithmRunning = running;
    IsRunning = IsRunningExperiments || algorithmRunning;
  }

  partial void OnIsRunningChanged(bool value) {
    // blocks stay clickable while running, but their menus offer no changes
    foreach (var block in AllBlocks(Experiments)) {
      block.RefreshMenu();
      if (block is AlgorithmBlockViewModel { Problem: { } problem }) problem.RefreshMenu();
    }
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

  /// <summary>Building blocks cannot change while experiments run.</summary>
  public bool IsEditable => !IsRunning;

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
    if (fileDialogs != null && await fileDialogs.OpenHlFileAsync() is string path) await OpenExperimentAsync(path);
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
    Experiments.Add(block);
    Selected = block;
    OnPropertyChanged(nameof(HasExperiments));
    RefreshState();
    return block;
  }

  internal async Task SaveAsync(ExperimentBlockViewModel block) {
    if (fileDialogs == null) return;
    if (!IsEditable) {
      Status = "An experiment can be saved when nothing runs.";
      return;
    }
    var path = await fileDialogs.SaveHlFileAsync(Path.GetFileName(block.FilePath) ?? block.Name + ".hl");
    if (path == null) return;
    await Task.Run(() => Documents.Save(block.Experiment, path));
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
    if (!IsEditable) {
      Status = "An experiment can be saved when nothing runs.";
      return;
    }
    try {
      await Task.Run(() => Documents.Save(block.Experiment, block.FilePath));
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
    if (!IsEditable) {
      Status = "A file can be renamed when nothing runs.";
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
    if (dialogs != null && await dialogs.PickAlgorithmAsync() is CatalogEntry entry) AddAlgorithm(container, entry);
  }

  /// <summary>Adds a new algorithm (without problem) at the end of this container.</summary>
  public AlgorithmBlockViewModel AddAlgorithm(ContainerBlockViewModel container, CatalogEntry entry) {
    var algorithm = (IAlgorithm)entry.CreateInstance();
    ExperimentTree.Add(container.Optimizer, algorithm);
    var block = new AlgorithmBlockViewModel(this, container, algorithm);
    AddChild(container, block);
    return block;
  }

  /// <summary>Adds a new batch run or time-limit run at the end of this container.</summary>
  public ContainerBlockViewModel AddContainer(ContainerBlockViewModel container, CatalogEntry entry) {
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
    if (!IsEditable) return;
    if (dialogs != null && await dialogs.PickProblemAsync(block.Algorithm) is IProblem problem) SetProblem(block, problem);
  }

  public void SetProblem(AlgorithmBlockViewModel block, IProblem? problem) {
    if (IsRunning) return;
    block.SetProblem(problem);
    if (block.Problem != null) Selected = block.Problem;
    RefreshState();
  }

  /// <summary>
  /// Deletes a block. Containers go with all their children, after a confirmation when they have
  /// any; a problem block only clears its algorithm's problem; an experiment leaves the workspace
  /// (its file stays on disk).
  /// </summary>
  public async Task DeleteAsync(BlockViewModel block) {
    if (IsRunning) return;
    if (block is ContainerBlockViewModel { Children.Count: > 0 } container && dialogs != null) {
      var what = block is ExperimentBlockViewModel ? "Remove the experiment" : $"Delete {block.Name}";
      if (!await dialogs.ConfirmAsync("Delete", $"{what} and its {container.Children.Count} building block(s)?")) return;
    }
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
    if (!IsEditable || Movable(block) is not { } moving) return false;
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
    if (Movable(block) is not { Parent: { } from } moving || !CanDrop(moving, container)) return false;
    if (Landing(moving, new DropTarget(container, index)) is not int at) return false;
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
    Selected = moving;
    RefreshState();
    return true;
  }

  /// <summary>The block being dragged (null when nothing is).</summary>
  public BlockViewModel? Dragged { get; private set; }

  /// <summary>Where the dragged block would land now (shown faded there); null: nowhere.</summary>
  public DropTarget? DropPreview { get; private set; }

  public bool BeginDrag(BlockViewModel block) {
    EndDrag(false);
    Dragged = IsEditable ? Movable(block) : null;
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
    if (Dragged == null || target != null && (!CanDrop(Dragged, target.Container) || Landing(Dragged, target) == null)) target = null;
    if (target == DropPreview) return;
    DropPreview?.Container.SetPreview(null, null);
    DropPreview = target;
    target?.Container.SetPreview(target.Index, Dragged);
    // only one of the two at a time: the faded preview at the new place, or the block at its old place
    if (Dragged != null) Dragged.IsMoving = target != null;
  }

  /// <summary>Ends dragging: drops the block where the preview shows it, or (cancelled) leaves it.</summary>
  public bool EndDrag(bool drop) {
    var target = DropPreview;
    var block = Dragged;
    PreviewDrop(null);
    if (block != null) block.IsMoving = false;
    Dragged = null;
    return drop && block != null && target != null && Move(block, target.Container, target.Index);
  }

  /// <summary>A copy goes next to the original: not for experiments, nor inside a time-limit run (it holds one algorithm).</summary>
  public bool CanCopy(BlockViewModel block) => Movable(block) is { Parent: { } parent } && parent.Optimizer is not TimeLimitRun;

  /// <summary>
  /// Copies of a block right after it, each independent (own problem, parameters, content) and unrun,
  /// named "Name (copy k)".
  /// </summary>
  public System.Collections.Generic.IReadOnlyList<BlockViewModel> Copy(BlockViewModel block, int count) {
    if (!IsEditable || count < 1 || !CanCopy(block) || Movable(block) is not { Parent: { } parent } original) return [];
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
  }

  [RelayCommand(CanExecute = nameof(CanStartAll))]
  private Task StartAllAsync() => StartAsync(Experiments.ToList());
  private bool CanStartAll() => CanStart(Experiments.ToList());

  [RelayCommand(CanExecute = nameof(CanStartSelected))]
  private Task StartSelectedAsync() => StartAsync(Experiments.Where(e => e.IsChecked).ToList());
  private bool CanStartSelected() => CanStart(Experiments.Where(e => e.IsChecked).ToList());

  private bool CanStart(System.Collections.Generic.IReadOnlyList<ExperimentBlockViewModel> experiments) =>
    !IsRunning && experiments.Count > 0 && experiments.All(e => e.IsReady);

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
    IsRunningExperiments = true;
    IsRunning = true;
    stopSource = new CancellationTokenSource();
    Status = experiments.Count == 1 ? $"Running {experiments[0].Name} ..." : $"Running {experiments.Count} experiments ...";
    try {
      // the Resources tab may limit how many experiments run at the same time
      int concurrent = Resources.Current.ConcurrentExperiments;
      using var slots = new SemaphoreSlim(concurrent > 0 ? concurrent : Math.Max(1, experiments.Count));
      var token = stopSource.Token;
      foreach (var e in experiments) e.IsRunning = true;
      RefreshLive();
      var started = await Task.WhenAll(experiments.Select(async e => {
        e.RunStatus = "Waiting for a free slot ...";
        try {
          await slots.WaitAsync(token);
        } catch (OperationCanceledException) {
          e.RunStatus = "Not started (stopped)";
          e.IsRunning = false;
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
          slots.Release();
        }
      }));
      var reports = started.OfType<RunReport>().ToArray();
      Status = $"{reports.Count(r => r.Outcome == RunOutcome.Completed)} of {reports.Length} experiments completed, " +
               $"{reports.Sum(r => r.Runs.Count)} runs";
    } finally {
      IsRunningExperiments = false;
      IsRunning = algorithmRunning;
      RefreshLive();
      stopSource.Dispose();
      stopSource = null;
      RefreshState();
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
