using System;
using System.Runtime.InteropServices;

namespace HeuristicLab.Studio.Services;

/// <summary>
/// Whether the left mouse button is held right now, anywhere on the screen. A window moved by its title bar is moved
/// by the window manager, which does not tell the application when the button is released: a results window moved
/// onto the second tab bar docks back only once it is let go there. Linux (X11, also XWayland) asks the X server,
/// Windows the system; elsewhere (or when that fails) it is unknown (null).
/// </summary>
public static class GlobalMouse {
  private static IntPtr display;
  private static bool unavailable;

  public static bool? IsLeftButtonDown() {
    if (unavailable) return null;
    try {
      if (OperatingSystem.IsWindows()) return (GetAsyncKeyState(0x01) & 0x8000) != 0;
      if (OperatingSystem.IsLinux()) {
        if (display == IntPtr.Zero) display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero) { unavailable = true; return null; }
        if (!XQueryPointer(display, XDefaultRootWindow(display), out _, out _, out _, out _, out _, out _, out uint mask)) return null;
        return (mask & Button1Mask) != 0;
      }
    } catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) {
      unavailable = true;
    }
    return null;
  }

  private const uint Button1Mask = 1 << 8;

  [DllImport("user32.dll")]
  private static extern short GetAsyncKeyState(int key);

  [DllImport("libX11.so.6")]
  private static extern IntPtr XOpenDisplay(IntPtr name);

  [DllImport("libX11.so.6")]
  private static extern IntPtr XDefaultRootWindow(IntPtr display);

  [DllImport("libX11.so.6")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child,
    out int rootX, out int rootY, out int winX, out int winY, out uint mask);
}
