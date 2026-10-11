using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;
using HeuristicLab.Studio.Services;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>Moving building blocks (drag and drop with a faded preview) and copying them.</summary>
[TestClass]
public class StudioDragDropTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private sealed class NumberDialogs(int answer) : IDialogService {
    public Task<SetupResult?> NewSetupAsync() => Task.FromResult<SetupResult?>(null);
    public Task<CatalogEntry?> PickAlgorithmAsync() => Task.FromResult<CatalogEntry?>(null);
    public Task<IProblem?> PickProblemAsync(IAlgorithm algorithm) => Task.FromResult<IProblem?>(null);
    public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
    public Task<int?> AskNumberAsync(string title, string message, int initial, int min, int max) => Task.FromResult<int?>(answer);
  }

  // HeuristicLab's own TimeLimitRun (from older files): one algorithm only
  private static CatalogEntry LegacyTimeLimit => new("Time-limit run", "", typeof(TimeLimitRun).FullName!, typeof(TimeLimitRun));
  private static CatalogEntry TabuSearch => Catalog.Find(Catalog.Algorithms(), "TabuSearch")!;
  private static CatalogEntry Container<T>() => ExperimentTree.ContainerTypes().Single(e => e.Type == typeof(T));

  private static IProblem Tsp() {
    var scratch = Setups.Create(new SetupRequest("TabuSearch") { Problem = "TravelingSalesmanProblem", Instance = "berlin52" }).Algorithm;
    return (IProblem)scratch.Problem!.Clone();
  }

  private static AlgorithmBlockViewModel Algorithm(ExperimentWorkspaceViewModel ws, ContainerBlockViewModel container, string name) {
    var block = ws.AddAlgorithm(container, TabuSearch);
    ws.SetProblem(block, Tsp());
    block.Name = name;
    ParameterEditor.Apply(block.Algorithm, ["MaximumIterations=5"]);
    return block;
  }

  private static string[] Names(ContainerBlockViewModel c) => c.Children.Select(b => b.Name).ToArray();
  private static string[] TreeNames(IOptimizer o) => ExperimentTree.Children(o).Select(c => c.Name).ToArray();

  [TestMethod]
  public async Task MovingKeepsTreeAndExperimentInStep() {
    await session.Dispatch(async () => {
      var ws = new ExperimentWorkspaceViewModel();
      var experiment = ws.NewExperiment();
      var a = Algorithm(ws, experiment, "A");
      var batch = ws.AddContainer(experiment, Container<BatchRun>());
      var b1 = Algorithm(ws, batch, "B1");
      var b2 = Algorithm(ws, batch, "B2");
      var timeLimit = ws.AddContainer(experiment, LegacyTimeLimit);
      var t = Algorithm(ws, timeLimit, "T");

      // into the batch, between its two algorithms
      Assert.IsTrue(ws.Move(a, batch, 1));
      CollectionAssert.AreEqual(new[] { "B1", "A", "B2" }, Names(batch));
      CollectionAssert.AreEqual(new[] { "B1", "A", "B2" }, TreeNames(batch.Optimizer), "HeuristicLab's batch content in the same order");
      Assert.AreSame(batch, a.Parent);
      Assert.AreSame(batch, a.Problem!.Parent);
      CollectionAssert.AreEqual(new[] { batch.Name, timeLimit.Name }, TreeNames(experiment.Experiment));

      // within the same container: down by one (index counts positions before the move)
      Assert.IsTrue(ws.Move(b1, batch, 2));
      CollectionAssert.AreEqual(new[] { "A", "B1", "B2" }, Names(batch));
      Assert.IsFalse(ws.Move(b1, batch, 1), "the same place: no move");
      Assert.IsFalse(ws.Move(b1, batch, 2), "right after itself: the same place");

      // the time-limit run before the batch: everything inside goes with it
      Assert.IsTrue(ws.Move(timeLimit, experiment, 0));
      CollectionAssert.AreEqual(new[] { timeLimit.Name, batch.Name }, Names(experiment));
      Assert.AreSame(t, timeLimit.Children.Single());

      // not allowed: into itself, into a full time-limit run, a batch into a time-limit run
      var inner = ws.AddContainer(batch, Container<BatchRun>());
      Assert.IsFalse(ws.Move(batch, inner, 0), "a batch run cannot go into its own content");
      Assert.IsFalse(ws.Move(batch, batch, 0));
      Assert.IsFalse(ws.Move(b2, timeLimit, 0), "a time-limit run holds one algorithm");
      Assert.IsFalse(ws.Move(inner, timeLimit, 0), "only algorithms go into a time-limit run");

      // the algorithm out of the time-limit run, another one in
      Assert.IsTrue(ws.Move(t, inner, 0));
      Assert.AreEqual(0, timeLimit.Children.Count);
      Assert.IsNull(((TimeLimitRun)timeLimit.Optimizer).Algorithm);
      Assert.IsTrue(ws.Move(b2, timeLimit, 0));
      Assert.AreSame(b2.Algorithm, ((TimeLimitRun)timeLimit.Optimizer).Algorithm);
      ((TimeLimitRun)timeLimit.Optimizer).MaximumExecutionTime = TimeSpan.FromSeconds(1);

      // to another experiment, with its runs
      var other = ws.NewExperiment();
      await OptimizerRunner.RunAsync(a.Algorithm);
      Assert.AreEqual(1, a.Algorithm.Runs.Count);
      experiment.IsModified = false;
      Assert.IsTrue(ws.Move(a, other, 0));
      Assert.AreSame(other, a.Root);
      Assert.AreEqual(1, a.Algorithm.Runs.Count, "its runs go with it");
      Assert.AreEqual(1, other.Experiment.Runs.Count);
      Assert.IsFalse(experiment.IsSaved && other.IsSaved, "both experiments changed");
      Assert.IsTrue(experiment.IsModified);

      // the rearranged experiment runs
      Assert.AreEqual("", experiment.Missing);
      ws.Selected = null;
      await ws.StartAllCommand.ExecuteAsync(null);
      StringAssert.StartsWith(experiment.RunStatus, "Completed", experiment.RunStatus);
    }, CancellationToken.None);
  }

  [TestMethod]
  public async Task DragPreviewShowsWhereTheBlockLands() {
    await session.Dispatch(() => {
      var ws = new ExperimentWorkspaceViewModel();
      var experiment = ws.NewExperiment();
      var a = Algorithm(ws, experiment, "A");
      var batch = ws.AddContainer(experiment, Container<BatchRun>());
      var b1 = Algorithm(ws, batch, "B1");
      var timeLimit = ws.AddContainer(experiment, LegacyTimeLimit);

      Assert.IsFalse(ws.BeginDrag(experiment), "experiments are not dragged");
      Assert.IsTrue(ws.BeginDrag(a.Problem!), "dragging the problem moves its algorithm");
      Assert.AreSame(a, ws.Dragged);

      // lower half of the batch header: first inside the batch
      var target = ws.TargetAt(batch, upperHalf: false)!;
      Assert.AreEqual(new DropTarget(batch, 0), target);
      Assert.IsFalse(a.IsMoving, "no preview yet: the block stays visible");
      ws.PreviewDrop(target);
      Assert.AreSame(a, b1.PreviewBefore, "shown faded above B1");
      Assert.IsTrue(a.IsMoving, "with a preview the original is hidden");
      // lower half of B1: after it, at the batch's end
      ws.PreviewDrop(ws.TargetAt(b1, upperHalf: false));
      Assert.IsNull(b1.PreviewBefore);
      Assert.AreSame(a, batch.PreviewAtEnd);
      // the upper half of the batch header: before the batch, which is right after A - no move, no preview
      ws.PreviewDrop(ws.TargetAt(batch, upperHalf: true));
      Assert.IsNull(ws.DropPreview);
      Assert.IsNull(batch.PreviewAtEnd);
      Assert.IsFalse(a.IsMoving, "no preview: the block shows at its place again");
      // an empty time-limit run takes it
      ws.PreviewDrop(ws.TargetAt(timeLimit, upperHalf: false));
      Assert.AreEqual(new DropTarget(timeLimit, 0), ws.DropPreview);
      Assert.AreSame(a, timeLimit.PreviewAtEnd);

      // cancelled: nothing moves, the preview goes away
      Assert.IsFalse(ws.EndDrag(drop: false));
      Assert.IsNull(timeLimit.PreviewAtEnd);
      Assert.IsFalse(a.IsMoving);
      CollectionAssert.AreEqual(new[] { "A", batch.Name, timeLimit.Name }, Names(experiment));

      // dropped: it lands where the preview was
      ws.BeginDrag(a);
      ws.PreviewDrop(ws.TargetAt(b1, upperHalf: true));
      Assert.IsTrue(ws.EndDrag(drop: true));
      CollectionAssert.AreEqual(new[] { "A", "B1" }, Names(batch));
      Assert.IsNull(ws.Dragged);

      // a batch over its own content: no target there
      ws.BeginDrag(batch);
      ws.PreviewDrop(new DropTarget(batch, 1));
      Assert.IsNull(ws.DropPreview);
      ws.EndDrag(drop: false);
      return Task.CompletedTask;
    }, CancellationToken.None);
  }

  private static Border BlockOf(Window window, BlockViewModel block) =>
    window.GetVisualDescendants().OfType<Border>()
      .First(b => b.Classes.Contains("block") && b.DataContext == block && !b.GetVisualAncestors().OfType<Border>().Any(g => g.Classes.Contains("ghost")));

  private static Point At(Window window, Control control, double fx, double fy) =>
    control.TranslatePoint(new Point(control.Bounds.Width * fx, control.Bounds.Height * fy), window)!.Value;

  [TestMethod]
  public async Task DraggingWithTheMouse() {
    var shot = await session.Dispatch(() => {
      var vm = new MainViewModel(null);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1300, Height = 800 };
      window.Show();
      var experiment = ws.NewExperiment();
      var a = Algorithm(ws, experiment, "A");
      var batch = ws.AddContainer(experiment, Container<BatchRun>());
      var b1 = Algorithm(ws, batch, "B1");
      var b2 = Algorithm(ws, batch, "B2");
      ws.Selected = null;
      Avalonia.Threading.Dispatcher.UIThread.RunJobs();

      // drag A by its name onto the lower half of B1: it should land between B1 and B2
      var from = At(window, BlockOf(window, a), 0.6, 0.5);
      window.MouseDown(from, MouseButton.Left);
      window.MouseMove(from + new Point(10, 10), RawInputModifiers.LeftMouseButton);
      Avalonia.Threading.Dispatcher.UIThread.RunJobs();
      Assert.AreSame(a, ws.Dragged, "moving with the button down starts dragging");
      var over = At(window, BlockOf(window, b1), 0.5, 0.8);
      window.MouseMove(over, RawInputModifiers.LeftMouseButton);
      Avalonia.Threading.Dispatcher.UIThread.RunJobs();
      Assert.AreSame(a, b2.PreviewBefore, "the preview sits between B1 and B2");
      var ghost = window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ghost") && b.IsVisible);
      Assert.AreEqual(0.4, ghost.GetVisualDescendants().OfType<ContentControl>().First().Opacity, 1e-9, "faded");
      Assert.IsFalse(window.GetVisualDescendants().OfType<Border>().Any(b => b.Classes.Contains("block") && b.DataContext == a && b.IsEffectivelyVisible
        && !b.GetVisualAncestors().OfType<Border>().Any(g => g.Classes.Contains("ghost"))), "the original is hidden while the preview shows");
      var file = Path.Combine(AppContext.BaseDirectory, "studio-drag-preview.png");
      window.CaptureRenderedFrame()!.Save(file);
      window.MouseUp(over, MouseButton.Left);
      Avalonia.Threading.Dispatcher.UIThread.RunJobs();
      CollectionAssert.AreEqual(new[] { batch.Name }, Names(experiment));
      CollectionAssert.AreEqual(new[] { "B1", "A", "B2" }, Names(batch));
      Assert.IsNull(b2.PreviewBefore);
      Assert.IsTrue(BlockOf(window, a).IsEffectivelyVisible, "shown again after the drop");

      // drag the batch run by its header to after... nowhere else in this experiment; into a new one instead
      var second = ws.NewExperiment();
      ws.Selected = null;
      Avalonia.Threading.Dispatcher.UIThread.RunJobs();
      var batchAt = At(window, BlockOf(window, batch), 0.5, 0.5);
      window.MouseDown(batchAt, MouseButton.Left);
      window.MouseMove(batchAt + new Point(0, 12), RawInputModifiers.LeftMouseButton);
      var secondHeader = At(window, BlockOf(window, second), 0.5, 0.5);
      window.MouseMove(secondHeader, RawInputModifiers.LeftMouseButton);
      Avalonia.Threading.Dispatcher.UIThread.RunJobs();
      Assert.AreEqual(new DropTarget(second, 0), ws.DropPreview, "onto an experiment: first inside it");
      window.CaptureRenderedFrame()!.Save(Path.Combine(AppContext.BaseDirectory, "studio-drag-container.png"));
      window.MouseUp(secondHeader, MouseButton.Left);
      Avalonia.Threading.Dispatcher.UIThread.RunJobs();
      Assert.AreSame(batch, second.Children.Single(), "the batch run moved with its three algorithms");
      CollectionAssert.AreEqual(new[] { "B1", "A", "B2" }, Names(batch));
      Assert.AreEqual(0, experiment.Children.Count);

      // Escape cancels a drag
      var b1At = At(window, BlockOf(window, b1), 0.5, 0.5);
      window.MouseDown(b1At, MouseButton.Left);
      window.MouseMove(b1At + new Point(0, 40), RawInputModifiers.LeftMouseButton);
      Avalonia.Threading.Dispatcher.UIThread.RunJobs();
      Assert.IsNotNull(ws.Dragged);
      window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
      window.MouseUp(b1At + new Point(0, 40), MouseButton.Left);
      Avalonia.Threading.Dispatcher.UIThread.RunJobs();
      Assert.IsNull(ws.Dragged);
      CollectionAssert.AreEqual(new[] { "B1", "A", "B2" }, Names(batch));
      window.Close();
      return Task.FromResult(file);
    }, CancellationToken.None);
    Assert.IsTrue(new FileInfo(shot).Length > 5_000);
  }

  [TestMethod]
  public async Task CopiesAreIndependentAndUnrun() {
    await session.Dispatch(async () => {
      var ws = new ExperimentWorkspaceViewModel(new NumberDialogs(3));
      var experiment = ws.NewExperiment();
      var a = Algorithm(ws, experiment, "A");
      await OptimizerRunner.RunAsync(a.Algorithm);
      Assert.AreEqual(1, a.Algorithm.Runs.Count);

      Assert.IsTrue(a.Menu.Any(m => m.Display == "Copy") && a.Menu.Any(m => m.Display == "Copy n times..."));
      var copy = (AlgorithmBlockViewModel)ws.Copy(a, 1).Single();
      CollectionAssert.AreEqual(new[] { "A", "A (copy 1)" }, Names(experiment));
      Assert.AreNotSame(a.Algorithm, copy.Algorithm);
      Assert.AreNotSame(a.Algorithm.Problem, copy.Algorithm.Problem, "its own problem");
      Assert.AreEqual(0, copy.Algorithm.Runs.Count, "a copy starts unrun");
      Assert.AreEqual(1, a.Algorithm.Runs.Count, "the original keeps its runs");
      Assert.AreEqual(AlgorithmStatus.NotRun, copy.Status);
      ParameterEditor.Apply(copy.Algorithm, ["MaximumIterations=7"]);
      Assert.AreEqual("5", ParameterEditor.Describe(a.Algorithm).First(p => p.Name == "MaximumIterations").Value, "independent parameters");

      // copy n times (the number from the dialog), after the original, numbered on
      await a.CopyManyCommand.ExecuteAsync(null);
      CollectionAssert.AreEqual(new[] { "A", "A (copy 2)", "A (copy 3)", "A (copy 4)", "A (copy 1)" }, Names(experiment));
      CollectionAssert.AreEqual(Names(experiment), TreeNames(experiment.Experiment));

      // a batch run with its content
      var batch = ws.AddContainer(experiment, Container<BatchRun>());
      Algorithm(ws, batch, "B1");
      Algorithm(ws, batch, "B2");
      var batchCopy = (ContainerBlockViewModel)ws.Copy(batch, 1).Single();
      CollectionAssert.AreEqual(new[] { "B1", "B2" }, Names(batchCopy));
      Assert.AreNotSame(((AlgorithmBlockViewModel)batch.Children[0]).Algorithm, ((AlgorithmBlockViewModel)batchCopy.Children[0]).Algorithm);
      Assert.AreEqual(((BatchRun)batch.Optimizer).Repetitions, ((BatchRun)batchCopy.Optimizer).Repetitions);

      // a time-limit run is copied as a whole; its single algorithm cannot be copied next to itself
      var timeLimit = ws.AddContainer(experiment, LegacyTimeLimit);
      var t = Algorithm(ws, timeLimit, "T");
      Assert.IsFalse(ws.CanCopy(t));
      Assert.IsFalse(t.Menu.Any(m => m.Display == "Copy"));
      var timeCopies = ws.Copy(timeLimit, 2);
      Assert.AreEqual(2, timeCopies.Count);
      Assert.IsTrue(timeCopies.All(c => ((ContainerBlockViewModel)c).Children.Single().Name == "T"));
      Assert.IsFalse(ws.CanCopy(experiment), "experiments are not copied here");

      // the whole thing still runs
      foreach (TimeLimitRun run in ExperimentTree.Children(experiment.Experiment).OfType<TimeLimitRun>()) run.MaximumExecutionTime = TimeSpan.FromSeconds(1);
      foreach (BatchRun run in ExperimentTree.Children(experiment.Experiment).OfType<BatchRun>()) run.Repetitions = 1;
      await ws.StartAllCommand.ExecuteAsync(null);
      StringAssert.StartsWith(experiment.RunStatus, "Completed", experiment.RunStatus);
    }, CancellationToken.None);
  }
}
