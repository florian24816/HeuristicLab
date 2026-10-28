using Avalonia.Controls;
using Avalonia.Layout;

namespace HeuristicLab.Studio.Views;

/// <summary>Yes/no question; ShowDialog returns true for yes (closing the window: no).</summary>
public sealed class ConfirmWindow : Window {
  /// <param name="yes">The confirming button's text.</param>
  /// <param name="cancel">A Cancel button too (without: only closing the window says no).</param>
  public ConfirmWindow(string title, string message, string yes = "Delete", bool cancel = true) {
    Title = title;
    Width = 420;
    SizeToContent = SizeToContent.Height;
    CanResize = false;
    WindowStartupLocation = WindowStartupLocation.CenterOwner;
    var ok = new Button { Content = yes, Classes = { "accent" } };
    var no = new Button { Content = "Cancel", IsVisible = cancel };
    ok.Click += (_, _) => Close(true);
    no.Click += (_, _) => Close(false);
    Content = new StackPanel {
      Margin = new(16), Spacing = 14,
      Children = {
        new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { no, ok } }
      }
    };
  }
}
