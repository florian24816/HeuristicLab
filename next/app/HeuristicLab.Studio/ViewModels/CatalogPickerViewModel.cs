using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using HeuristicLab.Next.Runtime;

namespace HeuristicLab.Studio.ViewModels;

/// <summary>Searchable list of catalog entries (everything the loaded plugins offer), grouped and tinted by HeuristicLab's categories.</summary>
public partial class CatalogPickerViewModel(string title, IReadOnlyList<CatalogEntry> entries) : ViewModelBase {
  public string Title { get; } = title;
  public IReadOnlyList<CatalogEntry> All { get; } = Catalog.ByCategory(entries);

  [ObservableProperty]
  public partial string Filter { get; set; } = "";

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(Selected))]
  public partial CatalogItem? SelectedItem { get; set; }

  /// <summary>The chosen entry.</summary>
  public CatalogEntry? Selected {
    get => SelectedItem?.Value as CatalogEntry;
    set => SelectedItem = Visible.FirstOrDefault(i => ReferenceEquals(i.Value, value));
  }

  private IReadOnlyList<CatalogItem>? visible;

  public IReadOnlyList<CatalogItem> Visible => visible ??= CatalogItem.Group(All.Where(Matches).Select(e => (e.Name, e.Description, e.Category, (object?)e)));

  private bool Matches(CatalogEntry e) => Filter.Length == 0
    || e.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)
    || e.Description.Contains(Filter, StringComparison.OrdinalIgnoreCase)
    || e.Category.Contains(Filter, StringComparison.OrdinalIgnoreCase);

  partial void OnFilterChanged(string value) {
    var selected = Selected;
    visible = null;
    OnPropertyChanged(nameof(Visible));
    SelectedItem = Visible.FirstOrDefault(i => ReferenceEquals(i.Value, selected)) ?? Visible.FirstOrDefault();
  }
}
