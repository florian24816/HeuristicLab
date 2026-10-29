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

  private void HeaderTapped(object? sender, TappedEventArgs e) {
    if (sender is Control { DataContext: GridCell cell } && DataContext is TableGridViewModel grid && grid.CanSort) {
      int index = System.Linq.Enumerable.ToList(grid.Header).IndexOf(cell);
      if (index >= 0) grid.SortCommand.Execute(grid.Columns[index].Key);
    }
  }
}
