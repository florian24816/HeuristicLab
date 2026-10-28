using System;
using System.Linq;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

  private readonly IDesktopNotifications? notifications;

  public MainViewModel(IFileDialogService? fileDialogs, IDialogService? dialogs = null, ISettingsStore? settingsStore = null,
      IDesktopNotifications? notifications = null) {
    this.settingsStore = settingsStore;
    this.notifications = notifications;
    this.fileDialogs = fileDialogs;
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
        foreach (var window in ResultsWindows) window.Analysis.RefreshSources();
        Runs.Refresh();
      }
    };
    Workspace.Finished += SendNotification;
    // before Resources: loading the settings saves nothing
    IsDarkMode = loaded?.DarkMode ?? false;
    IsNotifying = loaded?.Notifications ?? false;
    ApplyTheme();
    var limits = loaded == null ? new ResourceLimits() : new ResourceLimits {
      Cores = loaded.ResourceCores > 0 ? loaded.ResourceCores : Environment.ProcessorCount,
      ThreadsPerAlgorithm = Math.Max(1, loaded.ResourceThreads), ConcurrentExperiments = loaded.ResourceConcurrentExperiments,
      MemoryLimitGB = loaded.ResourceMemoryGB,
      Priority = Enum.TryParse<ResourcePriority>(loaded.ResourcePriority, out var priority) ? priority : ResourcePriority.Normal
    };
    Resources = new ResourcesViewModel(limits, _ => { if (Resources != null) SaveSettings(); }, StopEverything);
  }

  /// <summary>Dark or light mode (the square button at the top left), remembered in the settings.</summary>
  [ObservableProperty]
  public partial bool IsDarkMode { get; set; }

  partial void OnIsDarkModeChanged(bool value) {
    ApplyTheme();
    if (Resources != null) SaveSettings();
  }

  [RelayCommand]
  private void ToggleTheme() => IsDarkMode = !IsDarkMode;

  /// <summary>Notifications on (the square button next to the light/dark one), remembered in the settings.</summary>
  [ObservableProperty]
  public partial bool IsNotifying { get; set; }

  partial void OnIsNotifyingChanged(bool value) {
    if (Resources != null) SaveSettings();
  }

  [RelayCommand]
  private void ToggleNotifications() => IsNotifying = !IsNotifying;

  /// <summary>Started experiments, an algorithm or a detailed analysis run finished: tell the notification center.</summary>
  private async void SendNotification(string title, string message) {
    if (!IsNotifying || notifications == null) return;
    if (await notifications.SendAsync(title, message) is string error) Workspace.Status = $"Notification not sent: {error}";
  }

  private void ApplyTheme() {
    if (Application.Current is { } app) app.RequestedThemeVariant = IsDarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
  }

  /// <summary>Stores recent files and the resource limits (called on exit and after open/save).</summary>
  public void SaveSettings() {
    var limits = Resources?.Limits ?? new ResourceLimits();
    settingsStore?.Save(new StudioSettings {
      RecentFiles = Workspace.RecentFiles.ToList(),
      ResourceCores = limits.Cores, ResourceThreads = limits.ThreadsPerAlgorithm, ResourceConcurrentExperiments = limits.ConcurrentExperiments,
      ResourceMemoryGB = limits.MemoryLimitGB, ResourcePriority = limits.Priority.ToString(), DarkMode = IsDarkMode,
      Notifications = IsNotifying
    });
  }

  /// <summary>Experiments tab: building blocks of several experiments.</summary>
  public ExperimentWorkspaceViewModel Workspace { get; }

  /// <summary>Runs tab: the runs of the experiments in the Experiments tab.</summary>
  public RunsBrowserViewModel Runs { get; }

  /// <summary>Results tab: the runs of the workspace and of results folders, compared in tables, charts and tests.</summary>
  public RunAnalysisViewModel RunAnalysis { get; }

  // ---- results windows: more Results tabs, each in its own window

  /// <summary>The results windows open now.</summary>
  public System.Collections.ObjectModel.ObservableCollection<ResultsWindowViewModel> ResultsWindows { get; } = [];

  private int resultsWindowsMade;
  private readonly IFileDialogService? fileDialogs;

  /// <summary>Asks the main window to show a new results window.</summary>
  public event Action<ResultsWindowViewModel>? ResultsWindowRequested;

  /// <summary>A new results window ("HL-Results: n"), ticked as the main Results tab is now.</summary>
  [RelayCommand]
  private void AddResultsWindow() => OpenResultsWindow();

  public ResultsWindowViewModel OpenResultsWindow() {
    var analysis = new RunAnalysisViewModel(Workspace, fileDialogs) { Main = RunAnalysis };
    analysis.CopySelectionFromMain();
    var window = new ResultsWindowViewModel(++resultsWindowsMade, analysis);
    ResultsWindows.Add(window);
    ResultsWindowRequested?.Invoke(window);
    return window;
  }

  /// <summary>A results window was closed.</summary>
  public void ResultsWindowClosed(ResultsWindowViewModel window) => ResultsWindows.Remove(window);

  /// <summary>C# scripts (HeuristicLab's script samples, or new ones).</summary>
  public ScriptsViewModel Scripts { get; } = new();

  /// <summary>Resources tab: what the program may use, and what it uses.</summary>
  public ResourcesViewModel Resources { get; }

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(IsResultsTab))]
  public partial int SelectedTab { get; set; }

  /// <summary>The Results tab is shown: "Add results window" appears next to its header.</summary>
  public bool IsResultsTab => SelectedTab == ResultsTab;

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
