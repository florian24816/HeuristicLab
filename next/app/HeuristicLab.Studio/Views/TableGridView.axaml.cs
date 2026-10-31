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
    };
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
