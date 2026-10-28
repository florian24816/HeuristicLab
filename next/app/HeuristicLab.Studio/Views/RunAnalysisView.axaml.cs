using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

public partial class RunAnalysisView : UserControl {
  public RunAnalysisView() {
    InitializeComponent();
    // several sources selected at once (Ctrl+click, Shift+click, press and sweep)
    HeuristicLab.Studio.Controls.SweepSelection.Attach(SourceTree,
      () => DataContext is RunAnalysisViewModel analysis ? analysis.AllSourceNodes : []);
    // F11 on the Charts tab enters fullscreen (and leaves it again, there)
    AddHandler(KeyDownEvent, (_, e) => {
      if (e.Key == Key.F11 && DataContext is RunAnalysisViewModel { SelectedView: RunAnalysisViewModel.ChartsView }) { e.Handled = true; OpenFullscreen(); }
    }, RoutingStrategies.Tunnel);
  }

  public ChartsFullscreenWindow? Fullscreen { get; private set; }

  private void OnFullscreen(object? sender, RoutedEventArgs e) => OpenFullscreen();

  /// <summary>The chart in fullscreen, legend and box plot table in floating panels.</summary>
  public ChartsFullscreenWindow? OpenFullscreen() {
    if (DataContext is not RunAnalysisViewModel { Chart.HasAny: true } analysis || Fullscreen != null) return Fullscreen;
    Fullscreen = new ChartsFullscreenWindow(analysis);
    Fullscreen.Closed += (_, _) => Fullscreen = null;
    if (TopLevel.GetTopLevel(this) is Window owner) Fullscreen.Show(owner);
    else Fullscreen.Show();
    return Fullscreen;
  }
}
