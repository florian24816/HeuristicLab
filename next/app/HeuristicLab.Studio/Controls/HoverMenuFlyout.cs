using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace HeuristicLab.Studio.Controls;

/// <summary>
/// A menu flyout shown while the pointer is over its button or over the list itself ("Open recent", user's request
/// 2026-11-13): it opens when the button is hovered and closes once the pointer has been off both for a moment.
/// Closing it as soon as the pointer left the button flickered - the list opens in a window of its own, the button
/// lost the pointer for an instant, the list closed, the button got the pointer back and opened it again.
/// </summary>
public class HoverMenuFlyout : MenuFlyout {
  /// <summary>How long the pointer may be off the button and the list before the list closes.</summary>
  public static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(250);

  private Control? button;
  private bool overButton, overList;
  private DispatcherTimer? timer;

  /// <summary>Opens and closes this flyout by hovering the button it belongs to.</summary>
  public void Attach(Control target) {
    button = target;
    OverlayInputPassThroughElement = target;  // the tile keeps getting the pointer while the list is open
    target.PointerEntered += (_, _) => {
      overButton = true;
      if (!IsOpen && target.IsEffectivelyEnabled) ShowAt(target);
    };
    target.PointerExited += (_, _) => { overButton = false; CheckSoon(); };
  }

  protected override Control CreatePresenter() {
    var presenter = base.CreatePresenter();
    presenter.PointerEntered += (_, _) => overList = true;
    presenter.PointerExited += (_, _) => { overList = false; CheckSoon(); };
    return presenter;
  }

  protected override void OnClosed() {
    overList = false;
    timer?.Stop();
    base.OnClosed();
  }

  private void CheckSoon() {
    timer ??= new DispatcherTimer { Interval = Grace };
    timer.Stop();
    timer.Tick -= Check;
    timer.Tick += Check;
    timer.Start();
  }

  private void Check(object? sender, EventArgs e) {
    timer!.Stop();
    if (IsOpen && !overButton && !overList) Hide();
  }

  /// <summary>For tests: the grace period over now.</summary>
  public void CheckNow() => Check(null, EventArgs.Empty);
}
