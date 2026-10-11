using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace HeuristicLab.Studio.Views;

/// <summary>
/// Asks to pick one entry of a list, then confirm with the action button (disabled until something
/// is picked, so nothing happens by accident); ShowDialog returns the entry's index, or null.
/// </summary>
public sealed class ChooseWindow : Window {
  public ChooseWindow(string title, string message, IReadOnlyList<string> items, string action) {
    Title = title;
    Width = 480;
    Height = 420;
    WindowStartupLocation = WindowStartupLocation.CenterOwner;
    var list = new ListBox { ItemsSource = items, SelectionMode = SelectionMode.Single };
    var ok = new Button { Content = action, IsEnabled = false };
    var cancel = new Button { Content = "Cancel", IsCancel = true };
    list.SelectionChanged += (_, _) => ok.IsEnabled = list.SelectedIndex >= 0;
    ok.Click += (_, _) => Close(list.SelectedIndex >= 0 ? list.SelectedIndex : (int?)null);
    cancel.Click += (_, _) => Close(null);
    KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(null); };
    var panel = new DockPanel { Margin = new(16) };
    var text = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new(0, 0, 0, 10) };
    var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 10, 0, 0), Children = { cancel, ok } };
    DockPanel.SetDock(text, Dock.Top);
    DockPanel.SetDock(buttons, Dock.Bottom);
    panel.Children.Add(text);
    panel.Children.Add(buttons);
    panel.Children.Add(list);
    Content = panel;
  }
}
