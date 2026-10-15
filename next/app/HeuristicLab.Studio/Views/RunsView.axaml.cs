using System;
using Avalonia.Controls;
using Avalonia.Layout;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

public partial class RunsView : UserControl {
  public RunsView() => InitializeComponent();

  /// <summary>
  /// With its own details (Experiments tab) the table fills the height down to the window's edge and the
  /// selected run's details sit below it; without (Runs tab, which shows them itself) it stays at the top.
  /// </summary>
  protected override void OnDataContextChanged(EventArgs e) {
    base.OnDataContextChanged(e);
    bool fills = DataContext is RunsViewModel { ShowsDetails: true };
    Layout.RowDefinitions = RowDefinitions.Parse(fills ? "Auto,*,Auto" : "Auto,Auto,*");
    Table.MaxHeight = fills ? double.PositiveInfinity : 300;
    Table.VerticalAlignment = fills ? VerticalAlignment.Stretch : VerticalAlignment.Top;
    Details.MaxHeight = fills ? 240 : double.PositiveInfinity;
  }
}
