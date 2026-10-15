using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using HeuristicLab.Studio.Services;

namespace HeuristicLab.Studio.Views;

/// <summary>
/// Which experiments leave the workspace: all of them, or the ticked ones; with autosave, unsaved
/// changes are saved first (else the user is asked for each). ShowDialog returns null when cancelled.
/// </summary>
public sealed class EjectWindow : Window {
  public EjectWindow(IReadOnlyList<string> names, IReadOnlyList<bool> unsaved) {
    Title = "Eject experiments";
    Width = 480;
    Height = 460;
    WindowStartupLocation = WindowStartupLocation.CenterOwner;
    // a TextBlock, not a string: a string's '_' would be taken as an access key ("GA_TSP" shown as "GATSP")
    var boxes = names.Select((n, i) => new CheckBox { Content = new TextBlock { Text = unsaved[i] ? $"{n}   (unsaved)" : n } }).ToList();
    var autosave = new CheckBox { Content = "Autosave: save unsaved experiments before ejecting them" };
    ToolTip.SetTip(autosave, "On: unsaved experiments are saved (one never saved asks for a file name). Off: you are asked for each.");
    var selected = new Button { Content = "Eject selected", IsEnabled = false };
    var all = new Button { Content = "Eject all", Classes = { "accent" } };
    var cancel = new Button { Content = "Cancel" };
    foreach (var box in boxes) box.IsCheckedChanged += (_, _) => selected.IsEnabled = boxes.Any(b => b.IsChecked == true);
    EjectChoice Choice(IEnumerable<int> which) => new(which.ToList(), autosave.IsChecked == true);
    selected.Click += (_, _) => Close(Choice(boxes.Select((b, i) => (b, i)).Where(x => x.b.IsChecked == true).Select(x => x.i)));
    all.Click += (_, _) => Close(Choice(Enumerable.Range(0, names.Count)));
    cancel.Click += (_, _) => Close(null);
    KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(null); };

    var panel = new DockPanel { Margin = new(16) };
    var text = new TextBlock {
      Text = "Eject all experiments, or tick the ones to eject. Their files stay on disk.",
      TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new(0, 0, 0, 10)
    };
    var bottom = new StackPanel {
      Spacing = 12, Margin = new(0, 12, 0, 0),
      Children = {
        autosave,
        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, selected, all } }
      }
    };
    DockPanel.SetDock(text, Dock.Top);
    DockPanel.SetDock(bottom, Dock.Bottom);
    panel.Children.Add(text);
    panel.Children.Add(bottom);
    var list = new StackPanel { Spacing = 2 };
    foreach (var box in boxes) list.Children.Add(box);
    panel.Children.Add(new ScrollViewer { Content = list });
    Content = panel;
  }
}

/// <summary>An unsaved experiment that is about to be ejected: save it, eject it without saving, or keep it open.</summary>
public sealed class UnsavedWindow : Window {
  public UnsavedWindow(string name) {
    Title = "Unsaved experiment";
    Width = 460;
    SizeToContent = SizeToContent.Height;
    CanResize = false;
    WindowStartupLocation = WindowStartupLocation.CenterOwner;
    var save = new Button { Content = "Save", Classes = { "accent" } };
    var discard = new Button { Content = "Eject without saving" };
    var keep = new Button { Content = "Keep it open" };
    save.Click += (_, _) => Close(UnsavedChoice.Save);
    discard.Click += (_, _) => Close(UnsavedChoice.Discard);
    keep.Click += (_, _) => Close(UnsavedChoice.Keep);
    KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(UnsavedChoice.Keep); };
    Content = new StackPanel {
      Margin = new(16), Spacing = 14,
      Children = {
        new TextBlock { Text = $"\"{name}\" has unsaved changes. Save it before ejecting?", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { keep, discard, save } }
      }
    };
  }
}
