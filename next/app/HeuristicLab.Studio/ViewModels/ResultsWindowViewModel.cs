using CommunityToolkit.Mvvm.ComponentModel;

namespace HeuristicLab.Studio.ViewModels;

/// <summary>
/// One more Results tab (to compare several selections side by side): docked in the second tab bar of the main
/// window's Results tab (a parallelogram there), or dragged out into a window of its own (a rectangle then), named
/// in its tab and title bar ("HL-Results: 1", ... - the name can be changed while working).
/// </summary>
public partial class ResultsWindowViewModel(int number, RunAnalysisViewModel analysis) : ViewModelBase {
  public int Number { get; } = number;
  public RunAnalysisViewModel Analysis { get; } = analysis;

  /// <summary>The tab's and window's title, changed live by the name field.</summary>
  [ObservableProperty]
  public partial string Name { get; set; } = $"HL-Results: {number}";

  /// <summary>In the main window's second tab bar (else: a window of its own).</summary>
  [ObservableProperty]
  public partial bool IsDocked { get; set; } = true;

  /// <summary>Shown in the main window's Results tab now.</summary>
  [ObservableProperty]
  public partial bool IsSelected { get; set; }

  /// <summary>Where its window appears (screen pixels; where the tab was dragged to), null: wherever.</summary>
  public Avalonia.PixelPoint? WindowAt { get; set; }
}
