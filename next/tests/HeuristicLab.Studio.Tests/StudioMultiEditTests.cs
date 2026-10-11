using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;
using HeuristicLab.Studio.ViewModels;
using HeuristicLab.Studio.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HeuristicLab.Studio.Tests;

/// <summary>Editing one field across many algorithms: Shift+click on a value or choice, then block by block.</summary>
[TestClass]
public class StudioMultiEditTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static CatalogEntry Algorithm(string name) => Catalog.Find(Catalog.Algorithms(), name)!;

  private static IProblem Tsp() {
    var scratch = Setups.Create(new SetupRequest("TabuSearch") { Problem = "TravelingSalesmanProblem", Instance = "berlin52" }).Algorithm;
    return (IProblem)scratch.Problem!.Clone();
  }

  private static string Value(IAlgorithm algorithm, string parameter) =>
    ParameterEditor.Describe(algorithm).First(p => p.Name == parameter).Value;

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  private static T Visible<T>(Window window, Func<T, bool> where) where T : Control =>
    window.GetVisualDescendants().OfType<T>().First(c => c.IsEffectivelyVisible && where(c));

  private static Point Center(Window window, Control control) =>
    control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

  private static Border BlockOf(Window window, BlockViewModel block) =>
    Visible<Border>(window, b => b.Classes.Contains("block") && b.DataContext == block);

  private static void Click(Window window, Control control, RawInputModifiers modifiers = RawInputModifiers.None) {
    var at = Center(window, control);
    window.MouseDown(at, MouseButton.Left, modifiers);
    window.MouseUp(at, MouseButton.Left, modifiers);
    Pump();
  }

  private sealed record Setup(MainViewModel Main, ExperimentWorkspaceViewModel Workspace, Window Window,
    AlgorithmBlockViewModel First, IReadOnlyList<AlgorithmBlockViewModel> LocalSearches, AlgorithmBlockViewModel Tabu, AlgorithmBlockViewModel Elsewhere);

  /// <summary>The user's example: a local search with a problem, copied 10 times; plus a tabu search and another experiment's local search.</summary>
  private static Setup Build() {
    var main = new MainViewModel(null);
    var ws = main.Workspace;
    var window = new MainWindow { DataContext = main, Width = 1400, Height = 950 };
    window.Show();
    var experiment = ws.NewExperiment();
    var first = ws.AddAlgorithm(experiment, Algorithm("LocalSearch"));
    ws.SetProblem(first, Tsp());
    var copies = ws.Copy(first, 10).Cast<AlgorithmBlockViewModel>().ToList();
    var tabu = ws.AddAlgorithm(experiment, Algorithm("TabuSearch"));
    ws.SetProblem(tabu, Tsp());
    var other = ws.NewExperiment();
    var elsewhere = ws.AddAlgorithm(other, Algorithm("LocalSearch"));
    ws.SetProblem(elsewhere, Tsp());
    ws.Selected = first;
    Pump();
    return new Setup(main, ws, window, first, [first, .. copies], tabu, elsewhere);
  }

  [TestMethod]
  public async Task ValueAcrossAlgorithmsWithTheKeyboard() {
    var shot = await session.Dispatch(() => {
      var (_, ws, window, first, all, tabu, elsewhere) = Build();
      var detail = first.Detail;
      Assert.AreEqual(AlgorithmDetailViewModel.AlgorithmTab, detail.SelectedTab);
      detail.AlgorithmParameters.Selected = detail.AlgorithmParameters.Parameters.First(p => p.Name == "MaximumIterations");
      Pump();

      // Shift+click on the value field
      var field = Visible<TextBox>(window, t => t.Classes.Contains("field") && t.DataContext is ValueEditorViewModel { Path: "a/p:MaximumIterations" });
      Click(window, field, RawInputModifiers.Shift);
      Assert.AreEqual("a/p:MaximumIterations#value", ws.MultiEditing?.Key);
      Assert.AreEqual(2, first.MultiLevel, "the shown algorithm: stronger purple");
      Assert.IsTrue(all.Skip(1).All(b => b.MultiLevel == 1), "every other local search of the experiment: purple");
      Assert.AreEqual(0, tabu.MultiLevel, "another algorithm type is not involved");
      Assert.AreEqual(0, elsewhere.MultiLevel, "another experiment is not involved");
      Assert.AreEqual(0, first.Problem!.MultiLevel, "problem blocks are not involved");
      field = Visible<TextBox>(window, t => t.Classes.Contains("field") && t.DataContext is ValueEditorViewModel { Path: "a/p:MaximumIterations" });
      Assert.IsTrue(field.Classes.Contains("multi"), "the field is purple");
      Assert.AreSame(field, TopLevel.GetTopLevel(window)!.FocusManager!.GetFocusedElement(), "the keyboard is in the field");
      var overlays = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("multi") && b.IsEffectivelyVisible).ToList();
      Assert.AreEqual(all.Count, overlays.Count, "a purple overlay on each local search block");

      // type, then click the next purple block: the keyboard is in its field right away
      int value = 100;
      foreach (var block in all.Skip(1).Take(3).Append(all[0])) {
        window.KeyTextInput(value.ToString());
        Click(window, BlockOf(window, block));
        Assert.AreSame(block.Detail, ws.Detail);
        Assert.AreEqual(2, block.MultiLevel);
        var next = (TextBox)TopLevel.GetTopLevel(window)!.FocusManager!.GetFocusedElement()!;
        Assert.IsTrue(next.Classes.Contains("multi"));
        Assert.AreEqual("a/p:MaximumIterations", ((ValueEditorViewModel)next.DataContext!).Path);
        Assert.AreSame(((HeuristicLab.Core.IValueParameter)block.Algorithm.Parameters["MaximumIterations"]).Value, ((ValueEditorViewModel)next.DataContext!).Value,
          "the focused field belongs to the clicked algorithm");
        value += 100;
      }
      CollectionAssert.AreEqual(new[] { "100", "200", "300", "400" }, all.Take(4).Select(b => Value(b.Algorithm, "MaximumIterations")).ToList(),
        "each value went to the algorithm it was typed for");
      Assert.AreEqual("1000", Value(all[4].Algorithm, "MaximumIterations"), "untouched copies keep theirs");
      Assert.AreEqual("1000", Value(elsewhere.Algorithm, "MaximumIterations"));
      var file = Path.Combine(AppContext.BaseDirectory, "studio-multi-edit.png");
      window.CaptureRenderedFrame()!.Save(file);

      // a normal click on a non-purple block: just shows it
      Click(window, BlockOf(window, tabu));
      Assert.AreSame(tabu.Detail, ws.Detail);
      Assert.IsNotNull(ws.MultiEditing, "still editing across algorithms");

      // Shift+click on a purple block ends it
      Click(window, BlockOf(window, all[5]), RawInputModifiers.Shift);
      Assert.IsNull(ws.MultiEditing);
      Assert.IsTrue(all.All(b => b.MultiLevel == 0));
      Assert.IsFalse(window.GetVisualDescendants().OfType<Border>().Any(b => b.Classes.Contains("multi") && b.IsEffectivelyVisible));
      window.Close();
      return Task.FromResult(file);
    }, CancellationToken.None);
    Assert.IsTrue(new FileInfo(shot).Length > 5_000);
  }

  [TestMethod]
  public async Task ChoiceAcrossAlgorithmsWithArrowsAndEnter() {
    await session.Dispatch(() => {
      var (_, ws, window, first, all, _, _) = Build();
      var detail = first.Detail;
      // a choice with several options (for the TSP, MoveEvaluator offers one; MoveGenerator several)
      var node = detail.AlgorithmParameters.Parameters.First(p => p.Choices.Count > 1);
      string name = node.Name;
      detail.AlgorithmParameters.Selected = node;
      Pump();
      var before = all.Select(b => Value(b.Algorithm, name)).ToList();

      var choice = Visible<ComboBox>(window, c => c.Classes.Contains("field") && c.DataContext == node);
      Click(window, choice, RawInputModifiers.Shift);
      Assert.AreEqual($"a/p:{name}#choice", ws.MultiEditing?.Key);
      choice = Visible<ComboBox>(window, c => c.Classes.Contains("field") && c.Classes.Contains("multi"));
      Assert.IsTrue(choice.IsDropDownOpen, "the list opens to choose with the arrow keys");
      int index = choice.SelectedIndex;
      Assert.IsTrue(choice.ItemCount > 1, "there is something to choose");

      // keep the first one's choice (Escape closes the list), then arrow down + Enter in the next algorithm
      window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
      Pump();
      Assert.IsFalse(choice.IsDropDownOpen);
      Click(window, BlockOf(window, all[1]));
      choice = Visible<ComboBox>(window, c => c.Classes.Contains("field") && c.Classes.Contains("multi"));
      Assert.IsTrue(choice.IsDropDownOpen);
      window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
      window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
      Pump();
      Assert.AreNotEqual(before[1], Value(all[1].Algorithm, name), "chosen with the keyboard");
      Assert.AreEqual(before[0], Value(all[0].Algorithm, name), "the first one unchanged");

      // in the one after: a mouse click on an entry works as well
      Click(window, BlockOf(window, all[2]));
      choice = Visible<ComboBox>(window, c => c.Classes.Contains("field") && c.Classes.Contains("multi"));
      var item = choice.GetVisualDescendants().OfType<ComboBoxItem>().Concat(
        TopLevel.GetTopLevel(window)!.GetVisualDescendants().OfType<ComboBoxItem>()).FirstOrDefault(i => i.IsEffectivelyVisible && !i.IsSelected);
      if (item != null) {
        item.IsSelected = true;  // what a click on an entry does
        Pump();
        Assert.AreNotEqual(before[2], Value(all[2].Algorithm, name));
      }

      // Shift+click on the purple field ends it
      choice = Visible<ComboBox>(window, c => c.Classes.Contains("field") && c.Classes.Contains("multi"));
      choice.IsDropDownOpen = false;
      Pump();
      Click(window, choice, RawInputModifiers.Shift);
      Assert.IsNull(ws.MultiEditing);
      Assert.IsFalse(window.GetVisualDescendants().OfType<ComboBox>().Any(c => c.Classes.Contains("multi")));
      window.Close();
      return Task.CompletedTask;
    }, CancellationToken.None);
  }

  [TestMethod]
  public async Task OperatorGraphFieldsAndNestedOptions() {
    await session.Dispatch(() => {
      var (_, ws, window, first, all, _, _) = Build();
      var detail = first.Detail;

      // an option of an analyzer (nested in a checked list)
      var analyzer = detail.AlgorithmParameters.Parameters.First(p => p.Name == "Analyzer");
      detail.AlgorithmParameters.Selected = analyzer;
      var entry = analyzer.Editor.Entries.First(e => e.Parameters?.Parameters.Any(p => p.Editor.ValueKey != null) == true);
      analyzer.Editor.SelectedEntry = entry;
      var option = entry.Parameters!.Parameters.First(p => p.Editor.ValueKey != null);
      Assert.IsTrue(option.Editor.ToggleMulti());
      Assert.AreEqual($"a/p:Analyzer/e:{entry.Name}/p:{option.Name}#value", ws.MultiEditing!.Key);
      Assert.AreEqual(all.Count, all.Count(b => b.IsMultiMember));
      ws.Selected = all[3];
      Pump();
      var shown = all[3].Detail.AlgorithmParameters.Selected!.Editor;
      Assert.AreEqual(entry.Name, shown.SelectedEntry?.Name, "the same analyzer is selected in the next algorithm");
      Assert.IsTrue(shown.SelectedEntry!.Parameters!.Selected!.Editor.IsMultiTarget, "and its option is the purple field");
      ws.ExitMultiEdit();

      // a field in the operator graph
      ws.Selected = first;
      Pump();
      first.Detail.SelectedTab = AlgorithmDetailViewModel.GraphTab;
      var graph = first.Detail.Graph!;
      ParameterNodeViewModel? target = null;
      foreach (var node in graph.Nodes) {
        graph.Selected = node;
        target = graph.SelectedEditor?.Nested?.Parameters.FirstOrDefault(p => p.Editor.ValueKey != null || p.ChoiceKey != null && p.IsChoice);
        if (target != null) break;
      }
      Assert.IsNotNull(target, "an operator with an editable parameter");
      bool isChoice = target.Editor.ValueKey == null;
      Assert.IsTrue(isChoice ? target.ToggleMulti() : target.Editor.ToggleMulti());
      StringAssert.StartsWith(ws.MultiEditing!.Key, "g/n:");
      Assert.AreEqual(all.Count, all.Count(b => b.IsMultiMember), "every copy has the same graph");
      Click(window, BlockOf(window, all[6]));
      var other = all[6].Detail;
      Assert.AreEqual(AlgorithmDetailViewModel.GraphTab, other.SelectedTab, "the next algorithm opens on its graph");
      Assert.AreEqual(graph.Selected!.Name, other.Graph!.Selected?.Name, "with the same operator selected");
      var otherTarget = other.Graph.SelectedEditor!.Nested!.Selected!;
      Assert.AreEqual(target.Name, otherTarget.Name);
      Assert.IsTrue(isChoice ? otherTarget.IsMultiTarget : otherTarget.Editor.IsMultiTarget);
      var focused = TopLevel.GetTopLevel(window)!.FocusManager!.GetFocusedElement() as Control;
      Assert.IsTrue(focused?.Classes.Contains("multi") == true || focused?.GetVisualAncestors().OfType<Control>().Any(c => c.Classes.Contains("multi")) == true,
        "the keyboard is in the purple field of the graph");
      window.Close();
      return Task.CompletedTask;
    }, CancellationToken.None);
  }

  [TestMethod]
  public void FieldKeysResolveOnlyWhereTheFieldExists() {
    var ls = (IAlgorithm)Algorithm("LocalSearch").CreateInstance();
    ls.Problem = Tsp();
    Assert.IsTrue(MultiEdit.Resolves(ls, "a/p:MaximumIterations#value"));
    Assert.IsTrue(MultiEdit.Resolves(ls, "a/p:MoveEvaluator#choice"));
    Assert.IsFalse(MultiEdit.Resolves(ls, "a/p:MaximumIterations#choice"), "a number is not a choice");
    Assert.IsFalse(MultiEdit.Resolves(ls, "a/p:NoSuchParameter#value"));
    Assert.IsFalse(MultiEdit.Resolves(ls, "a/p:Analyzer/e:NoSuchAnalyzer/p:UpdateInterval#value"));
  }
}
