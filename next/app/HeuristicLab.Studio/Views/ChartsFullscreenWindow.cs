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
/// The Results tab's chart in fullscreen: the chart fills the screen, its legend floats in a panel of its
/// own and, for a box plot, so does the table of its numbers; both panels move by their title bars and
/// resize at their corners. Esc or F11 leaves.
/// </summary>
public sealed class ChartsFullscreenWindow : Window {
  private readonly Canvas canvas = new();
  private readonly RunAnalysisViewModel analysis;

  public FloatingPanel LegendPanel { get; }
  public FloatingPanel TablePanel { get; }

  public ChartsFullscreenWindow(RunAnalysisViewModel analysis) {
    this.analysis = analysis;
    DataContext = analysis;
    Title = "Results: " + analysis.Chart.Title;
    WindowState = WindowState.FullScreen;
    this[!BackgroundProperty] = this.GetResourceObservable("SystemControlBackgroundAltHighBrush").ToBinding();

    var leave = new Button { Content = "✕ Leave fullscreen", Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center };
    leave.Click += (_, _) => Close();
    LegendPanel = new FloatingPanel(canvas, "Legend: click to hide or show", new ChartLegendView { DataContext = analysis.Chart },
      new Size(460, 260), new Size(260, 120), leave);
    var table = new ContentControl { Margin = new Thickness(8, 4, 8, 8) };
    table.Bind(ContentControl.ContentProperty, new Binding(nameof(RunAnalysisViewModel.BoxTable)));
    TablePanel = new FloatingPanel(canvas, "Box plot: numbers per group", table, new Size(900, 220), new Size(320, 120));
    TablePanel.Bind(IsVisibleProperty, new Binding(nameof(RunAnalysisViewModel.IsBoxPlot)));

    Content = new Panel {
      Children = {
        new ContentControl { Content = analysis.Chart, Margin = new Thickness(12) },
        canvas
      }
    };
    // the legend in the panel instead of above the chart
    analysis.Chart.ShowLegend = false;
    Closed += (_, _) => analysis.Chart.ShowLegend = true;
    Opened += (_, _) => PlacePanels();
    // the window reaches its full size after opening: place again until the user moves a panel
    SizeChanged += (_, _) => PlacePanels();
    AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
  }

  /// <summary>Legend at the top right, the table at the bottom (each until the user moves it).</summary>
  private void PlacePanels() {
    double width = Bounds.Width > 0 ? Bounds.Width : 1200, height = Bounds.Height > 0 ? Bounds.Height : 800;
    if (!LegendPanel.Moved) {
      Canvas.SetLeft(LegendPanel, Math.Max(0, width - LegendPanel.Width - 24));
      Canvas.SetTop(LegendPanel, 24);
    }
    if (!TablePanel.Moved) {
      TablePanel.Width = Math.Min(TablePanel.Width, width - 40);
      Canvas.SetLeft(TablePanel, Math.Max(0, (width - TablePanel.Width) / 2));
      Canvas.SetTop(TablePanel, Math.Max(0, height - TablePanel.Height - 24));
    }
  }

  private void OnKey(object? sender, KeyEventArgs e) {
    if (e.Key is Key.Escape or Key.F11) {
      e.Handled = true;
      Close();
    }
  }
}
