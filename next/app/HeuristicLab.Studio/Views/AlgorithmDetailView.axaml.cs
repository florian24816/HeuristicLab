using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

public partial class AlgorithmDetailView : UserControl {
  public AlgorithmDetailView() {
    InitializeComponent();
    // Shift+click on a value or choice (Algorithm tab, Operator Graph tab): edit it across algorithms;
    // seen before the field itself handles the press
    AddHandler(PointerPressedEvent, FieldPressed, RoutingStrategies.Tunnel);
  }

  private static void FieldPressed(object? sender, PointerPressedEventArgs e) {
    if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.Source is not Visual source) return;
    foreach (var visual in source.GetSelfAndVisualAncestors()) {
      if (visual is Control { DataContext: IMultiField field } control && control.Classes.Contains("field")) {
        if (field.ToggleMulti()) e.Handled = true;
        return;
      }
    }
  }
}
