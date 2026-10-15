using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace HeuristicLab.Studio.ViewModels;

/// <summary>A column of a <see cref="TableGridViewModel"/>; a fixed column has its own shade and never scrolls away.</summary>
public sealed record GridColumn(string Key, string Header, double Width, Color? Fixed = null);

/// <summary>A cell: its text, its background (column color at the row's strength) and whether it stays in view.</summary>
public sealed record GridCell(string Text, IBrush Background, double Width, bool IsFixed, GridRow? Row) {
  public int ZIndex => IsFixed ? 1 : 0;
}

public sealed partial class GridRow(object item, Func<GridRow, IReadOnlyList<GridCell>> cells) : ObservableObject {
  private IReadOnlyList<GridCell>? built;

  public object Item { get; } = item;

  /// <summary>Built when the row is first drawn: a long table makes cells only for the rows on screen.</summary>
  public IReadOnlyList<GridCell> Cells => built ??= cells(this);

  [ObservableProperty]
  public partial bool IsSelected { get; set; }
}

/// <summary>
/// A table of runs that stays readable: columns alternate grey and light grey, rows alternate in
/// strength, fixed columns (where a run comes from, which run it is) are orange and stay in view
/// while the table scrolls sideways, the header stays in view while it scrolls down.
/// Long tables show their first <see cref="Limit"/> rows; a button as wide as the table shows all of them.
/// </summary>
public partial class TableGridViewModel : ViewModelBase {
  public static readonly Color Grey = Color.Parse("#D6D6D6"), LightGrey = Color.Parse("#EBEBEB");
  // the logos' orange is #F7931D; the run comes closest to it, its source is the palest
  public static readonly Color SourceShade = Color.Parse("#FDE5C8"), OriginShade = Color.Parse("#FBCD96"), RunShade = Color.Parse("#F9B462");
  /// <summary>Every other row: the colors at this strength (over white).</summary>
  public const double WeakRow = 0.55;

  private readonly Action<object?> selected;
  private readonly Action<string>? sort;

  public TableGridViewModel(IReadOnlyList<GridColumn> columns, IEnumerable<(object Item, IReadOnlyList<string> Texts)> rows,
      Action<object?> selected, Action<string>? sort = null) {
    this.selected = selected;
    this.sort = sort;
    Columns = columns;
    var colors = new List<Color>();
    int plain = 0;
    foreach (var c in columns) colors.Add(c.Fixed ?? (plain++ % 2 == 0 ? Grey : LightGrey));
    // the header row one shade darker than the rows below it
    Header = columns.Select((c, i) => new GridCell(c.Header, Brush(Darker(colors[i]), 1), c.Width, c.Fixed != null, null)).ToList();
    Rows = rows.Select((r, index) => {
      double strength = index % 2 == 0 ? 1 : WeakRow;
      var texts = r.Texts;
      return new GridRow(r.Item, row => columns.Select((c, i) => new GridCell(i < texts.Count ? texts[i] : "", Brush(colors[i], strength), c.Width, c.Fixed != null, row)).ToList());
    }).ToList();
    ShownRows = Rows.Count > Limit ? Rows.Take(Limit).ToList() : Rows;
  }

  /// <summary>Rows shown before the table is expanded.</summary>
  public const int Limit = 50;

  public IReadOnlyList<GridColumn> Columns { get; }
  public IReadOnlyList<GridCell> Header { get; }
  public IReadOnlyList<GridRow> Rows { get; }
  public bool CanSort => sort != null;

  /// <summary>The rows drawn: the first <see cref="Limit"/>, or all once expanded.</summary>
  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(ExpandText))]
  public partial IReadOnlyList<GridRow> ShownRows { get; private set; }

  /// <summary>More rows than shown, or all shown of more than <see cref="Limit"/>: the button below the table.</summary>
  public bool CanExpand => Rows.Count > Limit;

  public bool IsExpanded => ShownRows.Count > Limit;

  public string ExpandText => IsExpanded
    ? $"Show only the first {Limit} of {Rows.Count} rows"
    : $"Show all {Rows.Count} rows ({Limit} shown)";

  /// <summary>The button below the table: all rows, or the first <see cref="Limit"/> again.</summary>
  [RelayCommand]
  private void Expand() {
    ShownRows = IsExpanded ? Rows.Take(Limit).ToList() : Rows;
    OnPropertyChanged(nameof(IsExpanded));
  }

  /// <summary>Marks the row of this item as selected (without telling the owner, who selected it).</summary>
  public void Mark(object? item) {
    foreach (var row in Rows) row.IsSelected = ReferenceEquals(row.Item, item);
  }

  /// <summary>A click on a row.</summary>
  public void Select(GridRow row) {
    Mark(row.Item);
    selected(row.Item);
  }

  [RelayCommand]
  private void Sort(string key) => sort?.Invoke(key);

  private static readonly Dictionary<(Color, double), IBrush> brushes = [];

  /// <summary>The header's shade of a column color.</summary>
  public static Color Darker(Color color) {
    static byte Shade(byte c) => (byte)Math.Round(c * HeaderShade);
    return Color.FromRgb(Shade(color.R), Shade(color.G), Shade(color.B));
  }

  /// <summary>The header row's colors: the column colors at this brightness.</summary>
  public const double HeaderShade = 0.9;

  public static IBrush Brush(Color color, double strength) {
    lock (brushes) {
      if (!brushes.TryGetValue((color, strength), out var brush)) {
        byte Mix(byte c) => (byte)Math.Round(c * strength + 255 * (1 - strength));
        brushes[(color, strength)] = brush = new ImmutableSolidColorBrush(Color.FromRgb(Mix(color.R), Mix(color.G), Mix(color.B)));
      }
      return brush;
    }
  }
}
