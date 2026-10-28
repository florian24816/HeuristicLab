using CommunityToolkit.Mvvm.ComponentModel;

namespace HeuristicLab.Studio.ViewModels;

/// <summary>
/// A results window: one more Results tab in its own window (to compare several selections side by side),
/// named in its title bar ("HL-Results: 1", ... - the name can be changed while working).
/// </summary>
public partial class ResultsWindowViewModel(int number, RunAnalysisViewModel analysis) : ViewModelBase {
  public int Number { get; } = number;
  public RunAnalysisViewModel Analysis { get; } = analysis;

  /// <summary>The window's title, changed live by the name field at its top.</summary>
  [ObservableProperty]
  public partial string Name { get; set; } = $"HL-Results: {number}";
}
