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

/// <summary>The Results tab's filters, used like a person does: choose a column, type a value key by key, in every view.</summary>
[TestClass]
public class StudioFilterTests {
  private static HeadlessUnitTestSession session = null!;

  [ClassInitialize]
  public static void Start(TestContext _) => session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

  private static void Pump() {
    for (int i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
  }

  [TestMethod]
  [Timeout(600_000)]
  public async Task FiltersTypedKeyByKeyInEveryView() {
    await session.Dispatch(async () => {
      var vm = new MainViewModel(null, null);
      var ws = vm.Workspace;
      var window = new MainWindow { DataContext = vm, Width = 1500, Height = 960 };
      window.Show();
      var experiment = ws.NewExperiment();
      var batchRun = ExperimentTree.ContainerTypes().Single(e => e.Type == typeof(BatchRun));
      var batch = ws.AddContainer(experiment, batchRun);
      batch.RepetitionsText = "4";
      var ga = ws.AddAlgorithm(batch, Catalog.Find(Catalog.Algorithms(), "GeneticAlgorithm")!);
      var problem = (IProblem)Setups.Create(new SetupRequest("GeneticAlgorithm") { Problem = "TravelingSalesmanProblem", Instance = "ch130" }).Algorithm.Problem!;
      ws.SetProblem(ga, (IProblem)problem.Clone());
      ParameterEditor.Apply(ga.Algorithm, ["PopulationSize=20", "MaximumGenerations=5"]);
      var ts = ws.AddAlgorithm(experiment, Catalog.Find(Catalog.Algorithms(), "TabuSearch")!);
      ws.SetProblem(ts, (IProblem)problem.Clone());
      ParameterEditor.Apply(ts.Algorithm, ["MaximumIterations=5"]);
      await ws.StartAllCommand.ExecuteAsync(null);

      vm.SelectedTab = MainViewModel.ResultsTab;
      var analysis = vm.RunAnalysis;
      analysis.RefreshSources();
      analysis.Sources.Single().IsChecked = true;
      Pump();
      int all = analysis.Table.Rows.Count;
      Assert.AreEqual(5, all);

      var log = new List<string>();
      void Step(string what, Action action) {
        try {
          action();
          Pump();
        } catch (Exception e) {
          log.Add($"{what}: {e.GetType().Name}: {e.Message}\n{e.StackTrace?.Split('\n').Take(8).Aggregate((a, b) => a + "\n" + b)}");
        }
      }

      foreach (var view in new[] { RunAnalysisViewModel.TableView, RunAnalysisViewModel.ChartsView, RunAnalysisViewModel.StatisticsView, RunAnalysisViewModel.RunView }) {
        foreach (var kind in view == RunAnalysisViewModel.ChartsView ? analysis.ChartKinds : ["-"]) {
          Step($"view {view} {kind}", () => {
            analysis.SelectedView = view;
            if (kind != "-") analysis.ChartKind = kind;
          });
          Step("add filter", () => analysis.AddFilterCommand.Execute(null));
          var row = window.GetVisualDescendants().OfType<StackPanel>()
            .Last(p => p.DataContext is FilterViewModel && p.Children.OfType<ComboBox>().Any());
          var columns = (ComboBox)row.Children[0];
          var ops = (ComboBox)row.Children[1];
          var value = (TextBox)row.Children[2];
          // every column, each with a value typed key by key (text, numbers, a partial number)
          for (int c = 0; c < columns.ItemCount; c++) {
            int index = c;
            Step($"view {view} {kind}: column {index}", () => columns.SelectedIndex = index);
            foreach (var op in new[] { 2, 6 }) {
              Step($"op {op}", () => ops.SelectedIndex = op);
              Step("focus", () => value.Focus());
              foreach (var text in new[] { "1", "e", "-", "." }) Step($"type {text} into column {index}", () => window.KeyTextInput(text));
              for (int k = 0; k < 4; k++) Step($"backspace in column {index}", () => { window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null); window.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null); });
            }
          }
          Step("remove filter", () => analysis.RemoveFilterCommand.Execute(analysis.Filters.Last()));
          if (log.Count > 0) break;
        }
        if (log.Count > 0) break;
      }
      Assert.AreEqual(0, log.Count, string.Join("\n\n", log.Take(5)));

      // through the controls: the algorithm type contains "Tabu" - one run; the column stays chosen while typing
      analysis.SelectedView = RunAnalysisViewModel.ChartsView;
      analysis.AddFilterCommand.Execute(null);
      Pump();
      var last = window.GetVisualDescendants().OfType<StackPanel>().Last(p => p.DataContext is FilterViewModel && p.Children.OfType<ComboBox>().Any());
      var column = (ComboBox)last.Children[0];
      column.SelectedItem = analysis.AllColumns.Single(c => c.Key == "param:Algorithm Type");
      ((ComboBox)last.Children[1]).SelectedItem = "contains";
      ((TextBox)last.Children[2]).Focus();
      foreach (var key in "Tabu") { window.KeyTextInput(key.ToString()); Pump(); }
      Assert.AreEqual(1, analysis.Table.Rows.Count);
      Assert.AreEqual("param:Algorithm Type", ((ColumnChoice)column.SelectedItem!).Key, "the column is still chosen");
      analysis.RemoveFilterCommand.Execute(analysis.Filters.Single());
      Pump();
      Assert.AreEqual(all, analysis.Table.Rows.Count);
      window.Close();
      return true;
    }, CancellationToken.None);
  }
}
