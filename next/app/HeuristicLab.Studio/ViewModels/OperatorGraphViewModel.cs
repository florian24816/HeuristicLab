using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeuristicLab.Core;
using HeuristicLab.Next.Runtime;
using static HeuristicLab.Next.Runtime.ItemInspector;

namespace HeuristicLab.Studio.ViewModels;

public sealed record GraphNodeView(IOperator Operator, double X, double Y, string Name, string Kind, bool IsInitial, bool HasGraph) {
  /// <summary>An operator with its own graph inside (e.g. a main loop) or a single one.</summary>
  public string Pictogram => HasGraph ? "operator_multiple" : "operator_single";
}

/// <summary>
/// An arrow from one operator to the next: a curve and its arrowhead. Label: the parameter that
/// points to the next operator, shown only when it says more than "Successor" (e.g. TrueBranch).
/// Back: to an operator of the same or an earlier layer (a loop), drawn along the right side.
/// </summary>
public sealed record GraphEdgeView(Avalonia.Media.Geometry Line, Avalonia.Media.Geometry Arrow, string Label, double LabelX, double LabelY, bool IsBack) {
  public bool HasLabel => Label.Length > 0;
}

/// <summary>
/// An operator graph drawn in layers from the initial operator. Selecting an operator shows its
/// name, type, breakpoint and parameters; operators with their own graph (e.g. a main loop) can
/// be opened, and Back returns to the enclosing graph.
/// </summary>
public partial class OperatorGraphViewModel : ViewModelBase {
  // a node: square (pictogram) + gap + rectangle (name, type)
  public const double SquareSize = 44, NodeWidth = 260, NodeHeight = 44, LayerGap = 64, RowGap = 48, Margin = 24, TopRoom = 34;
  private const double ArrowLength = 9, ArrowWidth = 5, PortSpacing = 18, LoopGap = 28, LoopStep = 16;
  private readonly EditContext context;
  private readonly Stack<(OperatorGraph Graph, string Title, string Address)> path = new();

  public OperatorGraphViewModel(OperatorGraph graph, string title, EditContext context) {
    this.context = context;
    Show(graph, title, MultiEdit.GraphTab);
  }

  /// <summary>Address of the shown graph for editing across algorithms (opened nodes from the algorithm's graph).</summary>
  public string Address => path.Peek().Address;

  [ObservableProperty]
  public partial string Title { get; set; } = "";

  [ObservableProperty]
  public partial IReadOnlyList<GraphNodeView> Nodes { get; set; } = [];

  [ObservableProperty]
  public partial IReadOnlyList<GraphEdgeView> Edges { get; set; } = [];

  [ObservableProperty]
  public partial double Width { get; set; }

  [ObservableProperty]
  public partial double Height { get; set; }

  [ObservableProperty]
  public partial GraphNodeView? Selected { get; set; }

  [ObservableProperty]
  public partial ValueEditorViewModel? SelectedEditor { get; set; }

  [ObservableProperty]
  public partial double Zoom { get; set; } = 1;

  public bool CanGoBack => path.Count > 1;

  partial void OnSelectedChanged(GraphNodeView? value) {
    SelectedEditor = value == null ? null
      : new ValueEditorViewModel(value.Operator, context, path: MultiEdit.Node(Address, IndexOf(value), value.Operator.Name));
    OpenCommand.NotifyCanExecuteChanged();
  }

  public void Select(IOperator op) => Selected = Nodes.FirstOrDefault(n => n.Operator == op);

  private int IndexOf(GraphNodeView node) => Nodes.ToList().IndexOf(node);

  /// <summary>Back to the algorithm's own graph.</summary>
  public void Home() {
    while (path.Count > 1) Back();
  }

  /// <summary>Opens the node at this layout position ("index:name") or selects it; false if it is not there.</summary>
  public bool Go(string step) {
    if (Nodes.Count == 0) return false;
    var node = step[2..].Split(':', 2) is [var i, var name] && int.TryParse(i, out int index) && index < Nodes.Count
      && Nodes[index].Name == name ? Nodes[index] : null;
    if (node == null) return false;
    Selected = node;
    if (step.StartsWith("o:", System.StringComparison.Ordinal)) {
      if (!node.HasGraph) return false;
      Open();
    }
    return true;
  }

  [RelayCommand(CanExecute = nameof(CanOpen))]
  private void Open() {
    if (Selected != null && ItemInspector.GraphOf(Selected.Operator) is OperatorGraph g)
      Show(g, Selected.Name, MultiEdit.Open(Address, IndexOf(Selected), Selected.Name));
  }
  private bool CanOpen() => Selected?.HasGraph == true;

  [RelayCommand]
  private void Back() {
    if (path.Count < 2) return;
    path.Pop();
    var (graph, title, address) = path.Pop();
    Show(graph, title, address);
  }

  private void Show(OperatorGraph graph, string title, string address) {
    path.Push((graph, title, address));
    Title = string.Join(" › ", path.Reverse().Select(p => p.Title));
    var layout = ItemInspector.Layout(graph);
    var position = Place(layout);
    // the node list keeps the layout's order: positions in it address operators (MultiEdit)
    Nodes = layout.Nodes.Select(n => new GraphNodeView(n.Operator, position[n.Operator].X, position[n.Operator].Y,
      n.Operator.Name, n.Operator.ItemName, n.Operator == layout.Initial, ItemInspector.GraphOf(n.Operator) != null)).ToList();
    var (edges, right) = Route(layout, position);
    Edges = edges;
    Width = Nodes.Count == 0 ? 400 : Math.Max(Nodes.Max(n => n.X) + NodeWidth, right) + Margin;
    Height = Nodes.Count == 0 ? 200 : Nodes.Max(n => n.Y) + NodeHeight + Margin;
    Selected = Nodes.FirstOrDefault(n => n.IsInitial) ?? Nodes.FirstOrDefault();
    OnPropertyChanged(nameof(CanGoBack));
  }

  /// <summary>
  /// Top-left corners: layers from top to bottom, each centered; within a layer the operators are
  /// ordered by where their predecessors are (fewer crossing arrows).
  /// </summary>
  private static Dictionary<IOperator, (double X, double Y)> Place(GraphLayout layout) {
    var position = new Dictionary<IOperator, (double X, double Y)>();
    var layers = layout.Nodes.GroupBy(n => n.Layer).OrderBy(g => g.Key).Select(g => g.Select(n => n.Operator).ToList()).ToList();
    int widest = layers.Count == 0 ? 0 : layers.Max(l => l.Count);
    var predecessors = layout.Edges.ToLookup(e => e.To, e => e.From);
    for (int l = 0; l < layers.Count; l++) {
      var layer = layers[l];
      if (l > 0)
        layer = layer.Select((op, i) => (op, i, key: predecessors[op].Where(position.ContainsKey).Select(p => position[p].X).DefaultIfEmpty(double.NaN).Average()))
          .OrderBy(t => double.IsNaN(t.key) ? double.MaxValue : t.key).ThenBy(t => t.i).Select(t => t.op).ToList();
      double left = Margin + (widest - layer.Count) * (NodeWidth + RowGap) / 2;
      for (int i = 0; i < layer.Count; i++)
        position[layer[i]] = (left + i * (NodeWidth + RowGap), Margin + TopRoom + l * (NodeHeight + LayerGap));
    }
    return position;
  }

  /// <summary>
  /// Arrows: to a later layer as a curve from the bottom of one block to the top of the next (several
  /// arrows of a block spread along its edge); back to an earlier or the same layer (loops) along the
  /// right side, each loop a bit further out. Plus a short "Start" arrow into the initial operator.
  /// Returns the arrows and how far to the right they reach.
  /// </summary>
  private static (List<GraphEdgeView> Edges, double Right) Route(GraphLayout layout, Dictionary<IOperator, (double X, double Y)> position) {
    var layerOf = layout.Nodes.ToDictionary(n => n.Operator, n => n.Layer);
    var edges = new List<GraphEdgeView>();
    double right = 0;
    var forward = layout.Edges.Where(e => layerOf[e.To] > layerOf[e.From]).ToList();
    var back = layout.Edges.Where(e => layerOf[e.To] <= layerOf[e.From]).ToList();
    double Port(List<GraphEdge> group, GraphEdge e, double x) =>
      x + SquareSize + 3 + (NodeWidth - SquareSize - 3) / 2 + (group.IndexOf(e) - (group.Count - 1) / 2.0) * PortSpacing;
    var outgoing = forward.GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.OrderBy(e => position[e.To].X).ToList());
    var incoming = forward.GroupBy(e => e.To).ToDictionary(g => g.Key, g => g.OrderBy(e => position[e.From].X).ToList());
    foreach (var e in forward) {
      var (fx, fy) = position[e.From];
      var (tx, ty) = position[e.To];
      double x1 = Port(outgoing[e.From], e, fx), y1 = fy + NodeHeight;
      double x2 = Port(incoming[e.To], e, tx), y2 = ty;
      double end = y2 - ArrowLength, bend = Math.Max(16, (end - y1) / 2);
      edges.Add(new GraphEdgeView(
        Geometry($"M {N(x1)},{N(y1)} C {N(x1)},{N(y1 + bend)} {N(x2)},{N(end - bend)} {N(x2)},{N(end)}"),
        Arrowhead(x2, y2, 0, 1), Label(e), x1 + 5, y1 + 2, false));
    }
    double loopsFrom = position.Count == 0 ? 0 : position.Values.Max(p => p.X) + NodeWidth;
    for (int k = 0; k < back.Count; k++) {
      var e = back[k];
      var (fx, fy) = position[e.From];
      var (tx, ty) = position[e.To];
      double x1 = fx + NodeWidth, y1 = fy + NodeHeight / 2 + 6;
      double x2 = tx + NodeWidth, y2 = ty + NodeHeight / 2 - 6;
      double outer = loopsFrom + LoopGap + k * LoopStep;
      edges.Add(new GraphEdgeView(
        Geometry($"M {N(x1)},{N(y1)} H {N(outer - 8)} Q {N(outer)},{N(y1)} {N(outer)},{N(y1 - 8)} V {N(y2 + 8)} Q {N(outer)},{N(y2)} {N(outer - 8)},{N(y2)} H {N(x2 + ArrowLength)}"),
        Arrowhead(x2, y2, -1, 0), Label(e), outer + 4, (y1 + y2) / 2 - 7, true));
      right = Math.Max(right, outer + 4 + 7 * Label(e).Length);
    }
    if (layout.Initial is { } initial && position.TryGetValue(initial, out var start)) {
      double x = start.X + SquareSize + 3 + (NodeWidth - SquareSize - 3) / 2;
      edges.Add(new GraphEdgeView(Geometry($"M {N(x)},{N(start.Y - TopRoom + 6)} V {N(start.Y - ArrowLength)}"),
        Arrowhead(x, start.Y, 0, 1), "Start", x + 6, start.Y - TopRoom + 4, false));
    }
    return (edges, right);
  }

  private static string Label(GraphEdge e) => e.Label == "Successor" ? "" : e.Label;

  /// <summary>A filled triangle with its tip at (x, y), pointing in direction (dx, dy).</summary>
  private static Avalonia.Media.Geometry Arrowhead(double x, double y, double dx, double dy) {
    double bx = x - dx * ArrowLength, by = y - dy * ArrowLength, px = -dy * ArrowWidth, py = dx * ArrowWidth;
    return Geometry($"M {N(x)},{N(y)} L {N(bx + px)},{N(by + py)} L {N(bx - px)},{N(by - py)} Z");
  }

  private static Avalonia.Media.Geometry Geometry(string data) => Avalonia.Media.Geometry.Parse(data);
  private static string N(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
