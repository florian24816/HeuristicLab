using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Metadata;

namespace HeuristicLab.Studio.Controls;

/// <summary>
/// A table row's cells side by side, one control per item made from <see cref="ItemTemplate"/>. Given other items (the
/// row drawn for another run while scrolling) it keeps its controls and only gives them the new items: an ItemsControl
/// would build every cell again, which made scrolling a wide table stutter.
/// </summary>
public sealed class CellRow : StackPanel {
  public static readonly StyledProperty<IReadOnlyList<object>?> ItemsProperty =
    AvaloniaProperty.Register<CellRow, IReadOnlyList<object>?>(nameof(Items));
  public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
    AvaloniaProperty.Register<CellRow, IDataTemplate?>(nameof(ItemTemplate));

  public CellRow() => Orientation = Orientation.Horizontal;

  public IReadOnlyList<object>? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }

  [Content]
  public IDataTemplate? ItemTemplate { get => GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }

  protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
    base.OnPropertyChanged(change);
    if (change.Property == ItemsProperty || change.Property == ItemTemplateProperty) Fill();
  }

  private void Fill() {
    var items = Items ?? [];
    if (TemplateChanged(ItemTemplate)) Children.Clear();
    while (Children.Count > items.Count) Children.RemoveAt(Children.Count - 1);
    for (int i = 0; i < items.Count; i++) {
      if (i < Children.Count) Children[i].DataContext = items[i];
      else if (ItemTemplate?.Build(items[i]) is { } cell) {
        cell.DataContext = items[i];
        Children.Add(cell);
      }
    }
  }

  private IDataTemplate? built;

  /// <summary>Another template: the cells are made anew.</summary>
  private bool TemplateChanged(IDataTemplate? template) {
    bool changed = !ReferenceEquals(template, built);
    built = template;
    return changed;
  }
}
