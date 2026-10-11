using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace HeuristicLab.Studio.Views;

/// <summary>Asks for a whole number; ShowDialog returns it, or null when cancelled.</summary>
public sealed class NumberWindow : Window {
  public NumberWindow(string title, string message, int initial, int min, int max) {
    Title = title;
    Width = 360;
    SizeToContent = SizeToContent.Height;
    CanResize = false;
    WindowStartupLocation = WindowStartupLocation.CenterOwner;
    var number = new NumericUpDown { Value = initial, Minimum = min, Maximum = max, Increment = 1, FormatString = "0", HorizontalAlignment = HorizontalAlignment.Stretch };
    var ok = new Button { Content = "OK", Classes = { "accent" }, IsDefault = true };
    var cancel = new Button { Content = "Cancel", IsCancel = true };
    ok.Click += (_, _) => Close(number.Value is decimal v ? (int?)decimal.ToInt32(decimal.Round(v)) : null);
    cancel.Click += (_, _) => Close(null);
    KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(null); };
    Opened += (_, _) => number.Focus();
    Content = new StackPanel {
      Margin = new(16), Spacing = 14,
      Children = {
        new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
        number,
        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } }
      }
    };
  }
}
