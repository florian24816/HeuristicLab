using System.Linq;
using System.Windows.Input;
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
    // a click anywhere else in the Results tab (charts, table, filters, ...) ends the selection of sources
    AddHandler(PointerPressedEvent, (_, e) => {
      if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || DataContext is not RunAnalysisViewModel analysis) return;
      for (var v = e.Source as Avalonia.Visual; v != null; v = Avalonia.VisualTree.VisualExtensions.GetVisualParent(v))
        if (ReferenceEquals(v, SourceTree)) return;
      foreach (var source in analysis.AllSourceNodes) if (source.IsSelected) source.IsSelected = false;
    }, RoutingStrategies.Tunnel, handledEventsToo: true);
    // F11 on the Charts tab enters fullscreen (and leaves it again, there)
    AddHandler(KeyDownEvent, (_, e) => {
      if (e.Key == Key.F11 && DataContext is RunAnalysisViewModel { SelectedView: RunAnalysisViewModel.ChartsView }) { e.Handled = true; OpenFullscreen(); }
    }, RoutingStrategies.Tunnel);
  }

  public ChartsFullscreenWindow? Fullscreen { get; private set; }

  // ---- run windows ("Open run in window" in the table's row menu)

  private RunAnalysisViewModel? subscribed;

  // only a view on screen opens windows (a results tab's view is made again when it is shown again)
  protected override void OnDataContextChanged(System.EventArgs e) {
    base.OnDataContextChanged(e);
    Subscribe(IsAttached ? DataContext as RunAnalysisViewModel : null);
  }

  private bool IsAttached => TopLevel.GetTopLevel(this) != null;

  protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) {
    base.OnAttachedToVisualTree(e);
    Subscribe(DataContext as RunAnalysisViewModel);
  }

  protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) {
    base.OnDetachedFromVisualTree(e);
    Subscribe(null);
  }

  private void Subscribe(RunAnalysisViewModel? analysis) {
    if (ReferenceEquals(subscribed, analysis)) return;
    if (subscribed != null) subscribed.RunWindowRequested -= ShowRunWindow;
    subscribed = analysis;
    if (subscribed != null) subscribed.RunWindowRequested += ShowRunWindow;
  }

  private readonly System.Collections.Generic.List<RunWindow> runWindows = [];

  /// <summary>The run windows this view opened and that are still open (tests).</summary>
  public System.Collections.Generic.IReadOnlyList<RunWindow> RunWindows => runWindows;

  private void ShowRunWindow(RunWindowViewModel run) {
    var analysis = subscribed;
    var window = new RunWindow(run);
    runWindows.Add(window);
    window.Closed += (_, _) => {
      runWindows.Remove(window);
      analysis?.RunWindowClosed(run);
    };
    window.Show();
  }

  /// <summary>
  /// Right-click on a source: tick, untick or tick only the selected ones (a source not selected is selected alone
  /// first). The menu is the last one opened (tests).
  /// </summary>
  public ContextMenu? Menu { get; private set; }

  private void SourceMenu(object? sender, ContextRequestedEventArgs e) {
    if (sender is not Control { DataContext: RunSourceViewModel source } row || DataContext is not RunAnalysisViewModel analysis) return;
    e.Handled = true;
    if (!source.IsSelected) {
      foreach (var other in analysis.AllSourceNodes) if (other.IsSelected) other.IsSelected = false;
      source.IsSelected = true;
    }
    int count = analysis.AllSourceNodes.Count(s => s.IsSelected);
    MenuItem Item(string header, ICommand command, string tip) => new() { Header = header, Command = command, [ToolTip.TipProperty] = tip };
    Menu = new ContextMenu {
      ItemsSource = new[] {
        Item(count > 1 ? "Tick selected" : "Tick", analysis.TickSelectedCommand, "Tick the selected sources (and everything below them)"),
        Item(count > 1 ? "Untick selected" : "Untick", analysis.UntickSelectedCommand, "Untick the selected sources"),
        Item(count > 1 ? "Only selected" : "Only this", analysis.OnlySelectedCommand, "Tick the selected sources, untick all others")
      }
    };
    Menu.Open(row);
  }

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
