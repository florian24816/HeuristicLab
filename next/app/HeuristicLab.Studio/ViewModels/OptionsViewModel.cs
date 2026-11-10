using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeuristicLab.Studio.Services;

namespace HeuristicLab.Studio.ViewModels;

/// <summary>
/// Options tab (after Resources; user's request 2026-11-10): the size of a quick batch run (how many runs Start on one
/// algorithm makes) and a working directory where every file dialog opens. Both are remembered in the settings.
/// </summary>
public partial class OptionsViewModel : ViewModelBase {
  private readonly IFileDialogService? fileDialogs;
  private readonly Action changed;
  private readonly bool loading;

  public OptionsViewModel(int quickBatchSize, string? workingDirectory, IFileDialogService? fileDialogs, Action changed) {
    this.fileDialogs = fileDialogs;
    this.changed = changed;
    loading = true;
    QuickBatchSize = Math.Clamp(quickBatchSize, MinQuickBatchSize, MaxQuickBatchSize);
    WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory;
    loading = false;
  }

  public int MinQuickBatchSize => 1;
  public int MaxQuickBatchSize => QuickBatchRuns.MaxSize;

  /// <summary>Runs per quick batch run (10 at first).</summary>
  [ObservableProperty]
  public partial decimal? QuickBatchSize { get; set; }

  partial void OnQuickBatchSizeChanged(decimal? value) {
    if (value is null) return;  // a field being typed in: the last size stays
    var size = (int)Math.Clamp(Math.Round(value.Value), MinQuickBatchSize, MaxQuickBatchSize);
    if (size != value) { QuickBatchSize = size; return; }
    if (!loading) changed();
  }

  /// <summary>The size in use (the default while the field is empty).</summary>
  public int BatchSize => QuickBatchSize is decimal d ? (int)d : QuickBatchRuns.DefaultSize;

  [RelayCommand]
  private void DefaultQuickBatchSize() => QuickBatchSize = QuickBatchRuns.DefaultSize;

  /// <summary>Where every file dialog opens; null: where the system opens it.</summary>
  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(HasWorkingDirectory), nameof(WorkingDirectoryText), nameof(WorkingDirectoryMissing))]
  [NotifyCanExecuteChangedFor(nameof(ClearWorkingDirectoryCommand))]
  public partial string? WorkingDirectory { get; set; }

  partial void OnWorkingDirectoryChanged(string? value) {
    if (fileDialogs != null) fileDialogs.WorkingDirectory = value;
    if (!loading) changed();
  }

  public bool HasWorkingDirectory => WorkingDirectory != null;
  public string WorkingDirectoryText => WorkingDirectory ?? "None: file dialogs open where the system opens them.";
  /// <summary>A working directory that is not there (any more): the dialogs open where the system opens them.</summary>
  public bool WorkingDirectoryMissing => WorkingDirectory != null && !Directory.Exists(WorkingDirectory);

  [RelayCommand]
  private async Task ChooseWorkingDirectoryAsync() {
    if (fileDialogs != null && await fileDialogs.PickFolderAsync("Working directory") is string folder) WorkingDirectory = folder;
  }

  [RelayCommand(CanExecute = nameof(HasWorkingDirectory))]
  private void ClearWorkingDirectory() => WorkingDirectory = null;
}
