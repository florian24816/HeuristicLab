using Avalonia.Controls;
using Avalonia.Input;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

public partial class ChartLegendView : UserControl {
  public ChartLegendView() => InitializeComponent();

  private void EntryTapped(object? sender, TappedEventArgs e) {
    if (sender is Control { DataContext: LegendItem item } && DataContext is VisualsViewModel visuals) visuals.Toggle(item.Name);
  }
}
