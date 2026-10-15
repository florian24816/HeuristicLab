using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

/// <summary>
/// Detailed analysis in fullscreen: the picture fills the screen; navigation and chart float over it
/// in a panel that is moved by its title bar and resized at its lower right corner. Esc or F11 leaves.
/// </summary>
public sealed class AnalysisFullscreenWindow : Window {
  private readonly Canvas canvas = new();

  /// <summary>The floating panel with navigation and chart.</summary>
  public FloatingPanel Panel { get; }

  public AnalysisFullscreenWindow(AnalysisViewModel analysis) {
    DataContext = analysis;
    Title = "Detailed analysis";
    WindowState = WindowState.FullScreen;
    this[!BackgroundProperty] = this.GetResourceObservable("SystemControlBackgroundAltHighBrush").ToBinding();

    var leave = new Button { Content = "✕ Leave fullscreen", Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center };
    leave.Click += (_, _) => Close();
    Panel = new FloatingPanel(canvas, "Detailed analysis  ·  drag here to move, the corner to resize  ·  Esc or F11 leaves fullscreen",
      new AnalysisControlsView { Margin = new Thickness(10, 0, 10, 10) }, new Size(980, 420), new Size(420, 220), leave);

    Content = new Panel {
      Children = {
        new ContentControl { Content = analysis.Visuals, Margin = new Thickness(12) },
        canvas
      }
    };
    // the panel starts at the bottom, centred, like the lower half of the normal view
    Opened += (_, _) => PlacePanel();
    // the window reaches its full size after opening: place again until the user moves the panel
    SizeChanged += (_, _) => { if (!Panel.Moved) PlacePanel(); };
    AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
  }

  private void PlacePanel() {
    double width = Bounds.Width > 0 ? Bounds.Width : 1200, height = Bounds.Height > 0 ? Bounds.Height : 800;
    Panel.Width = Math.Min(Panel.Width, width - 40);
    Canvas.SetLeft(Panel, Math.Max(0, (width - Panel.Width) / 2));
    Canvas.SetTop(Panel, Math.Max(0, height - Panel.Height - 24));
  }

  private void OnKey(object? sender, KeyEventArgs e) {
    if (e.Key is Key.Escape or Key.F11) {
      e.Handled = true;
      Close();
    }
  }
}
