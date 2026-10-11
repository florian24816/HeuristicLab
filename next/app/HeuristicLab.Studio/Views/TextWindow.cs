using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace HeuristicLab.Studio.Views;

/// <summary>Asks for a line of text; ShowDialog returns it, or null when cancelled.</summary>
public sealed class TextWindow : Window {
  public TextWindow(string title, string message, string initial) {
    Title = title;
    Width = 440;
    SizeToContent = SizeToContent.Height;
    CanResize = false;
    WindowStartupLocation = WindowStartupLocation.CenterOwner;
    var text = new TextBox { Text = initial, HorizontalAlignment = HorizontalAlignment.Stretch };
    var ok = new Button { Content = "OK", Classes = { "accent" }, IsDefault = true };
    var cancel = new Button { Content = "Cancel", IsCancel = true };
    ok.Click += (_, _) => Close(text.Text);
    cancel.Click += (_, _) => Close(null);
    KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(null); };
    Opened += (_, _) => {
      text.Focus();
      // the name without its extension is selected for typing over
      int dot = initial.LastIndexOf('.');
      text.SelectionStart = 0;
      text.SelectionEnd = dot > 0 ? dot : initial.Length;
    };
    Content = new StackPanel {
      Margin = new(16), Spacing = 14,
      Children = {
        new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
        text,
        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } }
      }
    };
  }
}
