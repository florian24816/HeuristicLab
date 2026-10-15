using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

/// <summary>
/// An operator graph in fullscreen: the graph (with its navigation and zoom) fills the screen, the
/// selected operator's parameters float in a panel that moves, resizes and minimizes. Esc or F11 leaves.
/// </summary>
public sealed class OperatorGraphFullscreenWindow : Window {
  private readonly Canvas canvas = new();

  public OperatorGraphView Graph { get; }
  public FloatingPanel EditorPanel { get; }

  public OperatorGraphFullscreenWindow(OperatorGraphViewModel graph) {
    DataContext = graph;
    Title = "Operator graph";
    WindowState = WindowState.FullScreen;
    this[!BackgroundProperty] = this.GetResourceObservable("SystemControlBackgroundAltHighBrush").ToBinding();

    var leave = new Button { Content = "✕ Leave fullscreen", Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center };
    leave.Click += (_, _) => Close();
    var editor = new ContentControl { Margin = new Thickness(10, 6, 10, 10) };
    editor.Bind(ContentControl.ContentProperty, new Binding(nameof(OperatorGraphViewModel.SelectedEditor)));
    EditorPanel = new FloatingPanel(canvas, "Selected operator: parameters", new ScrollViewer { Content = editor },
      new Size(560, 420), new Size(320, 160), leave, minimizable: true);

    Graph = new OperatorGraphView(showsEditor: false) { DataContext = graph, Margin = new Thickness(12) };
    Content = new Panel { Children = { Graph, canvas } };
    Opened += (_, _) => PlacePanel();
    // the window reaches its full size after opening: place again until the user moves the panel
    SizeChanged += (_, _) => PlacePanel();
    AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
  }

  /// <summary>The parameters at the lower right, until the user moves the panel.</summary>
  private void PlacePanel() {
    if (EditorPanel.Moved) return;
    double width = Bounds.Width > 0 ? Bounds.Width : 1200, height = Bounds.Height > 0 ? Bounds.Height : 800;
    Canvas.SetLeft(EditorPanel, Math.Max(0, width - EditorPanel.Width - 24));
    double panelHeight = double.IsNaN(EditorPanel.Height) ? EditorPanel.Bounds.Height : EditorPanel.Height;
    Canvas.SetTop(EditorPanel, Math.Max(0, height - panelHeight - 24));
  }

  private void OnKey(object? sender, KeyEventArgs e) {
    if (e.Key is Key.Escape or Key.F11) {
      e.Handled = true;
      Close();
    }
  }
}
