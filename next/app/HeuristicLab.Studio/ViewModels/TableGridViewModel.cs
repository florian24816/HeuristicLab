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
/// A shown column, shared by its cells (they bind its width). In the Results table (header buttons): sorted by a
/// double-click on its title (none, ascending, descending); a click on its right edge switches regular / expanded (as
/// wide as its longest text, at most <see cref="TableGridViewModel.ExpandChars"/> characters); fixed columns also have
/// a button collapsing them to its size and back to regular (their right edge expands a collapsed one).
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
  /// <summary>Fixed columns: the collapse / back to regular button.</summary>
  public bool ShowsButton => HasButtons && IsFixed;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(Width), nameof(IsCollapsed), nameof(IsExpanded), nameof(ButtonTip))]
  public partial ColumnSize Size { get; set; }

  [ObservableProperty]
  public partial SortOrder Sort { get; set; }

  public bool IsCollapsed => Size == ColumnSize.Collapsed;
  public bool IsExpanded => Size == ColumnSize.Expanded;

  public string ButtonTip => IsCollapsed ? "Back to the regular width" : "Collapse to this button";

  public double Width => Size switch {
    ColumnSize.Collapsed => TableGridViewModel.CollapsedWidth,
    // a fixed column does not scroll: never wider than the view leaves it, else its edge and button are out of reach
    ColumnSize.Expanded => IsFixed ? Math.Min(expanded ??= grid.ExpandedWidth(index), grid.FixedRoom()) : expanded ??= grid.ExpandedWidth(index),
    _ => Column.Width
  };

  /// <summary>The view's width or the other columns' sizes changed: a fixed expanded column fits again.</summary>
  internal void Refit() {
    if (IsFixed && IsExpanded) OnPropertyChanged(nameof(Width));
  }

  /// <summary>A double-click on the title: none, ascending, descending, none.</summary>
  [RelayCommand]
  private void CycleSort() => grid.SortBy(Key);

  /// <summary>The fixed column's button: collapsed, or regular again.</summary>
  [RelayCommand]
  private void ToggleCollapse() => Resize(IsCollapsed ? ColumnSize.Regular : ColumnSize.Collapsed);

  /// <summary>A click on the column's right edge: expanded, or regular again.</summary>
  [RelayCommand]
  private void ToggleExpand() => Resize(IsExpanded ? ColumnSize.Regular : ColumnSize.Expanded);

  private void Resize(ColumnSize size) {
    Size = size;
    grid.Resized(Key, size);
  }
}

/// <summary>A cell: its text, its background (column color at the row's strength), its column (width, whether it stays in view).</summary>
public sealed record GridCell(string Text, IBrush Background, GridColumnView Column, GridRow? Row) {
  public bool IsFixed => Column.IsFixed;
  public int ZIndex => IsFixed ? 1 : 0;
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

  /// <summary>A marked place (Results tab, "Mark best/worst"): a light layer of its color over the row.</summary>
  [ObservableProperty]
  public partial IBrush? MarkBrush { get; set; }

  /// <summary>The first three places: bold text.</summary>
  [ObservableProperty]
  public partial bool IsBold { get; set; }
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
    // the column sorted by is framed (drawn by the view over the part in view)
    Framed = Views.FirstOrDefault(v => v.Sort != SortOrder.None);
    var all = rows.ToList();
    texts = all.Select(r => r.Texts).ToList();
    Rows = all.Select((r, index) => {
      double strength = index % 2 == 0 ? 1 : WeakRow;
      var texts = r.Texts;
      return new GridRow(r.Item, row => {
        // a row may wear other colors in its fixed columns (a run open in a window), in their order
        var tint = FixedTint?.Invoke(row.Item);
        int fixedIndex = 0;
        return columns.Select((c, i) => {
          var color = c.Fixed != null && tint != null && fixedIndex < tint.Count ? tint[fixedIndex++] : colors[i];
          return new GridCell(i < texts.Count ? texts[i] : "", Brush(color, strength), Views[i], row);
        }).ToList();
      });
    }).ToList();
    ShownRows = Rows.Count > Limit ? Rows.Take(Limit).ToList() : Rows;
    // the header row one shade darker than the rows below it
    Header = columns.Select((c, i) => new GridCell(c.Header, Brush(Darker(colors[i]), 1), Views[i], null)).ToList();
  }

  /// <summary>The columns as shown (sizes, sorting), in order.</summary>
  public IReadOnlyList<GridColumnView> Views { get; }

  /// <summary>Titles with buttons that sort and resize (the Results table).</summary>
  public bool HeaderButtons { get; }

  /// <summary>Where the column sizes are kept for the next table (sorted, filtered); null: not kept.</summary>
  private IDictionary<string, ColumnSize>? Sizes { get; }

  /// <summary>The column the table is sorted by (null: not sorted).</summary>
  public GridColumnView? Framed { get; }

  public const double FrameWidth = 3;
  public static readonly Color FrameOrange = Color.Parse("#F7931D"), FrameBlack = Color.Parse("#000000");

  /// <summary>
  /// The sorted column's frame, one gradient over the part of it in view: from its title to the window's lower edge
  /// (or its last row, once in view), ascending the logo orange at the top to black at the bottom, descending the
  /// other way.
  /// </summary>
  public IBrush? FrameBrush => Framed == null ? null : Framed.Sort == SortOrder.Descending ? FrameUp : FrameDown;

  private static readonly IBrush FrameDown = Gradient(FrameOrange, FrameBlack), FrameUp = Gradient(FrameBlack, FrameOrange);

  private static IBrush Gradient(Color top, Color bottom) =>
    new ImmutableLinearGradientBrush([new ImmutableGradientStop(0, top), new ImmutableGradientStop(1, bottom)],
      startPoint: new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative), endPoint: new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative));

  /// <summary>The fixed columns' button: as wide as this, as tall as the title row, at the column's right edge.</summary>
  public const double ButtonWidth = 26;

  /// <summary>A column's right edge (in every row) that a click expands or makes regular again.</summary>
  public const double EdgeWidth = 8;

  /// <summary>A collapsed fixed column: as wide as its button.</summary>
  public const double CollapsedWidth = ButtonWidth;

  /// <summary>An expanded column shows texts up to this many characters.</summary>
  public const int ExpandChars = 200;

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
    double buttons = Views[index].ShowsButton ? ButtonWidth : EdgeWidth;
    double header = Measure(Columns[index].Header, FontWeight.SemiBold) + 6 + 3 + buttons;
    return Math.Ceiling(Math.Max(Columns[index].Width, Math.Max(cells, header)));
  }

  internal void Resized(string key, ColumnSize size) {
    if (Sizes != null) Sizes[key] = size;
    foreach (var view in Views) view.Refit();
  }

  /// <summary>How wide the table's view is (set by the view; 0: not known yet).</summary>
  public double ViewWidth {
    get;
    set {
      if (field == value) return;
      field = value;
      foreach (var view in Views) view.Refit();
    }
  }

  /// <summary>Room in the view for an expanded fixed column: what the other fixed columns leave, shared among the expanded ones.</summary>
  internal double FixedRoom() {
    if (ViewWidth <= 0) return double.PositiveInfinity;
    var fixedViews = Views.Where(v => v.IsFixed).ToList();
    double others = fixedViews.Where(v => !v.IsExpanded).Sum(v => v.Width);
    int expanded = Math.Max(1, fixedViews.Count(v => v.IsExpanded));
    return Math.Max(Columns[0].Width, (ViewWidth - others - FixedSpare) / expanded);
  }

  /// <summary>Left in view beside the fixed columns, however wide they are.</summary>
  public const double FixedSpare = 60;

  /// <summary>A double-click on a title: the owner sorts again and makes a new table.</summary>
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
