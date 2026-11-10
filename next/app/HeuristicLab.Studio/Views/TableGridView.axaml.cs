using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

public partial class TableGridView : UserControl {
  public TableGridView() {
    InitializeComponent();
    // a table of several selectable rows: click, Ctrl+click, Shift+click, a rectangle (as the Results tab's sources)
    HeuristicLab.Studio.Controls.SweepSelection.Attach(this, () => DataContext is TableGridViewModel { MultiSelect: true } grid ? grid.Rows : [],
      "row", () => DataContext is TableGridViewModel { MultiSelect: true });
    // the sideways position is the table's: a table made again (sorted) starts where the last one was (at the top)
    Scroll.ScrollChanged += (_, _) => {
      if (!restoring && DataContext is TableGridViewModel grid) grid.ScrollX = Scroll.Offset.X;
      PlaceFrame();
    };
    // sizes change (window, columns, rows shown): the frame follows
    LayoutUpdated += (_, _) => PlaceFrame();
  }

  /// <summary>
  /// The sorted column's frame, over the part of it in view: from its title (always in view) down to the view's lower
  /// edge, or to its last row once that is in view (then with its bottom line). One gradient over that height, so it
  /// costs the same for any number of rows. A column scrolled under the fixed ones is cut at their edge.
  /// </summary>
  private void PlaceFrame() {
    // the room for expanded fixed columns
    if (DataContext is TableGridViewModel table && Scroll.Viewport.Width > 0) table.ViewWidth = Scroll.Viewport.Width;
    if (DataContext is not TableGridViewModel { Framed: { } framed } grid) {
      FrameClip.IsVisible = false;
      return;
    }
    double x = 0, fixedRight = 0;
    int index = -1;
    for (int i = 0; i < grid.Views.Count; i++) {
      if (grid.Views[i] == framed) index = i;
      if (index < 0) x += grid.Views[i].Width;
      if (grid.Views[i].IsFixed) fixedRight = grid.Views.Take(i + 1).Sum(v => v.Width);
    }
    var view = Scroll.Viewport;
    double rowsBottom = HeaderRow.Bounds.Height + RowList.Bounds.Height - Scroll.Offset.Y;
    bool bottom = rowsBottom <= view.Height + 0.5;
    double clip = framed.IsFixed ? 0 : fixedRight;
    if (!framed.IsFixed) x -= Scroll.Offset.X;
    double width = framed.Width, height = bottom ? rowsBottom : view.Height, w = TableGridViewModel.FrameWidth;
    var thickness = new Avalonia.Thickness(w, w, w, bottom ? w : 0);
    void Set<T>(Avalonia.AvaloniaObject o, Avalonia.AvaloniaProperty<T> p, T value) {
      if (!Equals(o.GetValue(p), value)) o.SetValue(p, value);
    }
    Set(FrameClip, IsVisibleProperty, view.Width > 0);
    Set(FrameClip, MarginProperty, new Avalonia.Thickness(clip, 0, 0, 0));
    Set(FrameClip, WidthProperty, System.Math.Max(0, view.Width - clip));
    Set(FrameClip, HeightProperty, System.Math.Max(0, view.Height));
    Set(SortFrame, Canvas.LeftProperty, x - clip);
    Set(SortFrame, WidthProperty, width);
    Set(SortFrame, HeightProperty, System.Math.Max(0, height));
    Set(SortFrame, Border.BorderThicknessProperty, thickness);
    Set(SortFrame, Border.BorderBrushProperty, grid.FrameBrush);
  }

  private bool restoring;

  protected override void OnDataContextChanged(System.EventArgs e) {
    base.OnDataContextChanged(e);
    if (DataContext is not TableGridViewModel { ScrollX: > 0 } grid) return;
    restoring = true;
    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
      restoring = false;
      if (ReferenceEquals(DataContext, grid)) Scroll.Offset = new Avalonia.Vector(grid.ScrollX, 0);
    }, Avalonia.Threading.DispatcherPriority.Loaded);
  }

  // ---- several selected rows: a press anywhere else in the window (empty space, another tab, ...; not in a menu)
  // unselects them (handler on the window: a press on empty space reaches only the window)

  private TopLevel? top;

  protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) {
    base.OnAttachedToVisualTree(e);
    top = TopLevel.GetTopLevel(this);
    top?.AddHandler(PointerPressedEvent, ElsewherePressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
  }

  protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) {
    base.OnDetachedFromVisualTree(e);
    top?.RemoveHandler(PointerPressedEvent, ElsewherePressed);
    top = null;
  }

  private void ElsewherePressed(object? sender, PointerPressedEventArgs e) {
    if (DataContext is not TableGridViewModel { MultiSelect: true } grid) return;
    for (var v = e.Source as Avalonia.Visual; v != null; v = Avalonia.VisualTree.VisualExtensions.GetVisualParent(v))
      if (ReferenceEquals(v, this) || v is MenuItem or Avalonia.Controls.ContextMenu) return;
    grid.Mark(null);
  }

  private void RowTapped(object? sender, TappedEventArgs e) {
    if (sender is Control { DataContext: GridRow row } && DataContext is TableGridViewModel { MultiSelect: false } grid) grid.Select(row);
  }

  /// <summary>The last row menu opened (tests).</summary>
  public ContextMenu? Menu { get; private set; }

  /// <summary>Right-click on a row: a row not selected is selected alone, then the menu for the selected rows opens.</summary>
  private void RowMenu(object? sender, ContextRequestedEventArgs e) {
    if (sender is not Control { DataContext: GridRow row } control || DataContext is not TableGridViewModel { RowMenu: { } menu } grid) return;
    e.Handled = true;
    if (!row.IsSelected || !grid.MultiSelect) grid.Select(row);
    var choices = menu(grid.MultiSelect ? grid.SelectedItems : [row.Item]);
    if (choices.Count == 0) return;
    Menu = new ContextMenu {
      ItemsSource = choices.Select(c => new MenuItem { Header = c.Display, Command = c.Command, [ToolTip.TipProperty] = c.Tip }).ToList()
    };
    Menu.Open(control);
  }

  /// <summary>A double-click on a column's title sorts it (not one on its button or its right edge).</summary>
  private void HeaderDoubleTapped(object? sender, TappedEventArgs e) {
    for (var v = e.Source as Avalonia.Visual; v != null && !ReferenceEquals(v, sender); v = Avalonia.VisualTree.VisualExtensions.GetVisualParent(v))
      if (v is Button || v is Avalonia.StyledElement { Classes: var classes } && classes.Contains("columnedge")) return;
    if (sender is Control { DataContext: GridCell cell } && DataContext is TableGridViewModel { HeaderButtons: true }) cell.Column.CycleSortCommand.Execute(null);
  }

  /// <summary>A click on a column's right edge (any row): expanded, or regular again.</summary>
  private void EdgePressed(object? sender, PointerPressedEventArgs e) {
    if (sender is not Control { DataContext: GridCell cell } edge || !e.GetCurrentPoint(edge).Properties.IsLeftButtonPressed) return;
    e.Handled = true;
    if (e.ClickCount == 1) cell.Column.ToggleExpandCommand.Execute(null);
  }
}
