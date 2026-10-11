using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace HeuristicLab.Studio.Controls;

/// <summary>
/// A border that stays in view while its scroll viewer scrolls: sideways (a table's fixed columns)
/// and/or down (a table's header). It is moved back by the scroll offset; give it a higher ZIndex
/// than what scrolls under it and an opaque background.
/// </summary>
public class Sticky : Border {
  public static readonly StyledProperty<bool> StickXProperty = AvaloniaProperty.Register<Sticky, bool>(nameof(StickX));
  public static readonly StyledProperty<bool> StickYProperty = AvaloniaProperty.Register<Sticky, bool>(nameof(StickY));

  private ScrollViewer? scroll;

  public bool StickX { get => GetValue(StickXProperty); set => SetValue(StickXProperty, value); }
  public bool StickY { get => GetValue(StickYProperty); set => SetValue(StickYProperty, value); }

  protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
    base.OnAttachedToVisualTree(e);
    scroll = this.FindAncestorOfType<ScrollViewer>();
    if (scroll != null) scroll.ScrollChanged += OnScrollChanged;
    Update();
  }

  protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
    if (scroll != null) scroll.ScrollChanged -= OnScrollChanged;
    scroll = null;
    base.OnDetachedFromVisualTree(e);
  }

  protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
    base.OnPropertyChanged(change);
    if (change.Property == StickXProperty || change.Property == StickYProperty) Update();
  }

  private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => Update();

  private void Update() {
    var offset = scroll?.Offset ?? default;
    double x = StickX ? offset.X : 0, y = StickY ? offset.Y : 0;
    RenderTransform = x == 0 && y == 0 ? null : new TranslateTransform(x, y);
  }
}
