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
        window.PositionChanged += (_, _) => WindowMoved(results, window);
        window.Show();
    }

    // ---- a results window moved onto the second tab bar: a faded tab shows where it would land; it docks back
    // once it was let go there. The window manager moves windows and does not tell when the button is released:
    // the window resting over the bar for a moment counts as let go.

    /// <summary>How long a window must rest over the bar to dock back.</summary>
    public static readonly TimeSpan DockAfter = TimeSpan.FromMilliseconds(500);
    private Avalonia.Threading.DispatcherTimer? dockTimer;
    private ResultsWindowViewModel? hovering;
    private int hoverIndex;

    private void WindowMoved(ResultsWindowViewModel results, ResultsWindow window)
    {
        if (subscribed is not { } main || results.IsDocked) return;
        var top = new PixelPoint(window.Position.X + (int)(window.Bounds.Width / 2), window.Position.Y + 12);
        WindowOver(results, ResultsBar.Covers(top) ? ResultsBar.IndexAt(top) : null);
    }

    /// <summary>A results window is over the bar at this place (null: not over it): the faded tab, and docking once it rests.</summary>
    public void WindowOver(ResultsWindowViewModel results, int? index)
    {
        if (subscribed is not { } main) return;
        dockTimer?.Stop();
        if (index is not int at) {
            if (ReferenceEquals(hovering, results)) main.PreviewTab(null, 0);
            hovering = null;
            return;
        }
        hovering = results;
        hoverIndex = at;
        main.PreviewTab(results, at);
        dockTimer ??= new Avalonia.Threading.DispatcherTimer { Interval = DockAfter };
        dockTimer.Tick -= DockResting;
        dockTimer.Tick += DockResting;
        dockTimer.Start();
    }

    private void DockResting(object? sender, EventArgs e)
    {
        dockTimer?.Stop();
        if (hovering is { } results && subscribed is { } main) {
            hovering = null;
            main.Dock(results, hoverIndex);
        }
    }

    // ---- closing the main window while results windows are open: asked first, as "Close windows" does

    private bool closeConfirmed;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (closeConfirmed || subscribed is not { } main || windows.Count == 0) return;
        e.Cancel = true;
        if (!await main.ConfirmCloseResultsWindowsAsync(windows.Count)) return;
        closeConfirmed = true;
        foreach (var window in windows.Values.ToArray()) window.Close();
        Close();
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
