using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace HeuristicLab.Studio.Views;

/// <summary>
/// A window-like panel floating over a fullscreen view: moved by its title bar, resized at its lower
/// right corner, kept on its canvas. Used by the fullscreen views (detailed analysis, Results charts).
/// </summary>
public sealed class FloatingPanel : Border {
  private readonly Canvas canvas;
  private readonly Size minimum;
  private Point? dragStart, resizeStart;
  private Point panelAtStart;
  private Size sizeAtStart;

  /// <summary>Whether the user moved or resized it (then it is not placed automatically any more).</summary>
  public bool Moved { get; private set; }

  private readonly Control content;
  private readonly Border grip;
  private readonly Button? minimizeButton;
  private double restoredHeight;

  /// <summary>Only the title bar is shown (minimizable panels).</summary>
  public bool IsMinimized { get; private set; }

  /// <param name="titleRight">Shown at the right end of the title bar (e.g. a button to leave fullscreen).</param>
  /// <param name="minimizable">A button in the title bar folds the panel to its title bar and back.</param>
  public FloatingPanel(Canvas canvas, string title, Control content, Size size, Size minimum, Control? titleRight = null, bool minimizable = false) {
    this.content = content;
    this.canvas = canvas;
    this.minimum = minimum;
    Width = size.Width;
    Height = size.Height;
    CornerRadius = new CornerRadius(6);
    BorderThickness = new Thickness(1);
    BorderBrush = new SolidColorBrush(Colors.Gray, 0.6);
    ClipToBounds = true;
    BoxShadow = BoxShadows.Parse("0 4 18 0 #60000000");
    this[!BackgroundProperty] = this.GetResourceObservable("SystemControlBackgroundAltHighBrush").ToBinding();

    var heading = new DockPanel();
    if (titleRight != null) {
      DockPanel.SetDock(titleRight, Dock.Right);
      heading.Children.Add(titleRight);
    }
    if (minimizable) {
      minimizeButton = new Button { Content = "—", Padding = new Thickness(10, 0), MinHeight = 24, VerticalAlignment = VerticalAlignment.Center,
                                    Margin = new Thickness(6, 0, 0, 0) };
      ToolTip.SetTip(minimizeButton, "Minimize");
      minimizeButton.Click += (_, _) => ToggleMinimized();
      DockPanel.SetDock(minimizeButton, Dock.Right);
      heading.Children.Add(minimizeButton);
    }
    heading.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
    var titleBar = new Border {
      Padding = new Thickness(10, 4), Cursor = new Cursor(StandardCursorType.SizeAll), MinHeight = 30,
      Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x6F, 0xD8), 0.18), Child = heading
    };
    titleBar.PointerPressed += (_, e) => {
      dragStart = e.GetPosition(canvas);
      panelAtStart = new Point(Canvas.GetLeft(this), Canvas.GetTop(this));
      e.Pointer.Capture(titleBar);
    };
    titleBar.PointerMoved += (_, e) => {
      if (dragStart is not { } start) return;
      Moved = true;
      var to = panelAtStart + (e.GetPosition(canvas) - start);
      MoveTo(to.X, to.Y);
    };
    titleBar.PointerReleased += (_, e) => { dragStart = null; e.Pointer.Capture(null); };

    grip = new Border {
      Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
      Cursor = new Cursor(StandardCursorType.BottomRightCorner), Background = new SolidColorBrush(Colors.Gray, 0.5), CornerRadius = new CornerRadius(0, 0, 6, 0)
    };
    grip.PointerPressed += (_, e) => { resizeStart = e.GetPosition(canvas); sizeAtStart = new Size(Width, Height); e.Pointer.Capture(grip); e.Handled = true; };
    grip.PointerMoved += (_, e) => {
      if (resizeStart is not { } start) return;
      Moved = true;
      var delta = e.GetPosition(canvas) - start;
      ResizeTo(sizeAtStart.Width + delta.X, sizeAtStart.Height + delta.Y);
    };
    grip.PointerReleased += (_, e) => { resizeStart = null; e.Pointer.Capture(null); };

    var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
    body.Children.Add(titleBar);
    Grid.SetRow(content, 1);
    body.Children.Add(content);
    Grid.SetRow(grip, 1);
    body.Children.Add(grip);
    Child = body;
    canvas.Children.Add(this);
  }

  /// <summary>Moves it, keeping its title bar on the canvas.</summary>
  public void MoveTo(double x, double y) {
    Canvas.SetLeft(this, Math.Clamp(x, 0, Math.Max(0, canvas.Bounds.Width - 80)));
    Canvas.SetTop(this, Math.Clamp(y, 0, Math.Max(0, canvas.Bounds.Height - 40)));
  }

  /// <summary>Folds the panel to its title bar, or opens it again at its former height.</summary>
  public void ToggleMinimized() {
    IsMinimized = !IsMinimized;
    content.IsVisible = grip.IsVisible = !IsMinimized;
    if (IsMinimized) {
      restoredHeight = Height;
      Height = double.NaN;
    } else Height = restoredHeight;
    if (minimizeButton != null) {
      minimizeButton.Content = IsMinimized ? "▢" : "—";
      ToolTip.SetTip(minimizeButton, IsMinimized ? "Restore" : "Minimize");
    }
  }

  public void ResizeTo(double width, double height) {
    if (IsMinimized) ToggleMinimized();
    Width = Math.Max(minimum.Width, width);
    Height = Math.Max(minimum.Height, height);
  }
}
