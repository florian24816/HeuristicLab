using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using HeuristicLab.Studio.Controls;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

/// <summary>An entry of a <see cref="ChooseWindow"/>.</summary>
public sealed partial class ChoiceRow(string text) : ObservableObject, IPickable {
  public string Text { get; } = text;

  [ObservableProperty]
  public partial bool IsSelected { get; set; }
}

/// <summary>
/// Asks to pick entries of a list - one by a click, several by Ctrl+click, Shift+click or pressing and
/// sweeping over them (shown inverted) - then confirm with the action button (disabled until something is
/// picked, so nothing happens by accident); ShowDialog returns the picked entries' indices, or null.
/// </summary>
public sealed class ChooseWindow : Window {
  public ChooseWindow(string title, string message, IReadOnlyList<string> items, string action) {
    Title = title;
    Width = 520;
    Height = 440;
    WindowStartupLocation = WindowStartupLocation.CenterOwner;
    Rows = items.Select(i => new ChoiceRow(i)).ToList();
    var ok = new Button { Content = action, IsEnabled = false };
    var cancel = new Button { Content = "Cancel", IsCancel = true };
    foreach (var row in Rows) row.PropertyChanged += (_, _) => ok.IsEnabled = Rows.Any(r => r.IsSelected);
    var list = new ItemsControl {
      ItemsSource = Rows,
      ItemTemplate = new FuncDataTemplate<ChoiceRow>((row, _) => {
        var border = new Border { Classes = { "selectrow" }, Child = new TextBlock { Text = row?.Text, TextWrapping = Avalonia.Media.TextWrapping.Wrap } };
        if (row != null) {
          border.Classes.Set("selected", row.IsSelected);
          row.PropertyChanged += (_, _) => border.Classes.Set("selected", row.IsSelected);
        }
        return border;
      })
    };
    var scroll = new ScrollViewer { Content = list, Background = Avalonia.Media.Brushes.Transparent };
    SweepSelection.Attach(scroll, () => Rows);
    ok.Click += (_, _) => Close(Picked());
    cancel.Click += (_, _) => Close(null);
    KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(null); };
    var panel = new DockPanel { Margin = new(16) };
    var text = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new(0, 0, 0, 6) };
    var hint = new TextBlock {
      Text = "Several: Ctrl+click, Shift+click, or press and sweep over them.", FontSize = 11, Opacity = 0.65, Margin = new(0, 0, 0, 8)
    };
    var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 10, 0, 0), Children = { cancel, ok } };
    DockPanel.SetDock(text, Dock.Top);
    DockPanel.SetDock(hint, Dock.Top);
    DockPanel.SetDock(buttons, Dock.Bottom);
    panel.Children.Add(text);
    panel.Children.Add(hint);
    panel.Children.Add(buttons);
    panel.Children.Add(scroll);
    Content = panel;
  }

  public IReadOnlyList<ChoiceRow> Rows { get; }

  private IReadOnlyList<int>? Picked() {
    var picked = Rows.Select((r, i) => (r, i)).Where(p => p.r.IsSelected).Select(p => p.i).ToList();
    return picked.Count > 0 ? picked : null;
  }
}
