using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

/// <summary>
/// A results window: only a Results tab; at its top, instead of the tabs, the window's name (title bar), changed
/// live - for an overview when working with several windows.
/// </summary>
public sealed class ResultsWindow : Window {
  public ResultsWindow(ResultsWindowViewModel results) {
    DataContext = results;
    Width = 1300;
    Height = 860;
    this.Bind(TitleProperty, new Binding(nameof(ResultsWindowViewModel.Name)));
    var name = new TextBox { Width = 320, VerticalAlignment = VerticalAlignment.Center, Classes = { "windowname" } };
    name.Bind(TextBox.TextProperty, new Binding(nameof(ResultsWindowViewModel.Name)) { Mode = BindingMode.TwoWay });
    var top = new StackPanel {
      Orientation = Orientation.Horizontal, Spacing = 10, Margin = new(14, 10, 14, 4),
      Children = { new TextBlock { Text = "Window name", VerticalAlignment = VerticalAlignment.Center, FontWeight = Avalonia.Media.FontWeight.SemiBold }, name }
    };
    var panel = new DockPanel();
    DockPanel.SetDock(top, Dock.Top);
    panel.Children.Add(top);
    panel.Children.Add(new ContentControl { Content = results.Analysis });
    Content = panel;
    this[!BackgroundProperty] = this.GetResourceObservable("WindowBrush").ToBinding();
  }
}
