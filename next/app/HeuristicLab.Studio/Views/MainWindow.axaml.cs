using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

public partial class MainWindow : Window
{
    private MainViewModel? subscribed;
    private readonly Dictionary<ResultsWindowViewModel, ResultsWindow> windows = [];

    /// <summary>The results tabs dragged out into windows of their own, still open.</summary>
    public IReadOnlyList<ResultsWindow> OpenResultsWindows => windows.Values.ToList();

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (subscribed != null) {
            subscribed.ResultsWindowRequested -= ShowResultsWindow;
            subscribed.ResultsWindowCloseRequested -= CloseResultsWindow;
            subscribed.ActivateRequested -= Activate;
        }
        subscribed = DataContext as MainViewModel;
        if (subscribed != null) {
            subscribed.ResultsWindowRequested += ShowResultsWindow;
            subscribed.ResultsWindowCloseRequested += CloseResultsWindow;
            subscribed.ActivateRequested += Activate;
        }
    }

    /// <summary>A results tab dragged out of the bar: a window of its own (not owned: it can go to another screen).</summary>
    private void ShowResultsWindow(ResultsWindowViewModel results)
    {
        if (subscribed is not { } main || windows.ContainsKey(results)) return;
        var window = new ResultsWindow(results, main);
        if (results.WindowAt is { } at) {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(at.X - 120, at.Y - 20);
        }
        windows[results] = window;
        window.Closed += (_, _) => {
            windows.Remove(results);
            main.ResultsWindowClosed(results);
        };
        // moved onto the main window's second tab bar: back in the main window
        window.PositionChanged += (_, _) => {
            var top = new PixelPoint(window.Position.X + (int)(window.Bounds.Width / 2), window.Position.Y + 12);
            if (!results.IsDocked && ResultsBar.Covers(top)) main.Dock(results);
        };
        window.Show();
    }

    private void CloseResultsWindow(ResultsWindowViewModel results)
    {
        if (windows.TryGetValue(results, out var window)) window.Close();
    }

    private void Activate(ResultsWindowViewModel? results)
    {
        if (results == null) Activate();
        else if (windows.TryGetValue(results, out var window)) window.Activate();
    }

    /// <summary>For tests: what a window moved to this screen point does.</summary>
    public bool WouldDock(PixelPoint top) => ResultsBar.Covers(top);
}
