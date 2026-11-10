using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

/// <summary>
/// A results tab dragged out into a window of its own: at its top the second tab bar (as in the main window: "Main
/// results" brings the main window to the front), below it the Results tab itself (its name - the title bar, changed
/// live - is edited there, next to Refresh).
/// </summary>
public sealed class ResultsWindow : Window {
  public ResultsWindow(ResultsWindowViewModel results, MainViewModel main) {
    DataContext = results;
    Width = 1300;
    Height = 860;
    this.Bind(TitleProperty, new Binding(nameof(ResultsWindowViewModel.Name)));
    var bar = new ResultsTabBar { DataContext = main, Own = results };
    var panel = new DockPanel();
    DockPanel.SetDock(bar, Dock.Top);
    panel.Children.Add(bar);
    panel.Children.Add(new ContentControl { Content = results.Analysis });
    Content = panel;
    this[!BackgroundProperty] = this.GetResourceObservable("WindowBrush").ToBinding();
  }
}
