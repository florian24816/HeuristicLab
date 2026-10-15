using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

public partial class OperatorGraphView : UserControl {
  public OperatorGraphView() : this(showsEditor: true) { }

  /// <param name="showsEditor">False: only the graph (fullscreen shows the selected operator's parameters in a panel of its own).</param>
  public OperatorGraphView(bool showsEditor) {
    InitializeComponent();
    if (!showsEditor) {
      Root.RowDefinitions = new RowDefinitions("Auto,*,0,0");
      EditorSplitter.IsVisible = EditorArea.IsVisible = FullscreenButton.IsVisible = false;
    }
    // mark the selected node (styles cannot compare records with the view model's selection)
    DataContextChanged += (_, _) => {
      if (DataContext is OperatorGraphViewModel vm)
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(OperatorGraphViewModel.Selected)) MarkSelected(vm); };
    };
    LayoutUpdated += (_, _) => { if (DataContext is OperatorGraphViewModel vm) MarkSelected(vm); };
    // F11 enters fullscreen (and leaves it again, there)
    if (showsEditor)
      AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.F11) { e.Handled = true; OpenFullscreen(); } }, RoutingStrategies.Tunnel);
  }

  public OperatorGraphFullscreenWindow? Fullscreen { get; private set; }

  private void OnFullscreen(object? sender, RoutedEventArgs e) => OpenFullscreen();

  /// <summary>The graph filling the screen, the selected operator's parameters in a floating panel.</summary>
  public OperatorGraphFullscreenWindow? OpenFullscreen() {
    if (DataContext is not OperatorGraphViewModel graph || Fullscreen != null) return Fullscreen;
    Fullscreen = new OperatorGraphFullscreenWindow(graph);
    Fullscreen.Closed += (_, _) => Fullscreen = null;
    if (TopLevel.GetTopLevel(this) is Window owner) Fullscreen.Show(owner);
    else Fullscreen.Show();
    return Fullscreen;
  }

  private void MarkSelected(OperatorGraphViewModel vm) {
    foreach (var border in this.GetVisualDescendants().OfType<Border>())
      if (border.DataContext is GraphNodeView node && border.Classes.Contains("node"))
        border.Classes.Set("selected", ReferenceEquals(node, vm.Selected));
  }

  private void NodeTapped(object? sender, TappedEventArgs e) {
    if ((sender as Control)?.DataContext is GraphNodeView node && DataContext is OperatorGraphViewModel vm) vm.Selected = node;
  }

  private void NodeDoubleTapped(object? sender, TappedEventArgs e) {
    if (DataContext is OperatorGraphViewModel vm && vm.OpenCommand.CanExecute(null)) vm.OpenCommand.Execute(null);
  }
}
