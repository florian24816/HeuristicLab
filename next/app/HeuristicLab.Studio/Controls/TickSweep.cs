using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HeuristicLab.Studio.Controls;

/// <summary>
/// Ticking a list of check boxes by sweeping: hold the right mouse button on a box - it switches - and move over the
/// others: each one passed gets the same state (all ticked or all unticked). A left click ticks one as usual.
/// </summary>
public static class TickSweep {
  public static void Attach(Control list) {
    bool? state = null;
    CheckBox? BoxAt(PointerEventArgs e) {
      for (var v = list.InputHitTest(e.GetPosition(list)) as Visual; v != null && v != list; v = v.GetVisualParent())
        if (v is CheckBox box) return box.IsEffectivelyEnabled ? box : null;
      return null;
    }
    list.AddHandler(InputElement.PointerPressedEvent, (_, e) => {
      if (!e.GetCurrentPoint(list).Properties.IsRightButtonPressed || BoxAt(e) is not { } box) return;
      state = box.IsChecked != true;
      box.IsChecked = state;
      e.Pointer.Capture(list);
      e.Handled = true;
    }, RoutingStrategies.Tunnel);
    list.AddHandler(InputElement.PointerMovedEvent, (_, e) => {
      if (state is not bool s) return;
      if (!e.GetCurrentPoint(list).Properties.IsRightButtonPressed) { state = null; return; }
      if (BoxAt(e) is { } box) box.IsChecked = s;
    }, RoutingStrategies.Tunnel);
    list.AddHandler(InputElement.PointerReleasedEvent, (_, e) => {
      if (state == null) return;
      state = null;
      e.Pointer.Capture(null);
      e.Handled = true;
    }, RoutingStrategies.Tunnel);
    list.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => state = null);
  }
}
