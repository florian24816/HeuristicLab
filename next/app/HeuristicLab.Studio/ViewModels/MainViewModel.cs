using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Studio.Services;

namespace HeuristicLab.Studio.ViewModels;

public sealed record NameValue(string Name, string Value);

/// <summary>
/// The window's tabs. Experiments are built, opened, saved and started in the Experiments tab;
/// the other tabs look at what they hold (runs, analysis) or set what may be used.
/// All HeuristicLab access goes through HeuristicLab.Next.Runtime.
/// </summary>
public partial class MainViewModel : ViewModelBase {
  private readonly ISettingsStore? settingsStore;

  public const int ExperimentsTab = 0, RunsTab = 1, ResultsTab = 2, ScriptsTab = 3, ResourcesTab = 4;

  public MainViewModel() : this(null) { }

  public MainViewModel(IFileDialogService? fileDialogs, IDialogService? dialogs = null, ISettingsStore? settingsStore = null) {
    this.settingsStore = settingsStore;
    var loaded = settingsStore?.Load();
    Workspace = new ExperimentWorkspaceViewModel(dialogs, fileDialogs, optimizer => {
      SelectedTab = RunsTab;
      Runs?.Select(optimizer);
    }, script => {
      Scripts.Open(script);
      SelectedTab = ScriptsTab;
    }, loaded?.RecentFiles, SaveSettings);
    Runs = new RunsBrowserViewModel(Workspace);
    RunAnalysis = new RunAnalysisViewModel(Workspace, fileDialogs);
    Workspace.PropertyChanged += (_, e) => {
      // finished experiments bring new runs
      if (e.PropertyName == nameof(ExperimentWorkspaceViewModel.IsRunning) && !Workspace.IsRunning) {
        RunAnalysis.RefreshSources();
        Runs.Refresh();
      }
    };
    var limits = loaded == null ? new ResourceLimits() : new ResourceLimits {
      Cores = loaded.ResourceCores > 0 ? loaded.ResourceCores : Environment.ProcessorCount,
      ThreadsPerAlgorithm = Math.Max(1, loaded.ResourceThreads), ConcurrentExperiments = loaded.ResourceConcurrentExperiments,
      MemoryLimitGB = loaded.ResourceMemoryGB,
      Priority = Enum.TryParse<ResourcePriority>(loaded.ResourcePriority, out var priority) ? priority : ResourcePriority.Normal
    };
    Resources = new ResourcesViewModel(limits, _ => { if (Resources != null) SaveSettings(); }, StopEverything);
  }

  /// <summary>Stores recent files and the resource limits (called on exit and after open/save).</summary>
  public void SaveSettings() {
    var limits = Resources?.Limits ?? new ResourceLimits();
    settingsStore?.Save(new StudioSettings {
      RecentFiles = Workspace.RecentFiles.ToList(),
      ResourceCores = limits.Cores, ResourceThreads = limits.ThreadsPerAlgorithm, ResourceConcurrentExperiments = limits.ConcurrentExperiments,
      ResourceMemoryGB = limits.MemoryLimitGB, ResourcePriority = limits.Priority.ToString()
    });
  }

  /// <summary>Experiments tab: building blocks of several experiments.</summary>
  public ExperimentWorkspaceViewModel Workspace { get; }

  /// <summary>Runs tab: the runs of the experiments in the Experiments tab.</summary>
  public RunsBrowserViewModel Runs { get; }

  /// <summary>Results tab: the runs of the workspace and of results folders, compared in tables, charts and tests.</summary>
  public RunAnalysisViewModel RunAnalysis { get; }

  /// <summary>C# scripts (HeuristicLab's script samples, or new ones).</summary>
  public ScriptsViewModel Scripts { get; } = new();

  /// <summary>Resources tab: what the program may use, and what it uses.</summary>
  public ResourcesViewModel Resources { get; }

  [ObservableProperty]
  public partial int SelectedTab { get; set; }

  partial void OnSelectedTabChanged(int value) {
    if (value == RunsTab) Runs.Refresh();
    if (value == ResultsTab) RunAnalysis.RefreshSources();
  }

  /// <summary>The memory limit was exceeded: stop experiments and algorithms.</summary>
  private void StopEverything(string message) {
    if (Workspace.StopCommand.CanExecute(null)) Workspace.StopCommand.Execute(null);
    if (Workspace.Detail is AlgorithmDetailViewModel detail && detail.StopCommand.CanExecute(null)) detail.StopCommand.Execute(null);
    Workspace.Status = message;
  }
}
