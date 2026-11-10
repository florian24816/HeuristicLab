using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HeuristicLab.Studio.Controls;

/// <summary>
/// A press on a place that takes no focus (empty space, a block's area, a label) leaves the text field being typed in:
/// without it the field kept the focus (and its cursor) until another field or button was clicked. For every window.
/// </summary>
public static class FocusRelease {
  public static void Register() =>
    InputElement.PointerPressedEvent.AddClassHandler<TopLevel>(Pressed, RoutingStrategies.Tunnel, handledEventsToo: true);

  private static void Pressed(TopLevel top, PointerPressedEventArgs e) {
    if (top.FocusManager?.GetFocusedElement() is not TextBox) return;
    // anything that takes the focus itself (the field, another field, a button, a list) handles it
    for (var v = e.Source as Visual; v != null; v = v.GetVisualParent())
      if (v is InputElement { Focusable: true, IsEffectivelyEnabled: true } and not TopLevel) return;
    top.FocusManager.Focus(null);  // no element: the focus is cleared
  }
}
