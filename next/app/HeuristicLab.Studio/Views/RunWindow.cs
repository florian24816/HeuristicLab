using Avalonia;
using Avalonia.Controls;
using HeuristicLab.Studio.ViewModels;

namespace HeuristicLab.Studio.Views;

/// <summary>
/// A run of the Results table in a window of its own (what the Results tab's "Run" view used to show): not owned,
/// so it can go to another screen; closed with the main window, its results tab or "Close all run view windows".
/// </summary>
public sealed class RunWindow : Window {
  public RunWindow(RunWindowViewModel run) {
    DataContext = run;
    Title = run.Title;
    Width = 1200;
    Height = 760;
    Content = new RunWindowView { DataContext = run };
    this[!BackgroundProperty] = this.GetResourceObservable("WindowBrush").ToBinding();
    run.CloseRequested += Close;
    run.ActivateRequested += Activate;
    Closed += (_, _) => {
      run.CloseRequested -= Close;
      run.ActivateRequested -= Activate;
    };
  }
}
