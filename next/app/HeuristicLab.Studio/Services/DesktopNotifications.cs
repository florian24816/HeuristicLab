using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace HeuristicLab.Studio.Services;

/// <summary>Sends a message to the system's notification center.</summary>
public interface IDesktopNotifications {
  /// <summary>Sends it in the background; returns why it failed (null: sent).</summary>
  Task<string?> SendAsync(string title, string message);
}

/// <summary>
/// The system's own notification center, through its standard tools (no extra packages):
/// Linux (GNOME, KDE, ...): the freedesktop notification service via notify-send, else gdbus;
/// Windows 10/11: a toast through Windows PowerShell's WinRT access; macOS: osascript.
/// </summary>
public sealed class DesktopNotifications : IDesktopNotifications {
  private const string AppName = "HeuristicLab Studio";
  // Windows only shows toasts of registered apps; Windows PowerShell is one
  private const string PowerShellAppId = @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe";

  private static readonly string? Icon = File.Exists(Path.Combine(AppContext.BaseDirectory, "HeuristicLab.ico"))
    ? Path.Combine(AppContext.BaseDirectory, "HeuristicLab.ico") : null;

  public Task<string?> SendAsync(string title, string message) => Task.Run(() => {
    try {
      if (OperatingSystem.IsWindows()) return Run(WindowsToast(title, message));
      if (OperatingSystem.IsMacOS()) return Run(MacNotification(title, message));
      if (Run(NotifySend(title, message)) is not string notifySend) return null;
      return Run(Gdbus(title, message)) is string gdbus ? $"{notifySend}; {gdbus}" : null;
    } catch (Exception e) {
      return e.Message;
    }
  });

  private static ProcessStartInfo NotifySend(string title, string message) {
    var start = new ProcessStartInfo("notify-send") { ArgumentList = { "--app-name", AppName } };
    if (Icon != null) { start.ArgumentList.Add("--icon"); start.ArgumentList.Add(Icon); }
    start.ArgumentList.Add(title);
    start.ArgumentList.Add(message);
    return start;
  }

  private static ProcessStartInfo Gdbus(string title, string message) => new("gdbus") {
    ArgumentList = {
      "call", "--session", "--dest", "org.freedesktop.Notifications", "--object-path", "/org/freedesktop/Notifications",
      "--method", "org.freedesktop.Notifications.Notify", AppName, "0", Icon ?? "", title, message, "[]", "{}", "-1"
    }
  };

  // title and message travel in environment variables: nothing to quote or escape
  private static ProcessStartInfo WindowsToast(string title, string message) {
    const string script = """
      [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null
      $xml = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
      $texts = $xml.GetElementsByTagName('text')
      $texts.Item(0).AppendChild($xml.CreateTextNode($env:HL_NOTIFY_TITLE)) > $null
      $texts.Item(1).AppendChild($xml.CreateTextNode($env:HL_NOTIFY_MESSAGE)) > $null
      $toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
      [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($env:HL_NOTIFY_APP).Show($toast)
      """;
    var start = new ProcessStartInfo("powershell.exe") {
      ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-Command", script }
    };
    start.Environment["HL_NOTIFY_TITLE"] = title;
    start.Environment["HL_NOTIFY_MESSAGE"] = message;
    start.Environment["HL_NOTIFY_APP"] = PowerShellAppId;
    return start;
  }

  private static ProcessStartInfo MacNotification(string title, string message) {
    var start = new ProcessStartInfo("osascript") {
      ArgumentList = { "-e", "display notification (system attribute \"HL_NOTIFY_MESSAGE\") with title (system attribute \"HL_NOTIFY_TITLE\")" }
    };
    start.Environment["HL_NOTIFY_TITLE"] = title;
    start.Environment["HL_NOTIFY_MESSAGE"] = message;
    return start;
  }

  /// <summary>Runs the tool; null if it worked, else why not.</summary>
  private static string? Run(ProcessStartInfo start) {
    start.UseShellExecute = false;
    start.CreateNoWindow = true;
    start.RedirectStandardOutput = true;
    start.RedirectStandardError = true;
    try {
      using var process = Process.Start(start);
      if (process == null) return $"{start.FileName} did not start";
      var error = process.StandardError.ReadToEndAsync();
      process.StandardOutput.ReadToEnd();
      if (!process.WaitForExit(15_000)) { process.Kill(); return $"{start.FileName} did not finish"; }
      return process.ExitCode == 0 ? null : $"{start.FileName}: {error.Result.Trim()}";
    } catch (System.ComponentModel.Win32Exception e) {
      return $"{start.FileName}: {e.Message}";
    }
  }
}
