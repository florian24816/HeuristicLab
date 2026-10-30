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

/// <summary>
/// The Results table's titles with buttons: how it is sorted, and the column sizes (kept by the owner, so a table made
/// again - sorted, filtered - keeps them).
/// </summary>
public sealed record TableLook(string? SortKey, SortOrder Sort, IDictionary<string, ColumnSize> Sizes);

/// <summary>How wide a column is drawn: fixed columns have all three, the others regular or expanded.</summary>
public enum ColumnSize { Collapsed, Regular, Expanded }

/// <summary>How the table is sorted by a column (one column at a time).</summary>
public enum SortOrder { None, Ascending, Descending }

/// <summary>
/// A shown column, shared by its cells (they bind its width): with header buttons (Results table) a button that sorts
/// (none, ascending, descending) and its size - fixed columns collapse, are regular or expand, the others are regular
/// or expanded (as wide as their longest text, at most <see cref="TableGridViewModel.ExpandChars"/> characters).
/// </summary>
public sealed partial class GridColumnView : ObservableObject {
  private readonly TableGridViewModel grid;
  private readonly int index;
  private double? expanded;

  internal GridColumnView(TableGridViewModel grid, int index, GridColumn column, ColumnSize size, SortOrder sort) {
    this.grid = grid;
    this.index = index;
    Column = column;
    Size = size;
    Sort = sort;
  }

  public GridColumn Column { get; }
  public string Key => Column.Key;
  public bool IsFixed => Column.Fixed != null;
  public bool HasButtons => grid.HeaderButtons;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(Width), nameof(IsCollapsed), nameof(IsExpanded), nameof(ShowsCollapse), nameof(ShowsToggle))]
  [NotifyCanExecuteChangedFor(nameof(ExpandCommand), nameof(CollapseCommand))]
  public partial ColumnSize Size { get; set; }

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(IsAscending), nameof(IsDescending), nameof(SortTip))]
  public partial SortOrder Sort { get; set; }

  public bool IsAscending => Sort == SortOrder.Ascending;
  public bool IsDescending => Sort == SortOrder.Descending;
  public bool IsCollapsed => Size == ColumnSize.Collapsed;
  public bool IsExpanded => Size == ColumnSize.Expanded;
  /// <summary>Fixed columns: "collapse" while not collapsed. Other columns: one button switching the size.</summary>
  public bool ShowsCollapse => HasButtons && IsFixed && !IsCollapsed;
  public bool ShowsExpand => HasButtons && IsFixed;
  public bool ShowsToggle => HasButtons && !IsFixed;

  public string SortTip => Sort switch {
    SortOrder.Ascending => "Sorted ascending: click for descending (or double-click the title)",
    SortOrder.Descending => "Sorted descending: click to stop sorting (or double-click the title)",
    _ => "Not sorted: click to sort ascending (or double-click the title)"
  };

  public double Width => Size switch {
    ColumnSize.Collapsed => TableGridViewModel.CollapsedWidth,
    ColumnSize.Expanded => expanded ??= grid.ExpandedWidth(index),
    _ => Column.Width
  };

  /// <summary>The sort button (or a double-click on the title): none, ascending, descending, none.</summary>
  [RelayCommand]
  private void CycleSort() => grid.SortBy(Key);

  [RelayCommand(CanExecute = nameof(CanExpand))]
  private void Expand() => Resize(Size == ColumnSize.Collapsed ? ColumnSize.Regular : ColumnSize.Expanded);

  private bool CanExpand => Size != ColumnSize.Expanded;

  [RelayCommand(CanExecute = nameof(CanCollapse))]
  private void Collapse() => Resize(Size == ColumnSize.Expanded ? ColumnSize.Regular : ColumnSize.Collapsed);

  private bool CanCollapse => IsFixed ? Size != ColumnSize.Collapsed : Size == ColumnSize.Expanded;

  /// <summary>A column that is not fixed: regular or expanded.</summary>
  [RelayCommand]
  private void ToggleSize() => Resize(IsExpanded ? ColumnSize.Regular : ColumnSize.Expanded);

  private void Resize(ColumnSize size) {
    Size = size;
    grid.Resized(Key, size);
  }
}

/// <summary>A cell: its text, its background (column color at the row's strength), its column (width, whether it stays in view).</summary>
public sealed record GridCell(string Text, IBrush Background, GridColumnView Column, GridRow? Row) {
  public bool IsFixed => Column.IsFixed;
  public int ZIndex => IsFixed ? 1 : 0;
  /// <summary>Part of the frame around the column the table is sorted by (null: none).</summary>
  public IBrush? FrameBrush { get; init; }
  public Avalonia.Thickness FrameThickness { get; init; }
}

public sealed partial class GridRow(object item, Func<GridRow, IReadOnlyList<GridCell>> cells) : ObservableObject, IPickable {
  private IReadOnlyList<GridCell>? built;

  public object Item { get; } = item;

  /// <summary>Built when the row is first drawn: a long table makes cells only for the rows on screen.</summary>
  public IReadOnlyList<GridCell> Cells => built ??= cells(this);

  /// <summary>Its colors changed: the cells are made again (when drawn).</summary>
  internal void Rebuild() {
    built = null;
    OnPropertyChanged(nameof(Cells));
  }

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
  private readonly IReadOnlyList<IReadOnlyList<string>> texts;

  public TableGridViewModel(IReadOnlyList<GridColumn> columns, IEnumerable<(object Item, IReadOnlyList<string> Texts)> rows,
      Action<object?> selected, Action<string>? sort = null, TableLook? look = null) {
    this.selected = selected;
    this.sort = sort;
    Columns = columns;
    HeaderButtons = look != null;
    var sizes = look?.Sizes;
    Views = columns.Select((c, i) => new GridColumnView(this, i, c,
      sizes != null && sizes.TryGetValue(c.Key, out var size) && (c.Fixed != null || size != ColumnSize.Collapsed) ? size : ColumnSize.Regular,
      look?.SortKey == c.Key ? look.Sort : SortOrder.None)).ToList();
    Sizes = sizes;
    var colors = new List<Color>();
    int plain = 0;
    foreach (var c in columns) colors.Add(c.Fixed ?? (plain++ % 2 == 0 ? Grey : LightGrey));
    // the column sorted by (not a fixed one) is framed from its title to its last row shown: one end green, the
    // other red (ascending: green at the top), a gradient between them along its sides
    framed = Views.FirstOrDefault(v => v.Sort != SortOrder.None && !v.IsFixed);
    var all = rows.ToList();
    texts = all.Select(r => r.Texts).ToList();
    Rows = all.Select((r, index) => {
      double strength = index % 2 == 0 ? 1 : WeakRow;
      var texts = r.Texts;
      return new GridRow(r.Item, row => {
        // a row may wear other colors in its fixed columns (a run open in a window), in their order
        var tint = FixedTint?.Invoke(row.Item);
        int fixedIndex = 0;
        bool last = index == ShownRows.Count - 1;
        return columns.Select((c, i) => {
          var color = c.Fixed != null && tint != null && fixedIndex < tint.Count ? tint[fixedIndex++] : colors[i];
          return new GridCell(i < texts.Count ? texts[i] : "", Brush(color, strength), Views[i], row) {
            FrameBrush = Views[i] == framed && index < ShownRows.Count ? Frame(index + 1) : null,
            FrameThickness = new(FrameWidth, 0, FrameWidth, last ? FrameWidth : 0)
          };
        }).ToList();
      });
    }).ToList();
    ShownRows = Rows.Count > Limit ? Rows.Take(Limit).ToList() : Rows;
    // the header row one shade darker than the rows below it
    Header = columns.Select((c, i) => new GridCell(c.Header, Brush(Darker(colors[i]), 1), Views[i], null) {
      FrameBrush = Views[i] == framed ? Frame(0) : null, FrameThickness = new(FrameWidth, FrameWidth, FrameWidth, 0)
    }).ToList();
  }

  /// <summary>The columns as shown (sizes, sorting), in order.</summary>
  public IReadOnlyList<GridColumnView> Views { get; }

  /// <summary>Titles with buttons that sort and resize (the Results table).</summary>
  public bool HeaderButtons { get; }

  /// <summary>Where the column sizes are kept for the next table (sorted, filtered); null: not kept.</summary>
  private IDictionary<string, ColumnSize>? Sizes { get; }

  private readonly GridColumnView? framed;

  public const double FrameWidth = 3;
  public static readonly Color FrameGreen = Color.Parse("#2EA043"), FrameRed = Color.Parse("#D73A3A");

  /// <summary>The frame's part along the header (0) or a shown row (1..): its share of the gradient from one end to the other.</summary>
  private IBrush Frame(int part) {
    int parts = ShownRows.Count + 1;
    bool down = framed?.Sort == SortOrder.Descending;
    Color At(double t) {
      var (a, b) = down ? (FrameRed, FrameGreen) : (FrameGreen, FrameRed);
      byte Mix(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
      return Color.FromRgb(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B));
    }
    return new ImmutableLinearGradientBrush([new ImmutableGradientStop(0, At((double)part / parts)), new ImmutableGradientStop(1, At((double)(part + 1) / parts))],
      startPoint: new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative), endPoint: new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative));
  }

  /// <summary>A collapsed fixed column: its two buttons and a few letters.</summary>
  public const double CollapsedWidth = 58;

  /// <summary>An expanded column shows texts up to this many characters.</summary>
  public const int ExpandChars = 120;

  /// <summary>As wide as the column's longest text (cut at <see cref="ExpandChars"/>) and its title with its buttons; at least regular.</summary>
  internal double ExpandedWidth(int index) {
    double Measure(string text, FontWeight weight = FontWeight.Normal) {
      if (text.Length > ExpandChars) text = text[..ExpandChars] + "…";
      using var layout = new Avalonia.Media.TextFormatting.TextLayout(text, new Typeface(FontFamily.Default, weight: weight), 14, null);
      return layout.WidthIncludingTrailingWhitespace;
    }
    // the longest texts by characters are measured (thousands of rows: not every one)
    var longest = texts.Select(t => index < t.Count ? t[index] : "").Distinct().OrderByDescending(t => t.Length).Take(8);
    double cells = longest.Select(t => Measure(t)).DefaultIfEmpty(0).Max() + 12 + 4;
    double buttons = Views[index].IsFixed ? 3 * 18 + 2 * 2 : 2 * 18 + 2;
    double header = Measure(Columns[index].Header, FontWeight.SemiBold) + 12 + 6 + buttons + 4;
    return Math.Ceiling(Math.Max(Columns[index].Width, Math.Max(cells, header)));
  }

  internal void Resized(string key, ColumnSize size) {
    if (Sizes != null) Sizes[key] = size;
  }

  /// <summary>The sort button: the owner sorts again and makes a new table.</summary>
  internal void SortBy(string key) => sort?.Invoke(key);

  /// <summary>Other colors for a row's fixed columns (null: their own).</summary>
  public Func<object, IReadOnlyList<Color>?>? FixedTint { get; set; }

  /// <summary>The menu (right-click) for the selected rows' items, if any.</summary>
  public Func<IReadOnlyList<object>, IReadOnlyList<MenuChoice>>? RowMenu { get; set; }

  /// <summary>How far the table is scrolled sideways (a table made again, e.g. sorted, starts there again).</summary>
  public double ScrollX { get; set; }

  /// <summary>Several rows can be selected (Ctrl+click, Shift+click, a rectangle - Controls.SweepSelection).</summary>
  public bool MultiSelect { get; init; }

  /// <summary>The selected rows' items, in table order.</summary>
  public IReadOnlyList<object> SelectedItems => Rows.Where(r => r.IsSelected).Select(r => r.Item).ToList();

  /// <summary><see cref="FixedTint"/> changed: the rows are colored again.</summary>
  public void Retint() {
    foreach (var row in Rows) row.Rebuild();
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
    // the sorted column's frame ends at the last row shown, its gradient spread over them
    if (framed != null) Retint();
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
