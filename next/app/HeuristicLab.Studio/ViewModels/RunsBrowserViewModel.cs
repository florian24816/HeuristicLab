using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;

namespace HeuristicLab.Studio.ViewModels;

/// <summary>
/// An experiment, batch run, time-limit run or algorithm of the workspace with its runs, or a part of the
/// "Single runs" folder (all of them, an experiment's, an algorithm's).
/// </summary>
public sealed class RunsSourceViewModel(string name, string kind, object key, Func<IReadOnlyList<IRun>> runs) {
  public RunsSourceViewModel(string name, string kind, IOptimizer optimizer) : this(name, kind, optimizer, () => optimizer.Runs.ToArray()) { }

  public string Name { get; } = name;
  public string Kind { get; } = kind;
  /// <summary>What it stands for (the optimizer, or a part of the single runs): kept selected over a refresh.</summary>
  public object Key { get; } = key;
  public IOptimizer? Optimizer => Key as IOptimizer;
  public IReadOnlyList<IRun> Runs => runs();
  public ObservableCollection<RunsSourceViewModel> Children { get; } = [];
  public int RunCount => Runs.Count;
  public string Label => $"{Name}  ({RunCount} run{(RunCount == 1 ? "" : "s")})";
}

/// <summary>
/// Runs tab: the runs of the experiments loaded in the Experiments tab. A block's runs as a table
/// (HeuristicLab keeps them in the optimizer, so they are saved in its .hl file); a selected run
/// shows its parameters and its results with their pictures.
/// </summary>
public partial class RunsBrowserViewModel : ViewModelBase {
  private readonly ExperimentWorkspaceViewModel workspace;

  public RunsBrowserViewModel(ExperimentWorkspaceViewModel workspace) {
    this.workspace = workspace;
  }

  public ObservableCollection<RunsSourceViewModel> Sources { get; } = [];

  public bool HasSources => Sources.Count > 0;

  [ObservableProperty]
  public partial RunsSourceViewModel? SelectedSource { get; set; }

  [ObservableProperty]
  public partial RunsViewModel? Runs { get; set; }

  [ObservableProperty]
  public partial IReadOnlyList<NameValue> RunParameters { get; set; } = [];

  [ObservableProperty]
  public partial IReadOnlyList<ResultEntryViewModel> RunResults { get; set; } = [];

  [ObservableProperty]
  public partial ResultEntryViewModel? SelectedRunResult { get; set; }

  [ObservableProperty]
  public partial ResultDetailViewModel? RunResultDetail { get; set; }

  /// <summary>The selected run (null if none).</summary>
  public IRun? SelectedRun => Runs?.Selected?.Run;

  /// <summary>Re-reads the workspace (new experiments, finished runs); the selected block and run stay selected.</summary>
  [RelayCommand]
  public void Refresh() {
    var selected = SelectedSource?.Key;
    var run = SelectedRun;
    Sources.Clear();
    foreach (var experiment in workspace.Experiments) Sources.Add(Node(experiment));
    if (workspace.Experiments.Count > 0) Sources.Add(SingleRunsNode(workspace.Experiments));
    OnPropertyChanged(nameof(HasSources));
    SelectKey(selected, run);
  }

  /// <summary>The key of the "Single runs" folder (all single runs of the workspace).</summary>
  public static readonly object SingleRunsKey = new();

  /// <summary>"Single runs": runs made by Start on one algorithm, per experiment and algorithm.</summary>
  internal static RunsSourceViewModel SingleRunsNode(IEnumerable<ExperimentBlockViewModel> experiments) {
    var list = experiments.ToList();
    var folder = new RunsSourceViewModel("Single runs", "Started on one algorithm", SingleRunsKey,
      () => list.SelectMany(e => e.SingleRuns.Runs).ToList());
    foreach (var experiment in list.Where(e => e.SingleRuns.Count > 0)) {
      var single = experiment.SingleRuns;
      var node = new RunsSourceViewModel(experiment.Name, "Experiment", single, () => single.Runs.ToArray());
      foreach (var group in single.Groups) node.Children.Add(new RunsSourceViewModel(group.Algorithm, "Algorithm", group, () => group.Runs));
      folder.Children.Add(node);
    }
    return folder;
  }

  /// <summary>Deletes all runs of one experiment, chosen and confirmed in a dialog.</summary>
  [RelayCommand]
  private async System.Threading.Tasks.Task DeleteRunsAsync() {
    await workspace.DeleteRunsAsync();
    Refresh();
  }

  /// <summary>Selects the block of this optimizer (and a run of it); the first experiment if it is not in the workspace.</summary>
  public void Select(IOptimizer? optimizer, IRun? run = null) => SelectKey(optimizer, run);

  private void SelectKey(object? key, IRun? run = null) {
    var node = All(Sources).FirstOrDefault(s => ReferenceEquals(s.Key, key)) ?? Sources.FirstOrDefault();
    if (!ReferenceEquals(node, SelectedSource)) SelectedSource = node;
    else Runs?.Refresh();
    if (Runs == null) return;
    Runs.Selected = Runs.Rows.FirstOrDefault(r => ReferenceEquals(r.Run, run)) ?? Runs.Selected ?? Runs.Rows.LastOrDefault();
  }

  private static RunsSourceViewModel Node(BlockViewModel block) {
    IOptimizer optimizer = block switch { ContainerBlockViewModel c => c.Optimizer, AlgorithmBlockViewModel a => a.Algorithm, _ => throw new ArgumentException(block.Name) };
    string kind = optimizer switch { Experiment => "Experiment", BatchRun => "Batch run", TimeLimitRun => "Time-limit run", _ => "Algorithm" };
    var node = new RunsSourceViewModel(block.Name, kind, optimizer);
    if (block is ContainerBlockViewModel container)
      foreach (var child in container.Children) node.Children.Add(Node(child));
    return node;
  }

  private static IEnumerable<RunsSourceViewModel> All(IEnumerable<RunsSourceViewModel> nodes) =>
    nodes.SelectMany(n => All(n.Children).Prepend(n));

  partial void OnSelectedSourceChanged(RunsSourceViewModel? value) {
    if (Runs != null) Runs.PropertyChanged -= OnRunsChanged;
    Runs = value == null ? null : new RunsViewModel(() => value.Runs) { ShowsDetails = false };
    if (Runs != null) {
      Runs.PropertyChanged += OnRunsChanged;
      Runs.Refresh();
      Runs.Selected = Runs.Rows.LastOrDefault();
    }
    ShowRun();
  }

  private void OnRunsChanged(object? sender, PropertyChangedEventArgs e) {
    if (e.PropertyName == nameof(RunsViewModel.Selected)) ShowRun();
  }

  private void ShowRun() {
    var run = SelectedRun;
    OnPropertyChanged(nameof(SelectedRun));
    RunParameters = run == null ? [] : OptimizerRunner.ToRecord(run).Parameters.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
      .Select(p => new NameValue(p.Key, Text(p.Value))).ToList();
    RunResults = run == null ? [] : run.Results.Select(r => new ResultEntryViewModel(r.Key, r.Value, ItemInspector.Summary(r.Value))).ToList();
    SelectedRunResult = RunResults.FirstOrDefault(r => HeuristicLab.Next.Runtime.Visuals.Visualizations.For(r.Value, r.Name).Count > 0 && r.Name.StartsWith("Best", StringComparison.Ordinal))
                        ?? RunResults.FirstOrDefault();
  }

  partial void OnSelectedRunResultChanged(ResultEntryViewModel? value) =>
    RunResultDetail = value == null ? null : new ResultDetailViewModel(value.Name, value.Value, new EditContext(_ => { }, () => false));

  private static string Text(ItemValue v) => v.Value switch {
    double d => d.ToString("G10", CultureInfo.InvariantCulture),
    null => v.Text ?? "",
    Array a => $"{v.Type} [{a.Length}]",
    var o => Convert.ToString(o, CultureInfo.InvariantCulture) ?? ""
  };
}
