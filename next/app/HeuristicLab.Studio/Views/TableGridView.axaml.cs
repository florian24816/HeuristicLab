using Avalonia.Controls;
using Avalonia.Input;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

public partial class TableGridView : UserControl {
  public TableGridView() => InitializeComponent();

  private void RowTapped(object? sender, TappedEventArgs e) {
    if (sender is Control { DataContext: GridRow row } && DataContext is TableGridViewModel grid) grid.Select(row);
  }

  private void HeaderTapped(object? sender, TappedEventArgs e) {
    if (sender is Control { DataContext: GridCell cell } && DataContext is TableGridViewModel grid && grid.CanSort) {
      int index = System.Linq.Enumerable.ToList(grid.Header).IndexOf(cell);
      if (index >= 0) grid.SortCommand.Execute(grid.Columns[index].Key);
    }
  }
}
