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
    // F11 on the Charts tab enters fullscreen (and leaves it again, there)
    AddHandler(KeyDownEvent, (_, e) => {
      if (e.Key == Key.F11 && DataContext is RunAnalysisViewModel { SelectedView: RunAnalysisViewModel.ChartsView }) { e.Handled = true; OpenFullscreen(); }
    }, RoutingStrategies.Tunnel);
  }

  public ChartsFullscreenWindow? Fullscreen { get; private set; }

  // ---- a click anywhere else (empty space, another tab, the table, ...) ends the selection of sources; the
  // handler sits on the window (a click on empty space reaches only the window), menus are windows of their own

  private TopLevel? top;

  protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) {
    base.OnAttachedToVisualTree(e);
    top = TopLevel.GetTopLevel(this);
    top?.AddHandler(PointerPressedEvent, ElsewherePressed, RoutingStrategies.Tunnel, handledEventsToo: true);
  }

  protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) {
    base.OnDetachedFromVisualTree(e);
    top?.RemoveHandler(PointerPressedEvent, ElsewherePressed);
    top = null;
  }

  private void ElsewherePressed(object? sender, PointerPressedEventArgs e) {
    if (DataContext is not RunAnalysisViewModel analysis) return;
    for (var v = e.Source as Avalonia.Visual; v != null; v = Avalonia.VisualTree.VisualExtensions.GetVisualParent(v))
      if (ReferenceEquals(v, SourceTree) || v is MenuItem or Avalonia.Controls.ContextMenu) return;
    foreach (var source in analysis.AllSourceNodes) if (source.IsSelected) source.IsSelected = false;
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
