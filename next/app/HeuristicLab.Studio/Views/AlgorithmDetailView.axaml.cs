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
    // Shift+Enter in that field: the value is taken (a choice's list has picked its item by then)
    // and the algorithm is named after it
    AddHandler(KeyDownEvent, FieldKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
  }

  private void FieldKeyDown(object? sender, KeyEventArgs e) {
    if (e.Key != Key.Enter || !e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.Source is not Visual source
        || DataContext is not AlgorithmDetailViewModel detail) return;
    foreach (var visual in source.GetSelfAndVisualAncestors()) {
      if (visual is Control { DataContext: IMultiField field } control && control.Classes.Contains("field")) {
        if (field.MultiKey is not { } key) return;
        if (control is TextBox box && field is ValueEditorViewModel value) value.Text = box.Text ?? "";
        if (control is ComboBox combo) combo.IsDropDownOpen = false;
        if (detail.NameAfterField(key)) e.Handled = true;
        return;
      }
    }
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
