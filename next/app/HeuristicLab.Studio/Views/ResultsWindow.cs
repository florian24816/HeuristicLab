using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

/// <summary>
/// A results tab dragged out into a window of its own: at its top the second tab bar (as in the main window: "Main
/// results" brings the main window to the front), below it the window's name (title bar, changed live) - for an
/// overview when working with several windows - and the Results tab itself.
/// </summary>
public sealed class ResultsWindow : Window {
  public ResultsWindow(ResultsWindowViewModel results, MainViewModel main) {
    DataContext = results;
    Width = 1300;
    Height = 860;
    this.Bind(TitleProperty, new Binding(nameof(ResultsWindowViewModel.Name)));
    var bar = new ResultsTabBar { DataContext = main, Own = results };
    var name = new TextBox { Width = 320, VerticalAlignment = VerticalAlignment.Center, Classes = { "windowname" } };
    name.Bind(TextBox.TextProperty, new Binding(nameof(ResultsWindowViewModel.Name)) { Mode = BindingMode.TwoWay });
    var top = new StackPanel {
      Orientation = Orientation.Horizontal, Spacing = 10, Margin = new(14, 8, 14, 4),
      Children = { new TextBlock { Text = "Window name", VerticalAlignment = VerticalAlignment.Center, FontWeight = Avalonia.Media.FontWeight.SemiBold }, name }
    };
    var panel = new DockPanel();
    DockPanel.SetDock(bar, Dock.Top);
    DockPanel.SetDock(top, Dock.Top);
    panel.Children.Add(bar);
    panel.Children.Add(top);
    panel.Children.Add(new ContentControl { Content = results.Analysis });
    Content = panel;
    this[!BackgroundProperty] = this.GetResourceObservable("WindowBrush").ToBinding();
  }
}
