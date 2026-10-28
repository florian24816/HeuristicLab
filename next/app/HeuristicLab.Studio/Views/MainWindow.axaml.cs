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

    // ---- a results window moved over the second tab bar: its tab's gap and ghost show where it would land; it docks
    // back once the mouse button is let go there. The window manager moves windows without telling when the button
    // is released: the button is asked for (GlobalMouse) while the window is over the bar; where that is unknown,
    // the window resting there for DockAfter counts as let go.

    /// <summary>Where the button state is unknown: how long a window must rest over the bar to dock back.</summary>
    public static readonly TimeSpan DockAfter = TimeSpan.FromMilliseconds(500);

    /// <summary>Whether the left mouse button is held (tests replace it).</summary>
    public Func<bool?> IsMouseDown { get; set; } = Services.GlobalMouse.IsLeftButtonDown;

    private readonly Avalonia.Threading.DispatcherTimer dockTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private ResultsWindowViewModel? hovering;
    private int hoverIndex;
    private DateTime restingSince;

    private void WindowMoved(ResultsWindowViewModel results, ResultsWindow window)
    {
        if (results.IsDocked) return;
        var top = new PixelPoint(window.Position.X + (int)(window.Bounds.Width / 2), window.Position.Y + 12);
        WindowOver(results, ResultsBar.Covers(top) ? top : null);
    }

    /// <summary>A results window is over the bar at this screen point (null: not over it).</summary>
    public void WindowOver(ResultsWindowViewModel results, PixelPoint? at)
    {
        if (at is not { } point) {
            if (ReferenceEquals(hovering, results)) StopHovering();
            return;
        }
        hovering = results;
        hoverIndex = ResultsBar.ShowArriving(results, point) ?? subscribed?.ResultsWindows.Count ?? 0;
        restingSince = DateTime.Now;
        if (!dockTimer.IsEnabled) {
            dockTimer.Tick -= CheckLetGo;
            dockTimer.Tick += CheckLetGo;
            dockTimer.Start();
        }
    }

    private void StopHovering()
    {
        hovering = null;
        dockTimer.Stop();
        ResultsBar.HideArriving();
    }

    /// <summary>While a window is over the bar: docks it back once the button is let go (or it rested, where unknown).</summary>
    public void CheckLetGo(object? sender, EventArgs e)
    {
        if (hovering is not { } results || subscribed is not { } main) { dockTimer.Stop(); return; }
        bool letGo = IsMouseDown() is bool down ? !down : DateTime.Now - restingSince >= DockAfter;
        if (!letGo) return;
        int index = hoverIndex;
        StopHovering();
        main.Dock(results, index);
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
