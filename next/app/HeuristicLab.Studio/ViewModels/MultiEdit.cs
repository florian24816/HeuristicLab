using System;
using System.Linq;
using HeuristicLab.Core;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;

namespace HeuristicLab.Studio.ViewModels;

/// <summary>
/// "Edit across algorithms": one field (a parameter's value or its choice) addressed the same way in
/// every algorithm of a type, so it can be edited in one block after another.
/// <para>
/// A field key is a path from the algorithm plus the kind of field, e.g.
/// <c>a/p:MaximumIterations#value</c> (Algorithm tab), <c>a/p:MoveEvaluator#choice</c>,
/// <c>a/p:Analyzer/e:BestTSPSolutionAnalyzer/p:UpdateInterval#value</c> (an analyzer's option) or
/// <c>g/o:2:MainLoop/n:5:Comparator/p:Comparison#choice</c> (Operator Graph tab: opened nodes and the
/// selected node, by position in the layout and name). Steps: p = parameter (into its value),
/// e = entry of a checked list (analyzers), o = open a node's own graph, n = a node of the graph.
/// </para>
/// </summary>
public static class MultiEdit {
  public const string ChoiceField = "#choice", ValueField = "#value";
  public const string AlgorithmTab = "a", GraphTab = "g";

  public static string Parameter(string prefix, string name) => $"{prefix}/p:{name}";
  public static string Entry(string prefix, string name) => $"{prefix}/e:{name}";
  public static string Node(string prefix, int index, string name) => $"{prefix}/n:{index}:{name}";
  public static string Open(string prefix, int index, string name) => $"{prefix}/o:{index}:{name}";

  public static string[] Steps(string key) => key[..key.LastIndexOf('#')].Split('/');
  public static bool IsChoice(string key) => key.EndsWith(ChoiceField, StringComparison.Ordinal);

  /// <summary>Whether the algorithm has this field (an editable value or a choice) at this path.</summary>
  public static bool Resolves(IAlgorithm algorithm, string key) {
    var steps = Steps(key);
    object? current = steps[0] switch {
      AlgorithmTab => algorithm,
      GraphTab => ItemInspector.GraphOf(algorithm),
      _ => null
    };
    IParameter? parameter = null;
    foreach (var step in steps.Skip(1)) {
      string name = step[2..];
      parameter = null;
      switch (step[..2]) {
        case "p:":
          parameter = current is IParameterizedItem item ? ItemInspector.Parameters(item).FirstOrDefault(p => p.Name == name) : null;
          if (parameter == null) return false;
          current = ItemInspector.Value(parameter);
          break;
        case "e:":
          var list = CheckedList.TryCreate(current as IItem);
          current = list == null ? null : Enumerable.Range(0, list.Count).Select(i => list[i]).FirstOrDefault(x => (x as INamedItem)?.Name == name);
          if (current == null) return false;
          break;
        case "o:":
        case "n:":
          if (current is not OperatorGraph graph || NodeAt(graph, name) is not { } op) return false;
          current = step[0] == 'o' ? ItemInspector.GraphOf(op) : op;
          if (current == null) return false;
          break;
        default:
          return false;
      }
    }
    if (parameter == null) return false;
    return IsChoice(key)
      ? ItemInspector.Choices(parameter) is { Count: > 0 }
      : ItemInspector.KindOf(ItemInspector.Value(parameter)) is ValueKind.Text or ValueKind.Bool;
  }

  /// <summary>"index:name" of a node in the graph's layout (the name must match too).</summary>
  public static IOperator? NodeAt(OperatorGraph graph, string indexAndName) {
    int colon = indexAndName.IndexOf(':');
    if (colon < 0 || !int.TryParse(indexAndName[..colon], out int index)) return null;
    var nodes = ItemInspector.Layout(graph).Nodes;
    return index < nodes.Count && nodes[index].Operator.Name == indexAndName[(colon + 1)..] ? nodes[index].Operator : null;
  }
}

/// <summary>The field being edited across algorithms: in which experiment, for which algorithm type.</summary>
public sealed record MultiEditState(ExperimentBlockViewModel Root, Type AlgorithmType, string Key);

/// <summary>An editor field that can be put into (or out of) "edit across algorithms" by Shift+click.</summary>
public interface IMultiField {
  /// <summary>Shift+click on the field: starts editing it across algorithms, or ends it; false if not possible here.</summary>
  bool ToggleMulti();
}
