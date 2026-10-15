using Avalonia.Controls;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

public partial class ParameterNodeView : UserControl {
  public ParameterNodeView() => InitializeComponent();

  /// <summary>A choice that probably does not work gets the yellow "warned" look (App.axaml).</summary>
  private void OnChoicePrepared(object? sender, ContainerPreparedEventArgs e) =>
    e.Container.Classes.Set("warned", e.Container.DataContext is ChoiceItem { IsWarned: true });
}
