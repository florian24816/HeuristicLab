using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeuristicLab.Core;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Next.Runtime.Runs;
using HeuristicLab.Optimization;
using HeuristicLab.Studio.Services;

namespace HeuristicLab.Studio.ViewModels;

/// <summary>Where runs come from: an experiment, batch run, time-limit run or algorithm of the workspace, or a results folder.</summary>
public partial class RunSourceViewModel(RunAnalysisViewModel owner, string name, string kind, IOptimizer? optimizer, string? folder) : ViewModelBase, IPickable {
  /// <summary>Selected (one of several, shown inverted) for "Tick selected" / "Untick selected" / "Only selected".</summary>
  [ObservableProperty]
  public partial bool IsSelected { get; set; }

  partial void OnIsSelectedChanged(bool value) => owner.SelectionChanged();

  public string Name { get; } = name;
  public string Kind { get; } = kind;
  public IOptimizer? Optimizer { get; } = optimizer;
  public string? Folder { get; } = folder;
  public ObservableCollection<RunSourceViewModel> Children { get; } = [];
  public RunSourceViewModel? Parent { get; init; }

  /// <summary>A part of the "Single runs" folder: what it stands for (kept ticked over a refresh) and its runs.</summary>
  public object? Group { get; init; }
  public Func<IReadOnlyList<IRun>>? RunList { get; init; }

  /// <summary>The runs it holds (also those of the blocks below it); null for a results folder.</summary>
  public IReadOnlyList<IRun>? RunsHere => Optimizer != null ? Optimizer.Runs.ToArray() : RunList?.Invoke();

  public int RunCount => RunsHere?.Count ?? owner.FolderRunCount(Folder);
  public string Label => $"{Name}  ({RunCount} run{(RunCount == 1 ? "" : "s")})";

  /// <summary>Whether the runs made here (not below) are shown.</summary>
  internal bool Own { get; set; }

  /// <summary>
  /// Ticked: everything here and below is shown; empty: nothing; partly (null): some of it. Ticking or
  /// unticking applies to everything below; a partly ticked box becomes ticked.
  /// </summary>
  public bool? IsChecked {
    get {
      bool any = false, all = true;
      foreach (var node in Subtree().Where(n => n.Counts)) { any |= node.Own; all &= node.Own; }
      return all ? true : any ? null : false;
    }
    set {
      bool on = value != false;
      foreach (var node in Subtree()) node.Own = on;
      owner.TicksChanged();
    }
  }

  /// <summary>
  /// Whether its own tick shows in the boxes: only blocks without children hold runs of their own
  /// (experiments and batch runs mirror their children's runs, a time-limit run's runs are its algorithm's).
  /// </summary>
  internal bool Counts => Children.Count == 0;

  public IEnumerable<RunSourceViewModel> Subtree() => Children.SelectMany(c => c.Subtree()).Prepend(this);

  internal void NotifyChecked() => OnPropertyChanged(nameof(IsChecked));

  public string Path => Parent == null ? Name : Parent.Path + " / " + Name;
}

public sealed record ColumnChoice(string Key, string Display) {
  public override string ToString() => Display;
}

public partial class FilterViewModel(RunAnalysisViewModel owner) : ViewModelBase {
  [ObservableProperty]
  public partial ColumnChoice? Column { get; set; }

  [ObservableProperty]
  public partial string Op { get; set; } = "=";

  [ObservableProperty]
  public partial string Value { get; set; } = "";

  /// <summary>The operators that make sense for the chosen column (RunFilter.OperatorsFor); replaced only when they change.</summary>
  [ObservableProperty]
  public partial IReadOnlyList<string> Operators { get; set; } = RunFilter.TextOperators;

  /// <summary>What to type, by the column's kind.</summary>
  [ObservableProperty]
  public partial string ValueHint { get; set; } = "text";

  public RunAnalysisViewModel Owner => owner;

  /// <summary>Offers the operators of the column's kind; an operator that does not fit becomes the first one ("=").</summary>
  public void FitOperators(RunColumn? column) {
    var operators = RunFilter.OperatorsFor(column);
    if (!operators.SequenceEqual(Operators)) Operators = operators;
    ValueHint = column == null ? "value" : column.IsNumeric ? "number" : column.IsYesNo ? "True or False" : "text";
    if (!Operators.Contains(Op)) Op = Operators[0];
  }

  /// <summary>
  /// The column chosen last. A column list replaced under the ComboBox makes it report no selection
  /// for a moment; the filter keeps its column then (RunAnalysisViewModel.Rebuild shows it again).
  /// </summary>
  public string? ColumnKey { get; private set; }

  partial void OnColumnChanged(ColumnChoice? value) {
    if (value == null) return;  // the list was replaced, not a choice: keep the column
    ColumnKey = value.Key;
    owner.Rebuild();
  }
  partial void OnOpChanged(string value) {
    if (value == null) return;  // the operator list was replaced, not a choice
    owner.Rebuild();
  }
  partial void OnValueChanged(string value) => owner.Rebuild();

  public RunFilter? Filter => ColumnKey == null || Value.Length == 0 ? null
    : new RunFilter(ColumnKey, Op != null && Operators.Contains(Op) ? Op : Operators[0], Value);
}

public sealed record AnalysisRow(RunRow Row, IReadOnlyList<string> Cells);

public sealed record SummaryLine(string Group, string Count, string Mean, string StdDev, string Min, string Q1, string Median, string Q3, string Max);

public sealed record PairLine(string Pair, string MannWhitney, string Adjusted, string TTest, string CohensD, string HedgesG, bool Significant);

/// <summary>
/// "Results" tab: the runs of any experiments, batch runs, time-limit runs and algorithms of the
/// workspace (and of results folders) as one table, filtered and grouped by any combination of
/// columns, and compared in charts (box plots, scatter, histograms, cumulative distributions,
/// curves of every run) and with statistical tests - HeuristicLab's run collection views, with
/// grouping as a first-class choice instead of a color or modifier.
/// </summary>
public partial class RunAnalysisViewModel : ViewModelBase {
  private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
  private readonly ExperimentWorkspaceViewModel workspace;
  private readonly IFileDialogService? fileDialogs;
  private readonly List<string> folders = [];
  private readonly Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> folderRows = [];
  private bool rebuilding;

  public RunAnalysisViewModel(ExperimentWorkspaceViewModel workspace, IFileDialogService? fileDialogs = null) {
    this.workspace = workspace;
    this.fileDialogs = fileDialogs;
  }

  public ObservableCollection<RunSourceViewModel> Sources { get; } = [];

  public const int TableView = 0, ChartsView = 1, StatisticsView = 2, RunView = 3;

  /// <summary>Table, Charts, Statistics or Run.</summary>
  [ObservableProperty]
  public partial int SelectedView { get; set; }

  [ObservableProperty]
  public partial RunTable Table { get; set; } = new([]);

  [ObservableProperty]
  public partial string Status { get; set; } = "Tick the experiments, batch runs, time-limit runs or algorithms whose runs you want to analyse.";

  public ObservableCollection<FilterViewModel> Filters { get; } = [];

  /// <summary>Columns grouped by, in order (several: every combination is a group).</summary>
  public ObservableCollection<ColumnChoice> GroupBy { get; } = [];

  [ObservableProperty]
  public partial decimal Bins { get; set; } = 5;

  [ObservableProperty]
  public partial IReadOnlyList<ColumnChoice> AllColumns { get; set; } = [];

  [ObservableProperty]
  public partial IReadOnlyList<ColumnChoice> NumericColumns { get; set; } = [];

  [ObservableProperty]
  public partial ColumnChoice? GroupToAdd { get; set; }

  public IReadOnlyList<RunGroup> Groups { get; private set; } = [];

  public int FolderRunCount(string? folder) => folder != null && folderRows.TryGetValue(folder, out var rows) ? rows.Count : 0;

  // ---- sources

  /// <summary>Re-reads the workspace (new experiments, finished runs); ticked sources stay ticked.</summary>
  [RelayCommand]
  public void RefreshSources() {
    var before = AllSources(Sources).Where(s => Key(s) != null).ToDictionary(s => Key(s)!, s => s.Own, ReferenceEqualityComparer.Instance);
    rebuilding = true;
    Sources.Clear();
    foreach (var experiment in workspace.Experiments) Sources.Add(Node(experiment, null, before));
    if (workspace.Experiments.Count > 0) Sources.Add(SingleRunsNode(before));
    foreach (var folder in folders) {
      var node = new RunSourceViewModel(this, Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)), "Results folder", null, folder) {
        Own = before.GetValueOrDefault(folder)
      };
      Sources.Add(node);
    }
    rebuilding = false;
    TicksChanged();
  }

  private static object? Key(RunSourceViewModel s) => (object?)s.Optimizer ?? s.Group ?? s.Folder;

  /// <summary>
  /// "Single runs" (made by Start on one algorithm, kept apart from the experiments): per experiment and
  /// algorithm, not ticked at first - ticked, they are compared like any other runs.
  /// </summary>
  private RunSourceViewModel SingleRunsNode(IReadOnlyDictionary<object, bool> before) {
    var experiments = workspace.Experiments.ToList();
    var folder = new RunSourceViewModel(this, "Single runs", "Started on one algorithm", null, null) {
      Group = RunsBrowserViewModel.SingleRunsKey, RunList = () => experiments.SelectMany(e => e.SingleRuns.Runs).ToList(),
      Own = before.GetValueOrDefault(RunsBrowserViewModel.SingleRunsKey)
    };
    foreach (var experiment in experiments.Where(e => e.SingleRuns.Count > 0)) {
      var single = experiment.SingleRuns;
      var node = new RunSourceViewModel(this, experiment.Name, "Experiment", null, null) {
        Parent = folder, Group = single, RunList = () => single.Runs.ToArray(), Own = before.GetValueOrDefault(single)
      };
      foreach (var group in single.Groups)
        node.Children.Add(new RunSourceViewModel(this, group.Algorithm, "Algorithm", null, null) {
          Parent = node, Group = group, RunList = () => group.Runs, Own = before.GetValueOrDefault(group)
        });
      folder.Children.Add(node);
    }
    return folder;
  }

  /// <summary>Ticks stay as they were; a block that is new here takes its parent's (added to a ticked batch run: ticked).</summary>
  private RunSourceViewModel Node(BlockViewModel block, RunSourceViewModel? parent, IReadOnlyDictionary<object, bool> before) {
    var optimizer = block switch { ContainerBlockViewModel c => c.Optimizer, AlgorithmBlockViewModel a => (IOptimizer)a.Algorithm, _ => null };
    string kind = optimizer switch { Experiment => "Experiment", BatchRun => "Batch run", TimeLimitRun => "Time-limit run", _ => "Algorithm" };
    var node = new RunSourceViewModel(this, block.Name, kind, optimizer, null) {
      Parent = parent,
      Own = optimizer != null && before.TryGetValue(optimizer, out var own) ? own : parent?.IsChecked == true
    };
    if (block is ContainerBlockViewModel container)
      foreach (var child in container.Children) node.Children.Add(Node(child, node, before));
    return node;
  }

  // ---- several sources at once: select them (Ctrl+click, Shift+click, press and sweep), then tick or untick

  public IEnumerable<RunSourceViewModel> AllSourceNodes => AllSources(Sources);

  public bool HasSelection => AllSources(Sources).Any(s => s.IsSelected);

  internal void SelectionChanged() {
    OnPropertyChanged(nameof(HasSelection));
    TickSelectedCommand.NotifyCanExecuteChanged();
    UntickSelectedCommand.NotifyCanExecuteChanged();
    OnlySelectedCommand.NotifyCanExecuteChanged();
  }

  /// <summary>Sets the selected sources (with everything below them) to ticked or unticked; with <paramref name="others"/> the rest too.</summary>
  private void TickSelection(bool on, bool? others = null) {
    var selected = AllSources(Sources).Where(s => s.IsSelected).SelectMany(s => s.Subtree()).ToHashSet();
    foreach (var node in AllSources(Sources)) {
      if (selected.Contains(node)) node.Own = on;
      else if (others is bool rest) node.Own = rest;
    }
    TicksChanged();
  }

  [RelayCommand(CanExecute = nameof(HasSelection))]
  private void TickSelected() => TickSelection(true);

  [RelayCommand(CanExecute = nameof(HasSelection))]
  private void UntickSelected() => TickSelection(false);

  /// <summary>Ticks the selected sources and unticks everything else.</summary>
  [RelayCommand(CanExecute = nameof(HasSelection))]
  private void OnlySelected() => TickSelection(true, others: false);

  /// <summary>A box was ticked or unticked: every box shows its state again, and the runs follow.</summary>
  public void TicksChanged() {
    if (rebuilding) return;
    foreach (var node in AllSources(Sources)) node.NotifyChecked();
    NotifyTickStates();
    Rebuild();
  }

  // ---- ticking all, all but the single runs, none; a results window copies the main tab's ticks

  /// <summary>The main Results tab, for a results window (its "Copy selection from main results tab"); null for the main tab.</summary>
  public RunAnalysisViewModel? Main { get; init; }
  public bool IsWindow => Main != null;

  /// <summary>Whether the box is the "Single runs" folder or below it.</summary>
  private static bool IsSingle(RunSourceViewModel node) {
    for (var n = node; n != null; n = n.Parent) if (ReferenceEquals(n.Group, RunsBrowserViewModel.SingleRunsKey)) return true;
    return false;
  }

  /// <summary>The boxes whose own tick counts (no children).</summary>
  private List<RunSourceViewModel> Leaves() => AllSources(Sources).Where(s => s.Counts).ToList();

  /// <summary>Everything ticked (the button "Select all" shows it in full orange).</summary>
  public bool AllTicked => Leaves() is { Count: > 0 } leaves && leaves.All(s => s.Own);

  /// <summary>Everything but the single runs ticked.</summary>
  public bool AllButSingleTicked {
    get {
      var leaves = Leaves();
      var experiments = leaves.Where(s => !IsSingle(s)).ToList();
      return experiments.Count > 0 && experiments.All(s => s.Own) && leaves.Where(IsSingle).All(s => !s.Own);
    }
  }

  /// <summary>Nothing ticked.</summary>
  public bool NoneTicked => Leaves().All(s => !s.Own);

  private void NotifyTickStates() {
    OnPropertyChanged(nameof(AllTicked));
    OnPropertyChanged(nameof(AllButSingleTicked));
    OnPropertyChanged(nameof(NoneTicked));
  }

  private void TickWhere(Func<RunSourceViewModel, bool> on) {
    foreach (var node in AllSources(Sources)) node.Own = on(node);
    TicksChanged();
  }

  [RelayCommand]
  private void SelectAll() => TickWhere(_ => true);

  [RelayCommand]
  private void SelectAllWithoutSingle() => TickWhere(n => !IsSingle(n));

  [RelayCommand]
  private void UnselectAll() => TickWhere(_ => false);

  /// <summary>A results window takes over the main Results tab's ticks (sources it does not know stay unticked).</summary>
  [RelayCommand]
  public void CopySelectionFromMain() {
    if (Main == null) return;
    RefreshSources();
    var ticks = AllSources(Main.Sources).Where(s => Key(s) != null).ToDictionary(s => Key(s)!, s => s.Own, ReferenceEqualityComparer.Instance);
    foreach (var node in AllSources(Sources)) node.Own = Key(node) is { } key && ticks.TryGetValue(key, out var own) && own;
    TicksChanged();
  }

  private static IEnumerable<RunSourceViewModel> AllSources(IEnumerable<RunSourceViewModel> nodes) =>
    nodes.SelectMany(n => AllSources(n.Children).Prepend(n));

  [RelayCommand]
  private async Task AddFolderAsync() {
    if (fileDialogs != null && await fileDialogs.PickFolderAsync("Add results folder") is string folder) AddFolder(folder);
  }

  public void AddFolder(string folder) {
    try {
      folderRows[folder] = new ResultStore(folder).Rows();
    } catch (Exception e) {
      Status = $"Cannot read {folder}: {e.Message}";
      return;
    }
    if (!folders.Contains(folder)) folders.Add(folder);
    RefreshSources();
    AllSources(Sources).First(s => s.Folder == folder).IsChecked = true;
  }

  // ---- filters and grouping

  [RelayCommand]
  private void AddFilter() => Filters.Add(new FilterViewModel(this) { Column = AllColumns.FirstOrDefault() });

  [RelayCommand]
  private void RemoveFilter(FilterViewModel filter) {
    Filters.Remove(filter);
    Rebuild();
  }

  partial void OnGroupToAddChanged(ColumnChoice? value) {
    if (value == null) return;
    if (!GroupBy.Any(g => g.Key == value.Key)) GroupBy.Add(value);
    GroupToAdd = null;
    Rebuild();
  }

  [RelayCommand]
  private void RemoveGroup(ColumnChoice group) {
    GroupBy.Remove(group);
    Rebuild();
  }

  partial void OnBinsChanged(decimal value) => Rebuild();

  // ---- building the table: small ones at once, large ones in the background with a progress bar

  /// <summary>What was read from each run (the slow part), so a new filter or grouping does not read them again.</summary>
  private readonly RunValueCache runValues = RunValueCache.Shared;
  private CancellationTokenSource? pending;
  private TaskCompletionSource settled = CreateSettled(done: true);

  private static TaskCompletionSource CreateSettled(bool done) {
    var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    if (done) source.SetResult();
    return source;
  }

  /// <summary>Done when the table shows the current sources, filters and grouping (a large one is built in the background).</summary>
  public Task Settled => settled.Task;

  /// <summary>Above this many runs not read yet, or rows in all, the table is built in the background.</summary>
  public const int BackgroundRuns = 100, BackgroundRows = 3000;

  /// <summary>The table is being built in the background: a bright layer with an orange progress bar covers the views.</summary>
  [ObservableProperty]
  public partial bool IsBusy { get; set; }

  [ObservableProperty]
  public partial double ProgressValue { get; set; }

  [ObservableProperty]
  public partial double ProgressMaximum { get; set; } = 1;

  [ObservableProperty]
  public partial string ProgressText { get; set; } = "";

  /// <summary>Builds table, groups and every view from the ticked sources, filters and grouping.</summary>
  public void Rebuild() {
    // a rebuild replaces lists that ComboBoxes show; their reaction must not start another one (each one
    // replaced the lists again: an endless recursion, a stack overflow that closed the program)
    if (rebuilding) return;
    pending?.Cancel();
    var ticked = AllSources(Sources).Where(s => s.Own).ToList();
    // where each run was made: the deepest optimizer of the tree that holds it (experiments and batch runs
    // hold their children's runs too; a time-limit run takes them over). It is shown if that box is ticked.
    var origin = new Dictionary<IRun, RunSourceViewModel>(ReferenceEqualityComparer.Instance);
    foreach (var node in AllSources(Sources))
      foreach (var run in node.RunsHere ?? []) origin[run] = node;
    // runs a container holds alone (a time-limit run takes its algorithm's) belong to that algorithm
    foreach (var (run, node) in origin.ToList())
      if (node.Children.Count > 0) origin[run] = FirstLeaf(node);
    originOf = origin;
    var shown = origin.Where(o => o.Value.Own).Select(o => {
      var source = SourceOf(o.Value);
      return new SourcedRun(source.Name, Relative(o.Value.Path, source), o.Key);
    }).ToList();
    var stores = ticked.Where(s => s.Folder != null && folderRows.ContainsKey(s.Folder!)).Select(s => (s.Name, Rows: folderRows[s.Folder!])).ToList();
    var filters = Filters.Select(f => f.Filter).OfType<RunFilter>().ToList();
    var groupKeys = GroupBy.Select(g => g.Key).ToList();
    int bins = (int)Bins;
    int unread = shown.Count(r => !runValues.Contains(r.Run));
    int rows = shown.Count + stores.Sum(s => s.Rows.Count);

    if (unread <= BackgroundRuns && rows <= BackgroundRows) {
      pending = null;  // a background build still running is outdated
      var built = Build(shown, stores, filters, groupKeys, bins, null, CancellationToken.None);
      Apply(built, ticked.Count);
      IsBusy = false;
      settled.TrySetResult();
      return;
    }

    // large: in the background; typing on cancels it and starts again
    var cancel = pending = new CancellationTokenSource();
    if (settled.Task.IsCompleted) settled = CreateSettled(done: false);
    IsBusy = true;
    ProgressValue = 0;
    ProgressMaximum = Math.Max(1, shown.Count);
    ProgressText = $"Processing runs: 0 of {shown.Count}";
    var progress = new Progress<int>(done => {
      if (cancel.IsCancellationRequested) return;
      ProgressValue = done;
      ProgressText = $"Processing runs: {done} of {shown.Count}";
    });
    _ = BuildInBackgroundAsync(shown, stores, filters, groupKeys, bins, progress, cancel, ticked.Count);
  }

  private async Task BuildInBackgroundAsync(List<SourcedRun> shown, List<(string Name, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows)> stores,
      List<RunFilter> filters, List<string> groupKeys, int bins, IProgress<int> progress, CancellationTokenSource cancel, int tickedCount) {
    Built built;
    try {
      built = await Task.Run(() => Build(shown, stores, filters, groupKeys, bins, progress, cancel.Token), cancel.Token);
    } catch (OperationCanceledException) {
      return;  // a newer rebuild took over
    } catch (Exception e) when (e is InvalidOperationException or ArgumentException or NullReferenceException) {
      if (ReferenceEquals(pending, cancel)) { IsBusy = false; Status = "The runs could not be read: " + e.Message; settled.TrySetResult(); }
      return;
    }
    if (cancel.IsCancellationRequested || !ReferenceEquals(pending, cancel)) return;
    Apply(built, tickedCount);
    IsBusy = false;
    settled.TrySetResult();
  }

  /// <summary>All rows, the filtered ones and their groups: built from plain data, so it can run off the UI thread.</summary>
  private sealed record Built(RunTable All, RunTable Table, IReadOnlyList<RunGroup> Groups);

  private Built Build(List<SourcedRun> shown, List<(string Name, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows)> stores,
      List<RunFilter> filters, List<string> groupKeys, int bins, IProgress<int>? progress, CancellationToken cancel) {
    var tables = new List<RunTable> { RunTable.FromRuns(shown, runValues, progress, cancel) };
    tables.AddRange(stores.Select(s => RunTable.FromStore(s.Name, s.Rows)));
    var all = RunTable.Combine(tables);
    cancel.ThrowIfCancellationRequested();
    // a filter on a column no ticked source has does not apply; an operator that does not fit the column becomes its first
    var fitting = filters.Where(f => all.Column(f.Key) != null).Select(f => {
      var operators = RunFilter.OperatorsFor(all.Column(f.Key));
      return operators.Contains(f.Op) ? f : f with { Op = operators[0] };
    }).ToList();
    var table = all.Where(fitting);
    cancel.ThrowIfCancellationRequested();
    return new Built(all, table, RunGrouping.Group(table, groupKeys, bins));
  }

  /// <summary>Shows a built table: column choices, filters' operators, the table, groups, charts and statistics.</summary>
  private void Apply(Built built, int tickedCount) {
    rebuilding = true;
    try {
      ApplyNow(built, tickedCount);
    } finally {
      rebuilding = false;
    }
  }

  private void ApplyNow(Built built, int tickedCount) {
    var all = built.All;
    // new lists only when the columns changed (a new list resets every ComboBox showing it)
    var columns = all.Columns.Select(c => new ColumnChoice(c.Key, c.ToString())).ToList();
    if (!columns.SequenceEqual(AllColumns)) AllColumns = columns;
    var numeric = all.Columns.Where(c => c.IsNumeric).Select(c => new ColumnChoice(c.Key, c.ToString())).ToList();
    if (!numeric.SequenceEqual(NumericColumns)) NumericColumns = numeric;
    foreach (var filter in Filters) {
      if (filter.ColumnKey != null && filter.Column?.Key != filter.ColumnKey)
        filter.Column = AllColumns.FirstOrDefault(c => c.Key == filter.ColumnKey) ?? filter.Column;
      filter.FitOperators(filter.ColumnKey == null ? null : all.Column(filter.ColumnKey));
    }
    Table = built.Table;
    Groups = built.Groups;
    Status = tickedCount == 0 ? "Tick the experiments, batch runs, time-limit runs or algorithms whose runs you want to analyse."
      : $"{Table.Rows.Count} of {all.Rows.Count} runs, {Groups.Count} group{(Groups.Count == 1 ? "" : "s")}";
    ChooseDefaults();
    RebuildRows();
    RebuildChart();
    RebuildStatistics();
  }

  private static RunSourceViewModel FirstLeaf(RunSourceViewModel node) => node.Children.Count == 0 ? node : FirstLeaf(node.Children[0]);

  /// <summary>The source a shown run is listed under: the highest box above its origin that is fully ticked.</summary>
  private static RunSourceViewModel SourceOf(RunSourceViewModel origin) {
    var source = origin;
    while (source.Parent is { } parent && parent.IsChecked == true) source = parent;
    return source;
  }

  /// <summary>Origin below the ticked source ("Batch run 1 / GA" under "Experiment 1"); the source itself if the run is its own.</summary>
  private static string Relative(string path, RunSourceViewModel source) =>
    path.StartsWith(source.Path + " / ", StringComparison.Ordinal) ? path[(source.Path.Length + 3)..] : path == source.Path ? source.Name : path;

  private void ChooseDefaults() {
    var quality = NumericColumns.FirstOrDefault(c => c.Key == "result:BestQuality") ?? NumericColumns.FirstOrDefault(c => c.Key.StartsWith("result:", StringComparison.Ordinal));
    if (ChartValue == null || !NumericColumns.Contains(ChartValue)) ChartValue = quality;
    if (StatisticsValue == null || !NumericColumns.Contains(StatisticsValue)) StatisticsValue = quality;
    List<ColumnChoice> xChoices = [new ColumnChoice(RunCharts.RunIndex, "Run (number)"), .. NumericColumns];
    if (!xChoices.SequenceEqual(XChoices)) XChoices = xChoices;
    if (ChartX == null || !XChoices.Contains(ChartX)) ChartX = XChoices[0];
    var curveTables = Table.Curves().Select(c => c.Table).ToList();
    if (!curveTables.SequenceEqual(CurveTables)) CurveTables = curveTables;
    if (CurveTable == null || !CurveTables.Contains(CurveTable)) CurveTable = CurveTables.FirstOrDefault(t => t == "Qualities") ?? CurveTables.FirstOrDefault();
  }

  // ---- table

  /// <summary>Shown columns: where runs come from, parameters that differ between runs, and all results.</summary>
  [ObservableProperty]
  public partial IReadOnlyList<ColumnChoice> TableColumns { get; set; } = [];

  [ObservableProperty]
  public partial IReadOnlyList<AnalysisRow> Rows { get; set; } = [];

  [ObservableProperty]
  public partial AnalysisRow? SelectedRow { get; set; }

  [ObservableProperty]
  public partial string? SortKey { get; set; }

  [ObservableProperty]
  public partial bool SortDescending { get; set; }

  [ObservableProperty]
  public partial bool ShowAllParameters { get; set; }

  partial void OnShowAllParametersChanged(bool value) => RebuildRows();

  [RelayCommand]
  private void Sort(string key) {
    if (SortKey == key) SortDescending = !SortDescending;
    else { SortKey = key; SortDescending = false; }
    RebuildRows();
  }

  private void RebuildRows() {
    var varying = Table.Columns.Where(c => c.Kind == "Parameter" && Table.Rows.Select(r => r.Text(c.Key)).Distinct().Skip(1).Any()).Select(c => c.Key).ToHashSet();
    TableColumns = Table.Columns.Where(c => c.Kind is "Run" or "Label" or "Result" || ShowAllParameters || varying.Contains(c.Key))
      .Select(c => new ColumnChoice(c.Key, c.Name)).ToList();
    IEnumerable<RunRow> rows = Table.Rows;
    if (SortKey != null) {
      var numeric = Table.Column(SortKey)?.IsNumeric == true;
      rows = numeric
        ? (SortDescending ? rows.OrderByDescending(r => r.Number(SortKey) ?? double.MinValue) : rows.OrderBy(r => r.Number(SortKey) ?? double.MaxValue))
        : (SortDescending ? rows.OrderByDescending(r => r.Text(SortKey), StringComparer.OrdinalIgnoreCase) : rows.OrderBy(r => r.Text(SortKey), StringComparer.OrdinalIgnoreCase));
    }
    var selected = SelectedRow?.Row;
    Rows = rows.Select(r => new AnalysisRow(r, TableColumns.Select(c => r.Text(c.Key)).ToList())).ToList();
    Grid = new TableGridViewModel(TableColumns.Select(c => new GridColumn(c.Key, c.Display, 150, c.Key switch {
        "source" => TableGridViewModel.SourceShade, "origin" => TableGridViewModel.OriginShade, "run" => TableGridViewModel.RunShade, _ => (Avalonia.Media.Color?)null
      })).ToList(),
      Rows.Select(r => ((object)r, r.Cells)), item => SelectedRow = item as AnalysisRow, Sort);
    SelectedRow = Rows.FirstOrDefault(r => ReferenceEquals(r.Row, selected));
    Grid.Mark(SelectedRow);
  }

  /// <summary>The table as shown: where a run comes from and which run it is stay in view.</summary>
  [ObservableProperty]
  public partial TableGridViewModel? Grid { get; set; }

  [RelayCommand]
  private async Task ExportAsync() {
    if (fileDialogs == null || await fileDialogs.SaveCsvFileAsync("runs.csv") is not string path) return;
    Export(path);
  }

  /// <summary>Where each run of the workspace was made (from the last rebuild).</summary>
  private Dictionary<IRun, RunSourceViewModel> originOf = new(ReferenceEqualityComparer.Instance);

  /// <summary>
  /// The shown runs (filtered, sorted) for Python: &lt;experiment&gt;_structure.py (blocks as attributes, typed
  /// values), &lt;experiment&gt;_data.json (the values) and &lt;experiment&gt;_analysis.py (examples; kept if it exists).
  /// </summary>
  [RelayCommand]
  private async Task ExportPythonAsync() {
    var runs = PythonRuns();
    if (runs.Count == 0) { Status = "Nothing to export: tick runs first."; return; }
    if (fileDialogs == null || await fileDialogs.SavePythonFileAsync(PythonExport.ModuleName(PythonTitle(runs)) + "_analysis.py") is not string path) return;
    ExportPython(path);
  }

  /// <summary>Exports next to this file; its name without _analysis / _structure / _data names the three files.</summary>
  public IReadOnlyList<string> ExportPython(string path) {
    var runs = PythonRuns();
    var name = Path.GetFileNameWithoutExtension(path);
    foreach (var suffix in new[] { "_analysis", "_structure", "_data" })
      if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length) { name = name[..^suffix.Length]; break; }
    var files = PythonExport.FilesFor(Path.GetDirectoryName(Path.GetFullPath(path))!, name);
    var written = PythonExport.Write(files, PythonTitle(runs), runs);
    Status = $"Exported {runs.Count} runs: {string.Join(", ", written.Select(Path.GetFileName))}"
             + (written.Contains(files.Analysis) ? "" : $" ({Path.GetFileName(files.Analysis)} kept as it was)");
    return written;
  }

  private static string PythonTitle(IReadOnlyList<PythonExportRun> runs) {
    var roots = runs.Select(r => r.Path[0]).Distinct().ToList();
    return roots.Count == 1 ? roots[0] : string.Join(", ", roots.Take(3)) + (roots.Count > 3 ? " and more" : "");
  }

  /// <summary>The shown runs with the path of blocks they were made in (a results folder's runs: the folder).</summary>
  private List<PythonExportRun> PythonRuns() =>
    Rows.Select(r => r.Row).Select(row => {
      if (row.Run != null && originOf.TryGetValue(row.Run, out var node)) {
        var chain = new List<RunSourceViewModel>();
        for (var n = node; n != null; n = n.Parent) chain.Insert(0, n);
        return new PythonExportRun(chain.Select(n => n.Name).ToList(), chain.Select(n => n.Kind).ToList(), row);
      }
      return new PythonExportRun([row.Source], ["Results folder"], row);
    }).ToList();

  public void Export(string path) {
    using var writer = new StreamWriter(path);
    var columns = TableColumns.Select(c => c.Key).ToList();
    // the table in its shown order and with its shown columns
    new RunTable(Rows.Select(r => r.Row).ToList()).WriteCsv(writer, columns);
    Status = $"Exported {Rows.Count} runs to {path}";
  }

  // ---- selected run

  [ObservableProperty]
  public partial IReadOnlyList<NameValue> RunDetails { get; set; } = [];

  [ObservableProperty]
  public partial IReadOnlyList<ResultEntryViewModel> RunResults { get; set; } = [];

  [ObservableProperty]
  public partial ResultEntryViewModel? SelectedRunResult { get; set; }

  [ObservableProperty]
  public partial ResultDetailViewModel? RunResultDetail { get; set; }

  partial void OnSelectedRowChanged(AnalysisRow? value) {
    Grid?.Mark(value);
    RunDetails = value == null ? [] : TableColumns.Select(c => new NameValue(c.Display, value.Row.Text(c.Key))).ToList();
    RunResults = value?.Row.Run is { } run
      ? run.Results.Select(r => new ResultEntryViewModel(r.Key, r.Value, ItemInspector.Summary(r.Value))).ToList()
      : [];
    SelectedRunResult = RunResults.FirstOrDefault(r => HeuristicLab.Next.Runtime.Visuals.Visualizations.For(r.Value, r.Name).Count > 0 && r.Name.StartsWith("Best", StringComparison.Ordinal))
                        ?? RunResults.FirstOrDefault();
  }

  partial void OnSelectedRunResultChanged(ResultEntryViewModel? value) =>
    RunResultDetail = value == null ? null : new ResultDetailViewModel(value.Name, value.Value, new EditContext(_ => { }, () => false));

  // ---- charts

  public IReadOnlyList<string> ChartKinds { get; } = ["Box plot", "Scatter", "Histogram", "Cumulative distribution", "Curves"];

  [ObservableProperty]
  public partial string ChartKind { get; set; } = "Box plot";

  [ObservableProperty]
  public partial ColumnChoice? ChartValue { get; set; }

  [ObservableProperty]
  public partial IReadOnlyList<ColumnChoice> XChoices { get; set; } = [];

  [ObservableProperty]
  public partial ColumnChoice? ChartX { get; set; }

  [ObservableProperty]
  public partial IReadOnlyList<string> CurveTables { get; set; } = [];

  [ObservableProperty]
  public partial string? CurveTable { get; set; }

  [ObservableProperty]
  public partial IReadOnlyList<string> CurveRows { get; set; } = [];

  [ObservableProperty]
  public partial string? CurveRow { get; set; }

  [ObservableProperty]
  public partial bool CurveRuns { get; set; } = true;

  [ObservableProperty]
  public partial bool CurveMean { get; set; } = true;

  public bool IsScatter => ChartKind == "Scatter";
  public bool IsCurves => ChartKind == "Curves";
  public bool NeedsValue => !IsCurves;

  public VisualsViewModel Chart { get; } = new();

  partial void OnChartKindChanged(string value) {
    OnPropertyChanged(nameof(IsBoxPlot));
    OnPropertyChanged(nameof(IsScatter));
    OnPropertyChanged(nameof(IsCurves));
    OnPropertyChanged(nameof(NeedsValue));
    RebuildChart();
  }
  partial void OnChartValueChanged(ColumnChoice? value) => RebuildChart();
  partial void OnChartXChanged(ColumnChoice? value) => RebuildChart();
  partial void OnCurveTableChanged(string? value) {
    CurveRows = Table.Curves().FirstOrDefault(c => c.Table == value).Rows ?? [];
    if (CurveRow == null || !CurveRows.Contains(CurveRow)) CurveRow = CurveRows.FirstOrDefault(r => r == "BestQuality") ?? CurveRows.FirstOrDefault();
    RebuildChart();
  }
  partial void OnCurveRowChanged(string? value) => RebuildChart();
  partial void OnCurveRunsChanged(bool value) => RebuildChart();
  partial void OnCurveMeanChanged(bool value) => RebuildChart();

  private void RebuildChart() {
    if (Groups.Count == 0) { Chart.Show([]); return; }
    HeuristicLab.Next.Runtime.Visuals.Visual? visual = ChartKind switch {
      "Curves" when CurveTable != null && CurveRow != null => RunCharts.Curves(Groups, CurveTable, CurveRow, CurveRuns, CurveMean),
      "Curves" => new HeuristicLab.Next.Runtime.Visuals.TextVisual("Curves", "The ticked runs have no tables (results folders keep only numbers)."),
      _ when ChartValue == null => null,
      "Scatter" when ChartX != null => RunCharts.Scatter(Table, Groups, ChartX.Key, ChartValue.Key),
      "Histogram" => RunCharts.Histogram(Table, Groups, ChartValue.Key),
      "Cumulative distribution" => RunCharts.Cumulative(Table, Groups, ChartValue.Key),
      _ => RunCharts.BoxPlot(Table, Groups, ChartValue.Key)
    };
    Chart.Show(visual == null ? [] : [visual]);
    BoxTable = IsBoxPlot && ChartValue != null ? BoxGrid(RunCharts.BoxStatistics(Groups, ChartValue.Key)) : null;
  }

  public bool IsBoxPlot => ChartKind == "Box plot";

  /// <summary>The box plot's numbers per group, in the program's table design (also in fullscreen).</summary>
  [ObservableProperty]
  public partial TableGridViewModel? BoxTable { get; set; }

  private static TableGridViewModel BoxGrid(IReadOnlyList<RunCharts.BoxStats> boxes) {
    GridColumn Number(string key, string header) => new(key, header, 112);
    var columns = new List<GridColumn> {
      new("group", "Group", 280, TableGridViewModel.RunShade), Number("n", "n"), Number("min", "Min"), Number("whiskerLow", "Whisker low"),
      Number("q1", "Q1"), Number("median", "Median"), Number("mean", "Mean"), Number("q3", "Q3"), Number("whiskerHigh", "Whisker high"),
      Number("max", "Max"), new("outliers", "Outliers", 240)
    };
    return new TableGridViewModel(columns, boxes.Select(b => ((object)b, (IReadOnlyList<string>)[
      b.Group, b.Count.ToString(Invariant), G(b.Min), G(b.WhiskerLow), G(b.Q1), G(b.Median), G(b.Mean), G(b.Q3), G(b.WhiskerHigh), G(b.Max),
      b.Outliers.Count == 0 ? "none" : $"{b.Outliers.Count}: {string.Join(", ", b.Outliers.Select(G))}"])), _ => { });
  }

  // ---- statistics

  [ObservableProperty]
  public partial ColumnChoice? StatisticsValue { get; set; }

  [ObservableProperty]
  public partial IReadOnlyList<SummaryLine> Summaries { get; set; } = [];

  [ObservableProperty]
  public partial IReadOnlyList<PairLine> Pairs { get; set; } = [];

  [ObservableProperty]
  public partial string AllGroupsTest { get; set; } = "";

  /// <summary>Summaries and Pairs in the program's table design.</summary>
  [ObservableProperty]
  public partial TableGridViewModel? SummaryGrid { get; set; }

  [ObservableProperty]
  public partial TableGridViewModel? PairsGrid { get; set; }

  partial void OnStatisticsValueChanged(ColumnChoice? value) => RebuildStatistics();

  private static string G(double v) => double.IsFinite(v) ? v.ToString("G6", Invariant) : "–";
  private static string P(double p) => double.IsFinite(p) ? (p < 0.0001 ? "< 0.0001" : p.ToString("0.0000", Invariant)) : "–";

  private void RebuildStatistics() {
    if (StatisticsValue == null || Groups.Count == 0) { Summaries = []; Pairs = []; AllGroupsTest = ""; SummaryGrid = PairsGrid = null; return; }
    var comparison = RunStatistics.Compare(Groups, StatisticsValue.Key);
    Summaries = comparison.Groups.Select(g => new SummaryLine(g.Group, g.Count.ToString(Invariant), G(g.Mean), G(g.StdDev), G(g.Min), G(g.Q1), G(g.Median), G(g.Q3), G(g.Max))).ToList();
    Pairs = comparison.Pairs.Select(p => new PairLine($"{p.A}  vs  {p.B}", P(p.MannWhitneyP), P(p.AdjustedP), P(p.TTestP), G(p.CohensD), G(p.HedgesG), p.AdjustedP < 0.05)).ToList();
    GridColumn Number(string key, string header) => new(key, header, 110);
    SummaryGrid = new TableGridViewModel([
        new("group", "Group", 280, TableGridViewModel.RunShade), Number("n", "n"), Number("mean", "Mean"), Number("sd", "Std. dev."),
        Number("min", "Min"), Number("q1", "Q1"), Number("median", "Median"), Number("q3", "Q3"), Number("max", "Max")
      ], Summaries.Select(l => ((object)l, (IReadOnlyList<string>)[l.Group, l.Count, l.Mean, l.StdDev, l.Min, l.Q1, l.Median, l.Q3, l.Max])), _ => { });
    PairsGrid = new TableGridViewModel([
        new("pair", "Pair", 380, TableGridViewModel.RunShade), new("mw", "Mann-Whitney", 130), Number("adjusted", "Adjusted"),
        Number("t", "t-test"), Number("d", "Cohen's d"), Number("g", "Hedges' g")
      ], Pairs.Select(l => ((object)l, (IReadOnlyList<string>)[l.Pair, l.MannWhitney, l.Significant ? l.Adjusted + "  *" : l.Adjusted, l.TTest, l.CohensD, l.HedgesG])), _ => { });
    AllGroupsTest = comparison.KruskalWallisP is double kw
      ? $"Kruskal-Wallis over all {comparison.Groups.Count} groups: p = {P(kw)}" + (kw < 0.05 ? " - the groups differ (α = 0.05)." : " - no significant difference (α = 0.05).")
      : "Tests need at least two groups with two or more runs each.";
  }
}
