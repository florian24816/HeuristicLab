using System;
using Avalonia.Controls;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

public partial class MainWindow : Window
{
    private MainViewModel? subscribed;

    /// <summary>The results windows opened from here and still open.</summary>
    public System.Collections.Generic.List<ResultsWindow> OpenResultsWindows { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Results windows open as windows of their own (not owned: they can go to another screen).</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (subscribed != null) {
            subscribed.ResultsWindowRequested -= ShowResultsWindow;
            subscribed.CloseResultsWindowsRequested -= CloseResultsWindows;
        }
        subscribed = DataContext as MainViewModel;
        if (subscribed != null) {
            subscribed.ResultsWindowRequested += ShowResultsWindow;
            subscribed.CloseResultsWindowsRequested += CloseResultsWindows;
        }
    }

    private void CloseResultsWindows()
    {
        foreach (var window in OpenResultsWindows.ToArray()) window.Close();
    }

    private void ShowResultsWindow(ResultsWindowViewModel results)
    {
        var window = new ResultsWindow(results);
        OpenResultsWindows.Add(window);
        window.Closed += (_, _) => {
            OpenResultsWindows.Remove(window);
            subscribed?.ResultsWindowClosed(results);
        };
        window.Show();
    }
}
