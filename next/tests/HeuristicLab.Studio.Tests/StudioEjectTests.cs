using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using HeuristicLab.Core;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;
using HeuristicLab.Studio.Controls;
using HeuristicLab.Studio.Services;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>Ejecting experiments: all or chosen ones, unsaved ones saved (autosave) or asked about.</summary>
[TestClass]
public class StudioEjectTests {
  private static HeadlessUnitTestSession session = null!;
  private static string LegacyGaTsp => Path.Combine(AppContext.BaseDirectory, "TestData", "GA_TSP.hl");

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  private sealed class Dialogs : IDialogService {
    public EjectChoice? Choice { get; set; }
    public Dictionary<string, UnsavedChoice> Answers { get; } = [];
    public List<string> Asked { get; } = [];
    public IReadOnlyList<bool>? ShownUnsaved { get; private set; }
    public Task<SetupResult?> NewSetupAsync() => Task.FromResult<SetupResult?>(null);
    public Task<CatalogEntry?> PickAlgorithmAsync() => Task.FromResult<CatalogEntry?>(null);
    public Task<IProblem?> PickProblemAsync(IAlgorithm algorithm) => Task.FromResult<IProblem?>(null);
    public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
    public Task<EjectChoice?> ChooseEjectAsync(IReadOnlyList<string> names, IReadOnlyList<bool> unsaved) {
      ShownUnsaved = unsaved;
      return Task.FromResult(Choice);
    }
    public Task<UnsavedChoice> AskUnsavedAsync(string name) {
      Asked.Add(name);
      return Task.FromResult(Answers.GetValueOrDefault(name, UnsavedChoice.Keep));
    }
  }

  private sealed class Files(string saveTo) : IFileDialogService {
    public Task<string?> OpenHlFileAsync() => Task.FromResult<string?>(null);
    public Task<string?> SaveHlFileAsync(string suggestedName) => Task.FromResult<string?>(saveTo);
    public Task<string?> OpenDataFileAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    public Task<string?> SaveCsvFileAsync(string suggestedName) => Task.FromResult<string?>(null);
  }

  [TestMethod]
  [Timeout(300_000)]
  public async Task EjectAllOrChosenAndHandleUnsavedOnes() {
    var folder = Path.Combine(Path.GetTempPath(), $"studio-eject-{Guid.NewGuid():N}");
    Directory.CreateDirectory(folder);
    try {
      await session.Dispatch(async () => {
        string saved = Path.Combine(folder, "saved.hl"), changed = Path.Combine(folder, "changed.hl"), chosen = Path.Combine(folder, "new.hl");
        var dialogs = new Dialogs();
        var main = new MainViewModel(new Files(chosen), dialogs);
        var ws = main.Workspace;
        // experiment files (a single algorithm's file opens inside a new, not yet saved experiment)
        await ws.OpenExperimentAsync(LegacyGaTsp);
        Documents.Save(ws.Experiments[0].Experiment, saved);
        Documents.Save(ws.Experiments[0].Experiment, changed);
        ws.Experiments.Clear();
        ws.RefreshState();
        var window = new MainWindow { DataContext = main, Width = 1400, Height = 900 };
        window.Show();
        Assert.IsFalse(ws.EjectCommand.CanExecute(null), "nothing to eject");

        async Task<(ExperimentBlockViewModel Saved, ExperimentBlockViewModel Changed, ExperimentBlockViewModel New)> Load() {
          await ws.OpenExperimentAsync(saved);
          var a = ws.Experiments[^1];
          await ws.OpenExperimentAsync(changed);
          var b = ws.Experiments[^1];
          b.MarkModified();
          var c = ws.NewExperiment();
          return (a, b, c);
        }
        var (a, b, c) = await Load();
        Pump();

        // the button: a grey square with the eject pictogram, right of Stop; the status sits in the tab row
        var buttons = window.GetVisualDescendants().OfType<Button>().Where(x => x.Classes.Contains("picto")).ToList();
        var eject = buttons.Single(x => x.GetVisualDescendants().OfType<Pictogram>().Any(p => p.Symbol == "eject"));
        var stop = buttons.Single(x => x.GetVisualDescendants().OfType<Pictogram>().Any(p => p.Symbol == "experiment_stop"));
        Assert.IsTrue(eject.Bounds.Left > stop.Bounds.Right, "next to Stop");
        var square = eject.GetVisualDescendants().OfType<Border>().First(x => x.Classes.Contains("square"));
        Assert.AreEqual(Color.Parse("#A8A8A8"), ((ISolidColorBrush)square.Background!).Color, "grey, not orange");
        var status = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("runstatus"));
        var resources = window.GetVisualDescendants().OfType<TabItem>().Single(t => t.Header is "Resources");
        Assert.IsTrue(status.TranslatePoint(default, window)!.Value.X >= resources.TranslatePoint(new Point(resources.Bounds.Width, 0), window)!.Value.X,
          "between the Resources tab and the logo");
        Assert.IsTrue(status.TranslatePoint(default, window)!.Value.Y < 60, "in the tab row");
        window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-eject.png"));
        // the dialogs themselves
        var ejectWindow = new EjectWindow(ws.Experiments.Select(e => e.Name).ToList(), ws.Experiments.Select(e => !e.IsSaved).ToList());
        ejectWindow.Show();
        Pump();
        CollectionAssert.AreEqual(new[] { "GA_TSP", "GA_TSP   (unsaved)", "New experiment 1   (unsaved)" },
          ejectWindow.GetVisualDescendants().OfType<CheckBox>().Select(x => x.Content).OfType<TextBlock>().Select(t => t.Text).ToList(),
          "names as they are (an underscore is no access key)");
        Assert.IsFalse(ejectWindow.GetVisualDescendants().OfType<Button>().Single(x => x.Content is "Eject selected").IsEnabled, "nothing ticked yet");
        ejectWindow.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-eject-window.png"));
        ejectWindow.Close();
        var unsavedWindow = new UnsavedWindow(b.Name);
        unsavedWindow.Show();
        Pump();
        unsavedWindow.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-eject-unsaved.png"));
        unsavedWindow.Close();

        // not while anything runs
        ws.SetAlgorithmRunning(true);
        Assert.IsFalse(ws.EjectCommand.CanExecute(null));
        ws.SetAlgorithmRunning(false);
        Assert.IsTrue(ws.EjectCommand.CanExecute(null));

        // cancelled: nothing happens
        dialogs.Choice = null;
        await ws.EjectCommand.ExecuteAsync(null);
        Assert.AreEqual(3, ws.Experiments.Count);
        CollectionAssert.AreEqual(new[] { false, true, true }, dialogs.ShownUnsaved!.ToList(), "the dialog marks the unsaved ones");

        // all, without autosave: asked for each unsaved one - eject without saving, keep
        dialogs.Choice = new EjectChoice([0, 1, 2], Autosave: false);
        dialogs.Answers[b.Name] = UnsavedChoice.Discard;
        dialogs.Answers[c.Name] = UnsavedChoice.Keep;
        var before = File.GetLastWriteTimeUtc(changed);
        await ws.EjectCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { b.Name, c.Name }, dialogs.Asked, "only unsaved ones are asked about");
        CollectionAssert.AreEqual(new[] { c }, ws.Experiments.ToList(), "the one kept stays");
        Assert.AreEqual(before, File.GetLastWriteTimeUtc(changed), "ejected without saving");

        // chosen ones with autosave: saved to its file, or to a chosen one, then ejected
        ws.Experiments.Clear();
        (a, b, c) = await Load();
        dialogs.Asked.Clear();
        dialogs.Choice = new EjectChoice([1, 2], Autosave: true);
        await ws.EjectCommand.ExecuteAsync(null);
        Assert.AreEqual(0, dialogs.Asked.Count, "autosave asks nothing");
        CollectionAssert.AreEqual(new[] { a }, ws.Experiments.ToList(), "the one not chosen stays");
        Assert.IsTrue(File.GetLastWriteTimeUtc(changed) > before, "saved before ejecting");
        Assert.IsTrue(File.Exists(chosen), "a never saved one goes to the chosen file");
        Assert.IsNotNull(Documents.Load(chosen));
        window.Close();
        return true;
      }, CancellationToken.None);
    } finally {
      Directory.Delete(folder, recursive: true);
    }
  }
}
