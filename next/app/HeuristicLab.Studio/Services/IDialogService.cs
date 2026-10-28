using System.Linq;
using System.Threading.Tasks;
using HeuristicLab.Optimization;
using Avalonia.Controls;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;

namespace HeuristicLab.Studio.Services;

/// <summary>Modal dialogs behind an interface, so view models can be tested without windows.</summary>
public interface IDialogService {
  /// <summary>Shows the "New" dialog; returns the created setup or null if cancelled.</summary>
  Task<SetupResult?> NewSetupAsync();
  /// <summary>Chooses an algorithm type from the catalog; null if cancelled.</summary>
  Task<CatalogEntry?> PickAlgorithmAsync();
  /// <summary>Chooses and loads a problem (benchmark instance or data) for this algorithm; null if cancelled.</summary>
  Task<IProblem?> PickProblemAsync(IAlgorithm algorithm);
  Task<bool> ConfirmAsync(string title, string message);
  /// <summary>A message with only "OK": true when it is pressed, false when the dialog is closed instead.</summary>
  Task<bool> AcknowledgeAsync(string title, string message) => ConfirmAsync(title, message);
  /// <summary>Asks for a whole number between min and max; null if cancelled.</summary>
  Task<int?> AskNumberAsync(string title, string message, int initial, int min, int max) => Task.FromResult<int?>(null);
  /// <summary>Asks for a line of text; null if cancelled.</summary>
  Task<string?> AskTextAsync(string title, string message, string initial) => Task.FromResult<string?>(null);
  /// <summary>Picks one or several of the items and confirms them with the action button; their indices, null if cancelled.</summary>
  Task<System.Collections.Generic.IReadOnlyList<int>?> ChooseAsync(string title, string message, System.Collections.Generic.IReadOnlyList<string> items, string action) =>
    Task.FromResult<System.Collections.Generic.IReadOnlyList<int>?>(null);
  /// <summary>Which experiments to eject (indices) and whether to autosave; null when cancelled.</summary>
  Task<EjectChoice?> ChooseEjectAsync(System.Collections.Generic.IReadOnlyList<string> names, System.Collections.Generic.IReadOnlyList<bool> unsaved) =>
    Task.FromResult<EjectChoice?>(null);
  /// <summary>An unsaved experiment about to be ejected: save, eject without saving, or keep it.</summary>
  Task<UnsavedChoice> AskUnsavedAsync(string name) => Task.FromResult(UnsavedChoice.Keep);
}

/// <param name="Experiments">Indices of the experiments to eject.</param>
public sealed record EjectChoice(System.Collections.Generic.IReadOnlyList<int> Experiments, bool Autosave);

public enum UnsavedChoice { Save, Discard, Keep }

public sealed class DialogService(Window owner, IFileDialogService fileDialogs) : IDialogService {
  public async Task<SetupResult?> NewSetupAsync() {
    var viewModel = new NewSetupViewModel(fileDialogs);
    var dialog = new NewSetupWindow { DataContext = viewModel };
    return await dialog.ShowDialog<bool>(owner) ? viewModel.Result : null;
  }

  public async Task<CatalogEntry?> PickAlgorithmAsync() {
    var viewModel = new CatalogPickerViewModel("Add algorithm", Catalog.Algorithms());
    var dialog = new CatalogPickerWindow { DataContext = viewModel };
    return await dialog.ShowDialog<bool>(owner) ? viewModel.Selected : null;
  }

  public async Task<IProblem?> PickProblemAsync(IAlgorithm algorithm) {
    var entry = Catalog.Algorithms().FirstOrDefault(a => a.Type == algorithm.GetType());
    if (entry == null) return null;
    var viewModel = new NewSetupViewModel(fileDialogs, entry);
    var dialog = new NewSetupWindow { DataContext = viewModel };
    if (!await dialog.ShowDialog<bool>(owner) || viewModel.Result?.Algorithm is not IAlgorithm scratch) return null;
    // the problem was built on a scratch algorithm of the same type; a clone carries no event
    // registrations of it (legacy algorithms cannot be detached: Problem = null throws)
    return scratch.Problem?.Clone() as IProblem;
  }

  public async Task<bool> ConfirmAsync(string title, string message) =>
    await new ConfirmWindow(title, message).ShowDialog<bool>(owner);

  public async Task<bool> AcknowledgeAsync(string title, string message) =>
    await new ConfirmWindow(title, message, yes: "OK", cancel: false).ShowDialog<bool>(owner);

  public async Task<int?> AskNumberAsync(string title, string message, int initial, int min, int max) =>
    await new NumberWindow(title, message, initial, min, max).ShowDialog<int?>(owner);

  public async Task<string?> AskTextAsync(string title, string message, string initial) =>
    await new TextWindow(title, message, initial).ShowDialog<string?>(owner);

  public async Task<System.Collections.Generic.IReadOnlyList<int>?> ChooseAsync(string title, string message, System.Collections.Generic.IReadOnlyList<string> items, string action) =>
    await new ChooseWindow(title, message, items, action).ShowDialog<System.Collections.Generic.IReadOnlyList<int>?>(owner);

  public async Task<EjectChoice?> ChooseEjectAsync(System.Collections.Generic.IReadOnlyList<string> names, System.Collections.Generic.IReadOnlyList<bool> unsaved) =>
    await new EjectWindow(names, unsaved).ShowDialog<EjectChoice?>(owner);

  public async Task<UnsavedChoice> AskUnsavedAsync(string name) =>
    await new UnsavedWindow(name).ShowDialog<UnsavedChoice>(owner);
}
