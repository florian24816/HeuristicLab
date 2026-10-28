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
    this.dialogs = dialogs;
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
    // the main Results tab closes the results windows ("Close windows", enabled while there are some)
    RunAnalysis.CloseWindowsCommand = CloseResultsWindowsCommand;
    ResultsWindows.CollectionChanged += (_, _) => {
      OnPropertyChanged(nameof(HasResultsWindows));
      CloseResultsWindowsCommand.NotifyCanExecuteChanged();
    };
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

  // ---- more Results tabs: in a second tab bar of the Results tab, or each in a window of its own

  /// <summary>
  /// The results tabs besides "Main results" (user's request 2026-10-28): "Add window" adds one docked in the second
  /// tab bar and selects it; dragged out of the bar it becomes a window (and back when the window is moved onto the bar).
  /// </summary>
  public System.Collections.ObjectModel.ObservableCollection<ResultsWindowViewModel> ResultsWindows { get; } = [];

  private int resultsWindowsMade;
  private readonly IFileDialogService? fileDialogs;
  private readonly IDialogService? dialogs;

  /// <summary>The results tab shown in the main window's Results tab; null: "Main results".</summary>
  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(ShownAnalysis), nameof(IsMainResultsShown), nameof(SelectedDocked))]
  public partial ResultsWindowViewModel? SelectedResults { get; set; }

  partial void OnSelectedResultsChanged(ResultsWindowViewModel? value) {
    foreach (var results in ResultsWindows) results.IsSelected = ReferenceEquals(results, value);
  }

  /// <summary>What the main window's Results tab shows: the selected docked tab, else the main results.</summary>
  public RunAnalysisViewModel ShownAnalysis => SelectedResults is { IsDocked: true } selected ? selected.Analysis : RunAnalysis;

  public bool IsMainResultsShown => SelectedResults == null;

  /// <summary>The selected docked tab (its name field shows under the bar), null for "Main results".</summary>
  public ResultsWindowViewModel? SelectedDocked => SelectedResults is { IsDocked: true } selected ? selected : null;

  public bool HasResultsWindows => ResultsWindows.Count > 0;

  /// <summary>Asks the main window to show this results tab as a window (it was dragged out of the bar).</summary>
  public event Action<ResultsWindowViewModel>? ResultsWindowRequested;

  /// <summary>Asks the main window to close this results tab's window (docked again, or closed).</summary>
  public event Action<ResultsWindowViewModel>? ResultsWindowCloseRequested;

  /// <summary>Asks to bring a window to the front: a results tab's window, or (null) the main window.</summary>
  public event Action<ResultsWindowViewModel?>? ActivateRequested;

  [RelayCommand]
  private void AddResultsWindow() => OpenResultsWindow();

  /// <summary>A new results tab ("HL-Results: n"), docked in the second bar and selected, ticked as the main Results tab is now.</summary>
  public ResultsWindowViewModel OpenResultsWindow() {
    var analysis = new RunAnalysisViewModel(Workspace, fileDialogs) { Main = RunAnalysis };
    analysis.CopySelectionFromMain();
    var results = new ResultsWindowViewModel(++resultsWindowsMade, analysis);
    ResultsWindows.Add(results);
    SelectResults(results);
    return results;
  }

  /// <summary>
  /// A click on a tab of the second bar: a docked one is shown in the main window's Results tab, a window comes to
  /// the front; "Main results" (null) shows the main results and brings the main window to the front.
  /// </summary>
  public void SelectResults(ResultsWindowViewModel? results) {
    if (results is { IsDocked: false }) {
      ActivateRequested?.Invoke(results);
      return;
    }
    SelectedResults = results;
    SelectedTab = ResultsTab;
    OnPropertyChanged(nameof(ShownAnalysis));
    if (results == null) ActivateRequested?.Invoke(null);
  }

  /// <summary>A tab dragged out of the bar: a window of its own (shown at this screen position, if given).</summary>
  public void Undock(ResultsWindowViewModel results, Avalonia.PixelPoint? at = null) {
    if (!results.IsDocked || !ResultsWindows.Contains(results)) return;
    results.WindowAt = at;
    results.IsDocked = false;
    if (ReferenceEquals(SelectedResults, results)) SelectedResults = null;
    RefreshShown();
    ResultsWindowRequested?.Invoke(results);
  }

  /// <summary>A window moved onto the second bar: back in the main window, selected.</summary>
  public void Dock(ResultsWindowViewModel results) {
    if (results.IsDocked || !ResultsWindows.Contains(results)) return;
    results.IsDocked = true;
    ResultsWindowCloseRequested?.Invoke(results);
    SelectResults(results);
    ActivateRequested?.Invoke(null);
  }

  /// <summary>The small cross of a tab: the results tab goes (its window too).</summary>
  public void CloseResults(ResultsWindowViewModel results) {
    if (!ResultsWindows.Remove(results)) return;
    if (ReferenceEquals(SelectedResults, results)) SelectedResults = null;
    RefreshShown();
    if (!results.IsDocked) {
      results.IsDocked = true;  // closing its window is no longer a "closed by the user"
      ResultsWindowCloseRequested?.Invoke(results);
    }
  }

  private void RefreshShown() {
    OnPropertyChanged(nameof(ShownAnalysis));
    OnPropertyChanged(nameof(SelectedDocked));
  }

  /// <summary>"Close windows" (main Results tab): after "OK" in "n result windows are about to be closed", all of them.</summary>
  [RelayCommand(CanExecute = nameof(HasResultsWindows))]
  private async System.Threading.Tasks.Task CloseResultsWindowsAsync() {
    int count = ResultsWindows.Count;
    if (count == 0) return;
    var message = count == 1 ? "1 result window is about to be closed" : $"{count} result windows are about to be closed";
    if (dialogs != null && !await dialogs.AcknowledgeAsync("Close windows", message)) return;
    foreach (var results in ResultsWindows.ToList()) CloseResults(results);
  }

  /// <summary>A results window was closed by the user (its title bar's X): the tab goes too.</summary>
  public void ResultsWindowClosed(ResultsWindowViewModel window) {
    if (!window.IsDocked) CloseResults(window);
  }

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
