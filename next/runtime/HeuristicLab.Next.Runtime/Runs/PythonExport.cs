using System.Globalization;
using System.Text;
using HeuristicLab.Analysis;

namespace HeuristicLab.Next.Runtime.Runs;

/// <summary>A run to export and where it was made: names from the experiment down to its block.</summary>
/// <param name="Kinds">The blocks' kinds along the path (Experiment, Batch run, Time-limit run, Algorithm, Results folder).</param>
public sealed record PythonExportRun(IReadOnlyList<string> Path, IReadOnlyList<string> Kinds, RunRow Row);

/// <summary>
/// Writes runs as a Python module that mirrors the experiment: every experiment, batch run,
/// time-limit run and algorithm is an attribute of the block above it (exp.batch_run_1.ga), every
/// run has typed params / results / curves. Editors can complete all of it because the module
/// declares it (generated classes, no dictionaries). Each block also gives its runs as a pandas
/// table (.df) and compare(a, b) puts several blocks side by side. Only the standard library is
/// needed to load it; pandas only for the tables.
/// <para>
/// Large curve data (DataTable rows such as Qualities / BestQuality per generation) goes into
/// "&lt;module&gt;_data.json" next to the module, so the .py stays small enough for editors.
/// </para>
/// </summary>
public static class PythonExport {
  private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
  /// <summary>Curve values up to this count are written into the module itself.</summary>
  public const int InlineCurveValues = 20_000;

  private static readonly HashSet<string> Keywords = [
    "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif",
    "else", "except", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or",
    "pass", "raise", "return", "try", "while", "with", "yield", "match", "case", "type"
  ];

  // a block's own members: a child block may not hide them
  private static readonly HashSet<string> BlockMembers = [
    "name", "kind", "path", "parent", "blocks", "runs", "values", "curves", "df", "curve_df", "find", "compare", "tree"
  ];

  /// <summary>A file name that Python can import: letters, digits and '_' ("New experiment 1" -> "New_experiment_1").</summary>
  public static string ModuleName(string name) {
    var sb = new StringBuilder();
    foreach (var c in name.Trim()) sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
    var module = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
    if (module.Length == 0) module = "runs";
    if (char.IsDigit(module[0])) module = "_" + module;
    return Keywords.Contains(module) ? module + "_" : module;
  }

  /// <summary>A Python name for a HeuristicLab name: "BestQuality" -> best_quality, "Batch run 1" -> batch_run_1.</summary>
  public static string Identifier(string name) {
    var sb = new StringBuilder();
    for (int i = 0; i < name.Length; i++) {
      char c = name[i];
      if (char.IsLetterOrDigit(c)) {
        // a word starts at an upper-case letter after a lower-case letter or digit, or before lower case in an acronym (TSPData -> tsp_data)
        bool boundary = char.IsUpper(c) && i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])
                         || (char.IsUpper(name[i - 1]) && i + 1 < name.Length && char.IsLower(name[i + 1])));
        if (boundary) sb.Append('_');
        sb.Append(char.ToLowerInvariant(c));
      } else sb.Append('_');
    }
    var id = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
    if (id.Length == 0) id = "x";
    if (char.IsDigit(id[0])) id = "n" + id;
    return Keywords.Contains(id) ? id + "_" : id;
  }

  private static string Pascal(string identifier) =>
    string.Concat(identifier.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

  /// <summary>Unique names within one scope: the second "x" becomes x_2.</summary>
  private sealed class Names(IEnumerable<string>? reserved = null) {
    private readonly HashSet<string> used = [.. reserved ?? []];
    public string Take(string wanted) {
      var name = wanted;
      for (int k = 2; !used.Add(name); k++) name = $"{wanted}_{k}";
      return name;
    }
  }

  private sealed class Node(string name, string kind, Node? parent) {
    public string Name { get; } = name;
    public string Kind { get; set; } = kind;
    public Node? Parent { get; } = parent;
    public List<Node> Children { get; } = [];
    public Dictionary<Node, string> ChildAttributes { get; } = [];
    public string Class { get; set; } = "";
    public int RunCount { get; set; }
    public string Path => Parent == null ? Name : Parent.Path + " / " + Name;
    public Node Child(string name, string kind) {
      var child = Children.FirstOrDefault(c => c.Name == name);
      if (child == null) Children.Add(child = new Node(name, kind, this));
      return child;
    }
  }

  private enum ValueType { Integer, Number, Bool, Text }

  private sealed record Field(string Key, string Name, string Attribute, ValueType Type);

  /// <summary>Writes the module (and, for large curves, its data file); returns the files written.</summary>
  /// <param name="path">The .py file; its name should be importable (see ModuleName).</param>
  /// <param name="title">What the runs are, for the module's description (e.g. the experiment's name).</param>
  /// <param name="inlineCurveValues">Curve values up to this count go into the module, more into the data file.</param>
  public static IReadOnlyList<string> Write(string path, string title, IReadOnlyList<PythonExportRun> runs, int inlineCurveValues = InlineCurveValues) {
    var module = System.IO.Path.GetFileNameWithoutExtension(path);
    var dataFile = module + "_data.json";
    var (source, data) = Generate(module, title, runs, dataFile, inlineCurveValues);
    File.WriteAllText(path, source, new UTF8Encoding(false));
    var written = new List<string> { path };
    var dataPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!, dataFile);
    if (data != null) {
      File.WriteAllText(dataPath, data, new UTF8Encoding(false));
      written.Add(dataPath);
    } else if (File.Exists(dataPath)) File.Delete(dataPath);  // left from an earlier, larger export of the same name
    return written;
  }

  /// <summary>The module's source, and the data file's content if the curves are too large to inline.</summary>
  public static (string Source, string? Data) Generate(string module, string title, IReadOnlyList<PythonExportRun> runs, string dataFile,
      int inlineCurveValues = InlineCurveValues) {
    // ---- the tree: one root (the experiment), or a root holding several experiments / folders
    bool single = runs.Count > 0 && runs.All(r => r.Path.Count > 0 && r.Path[0] == runs[0].Path[0]);
    var root = single ? new Node(runs[0].Path[0], runs[0].Kinds[0], null) : new Node(title, "Runs", null);
    var origins = new List<Node>();
    foreach (var run in runs) {
      var node = root;
      for (int i = single ? 1 : 0; i < run.Path.Count; i++) node = node.Child(run.Path[i], run.Kinds[i]);
      origins.Add(node);
      for (var n = node; n != null; n = n.Parent) n.RunCount++;
    }
    var classNames = new Names(["Block", "Run", "Params", "Results", "Curves"]);
    void Name(Node node) {
      node.Class = classNames.Take("_" + (node.Parent == null ? Pascal(Identifier(node.Name)) : node.Parent.Class.TrimStart('_') + "_" + Pascal(Identifier(node.Name))));
      var attributes = new Names(BlockMembers);
      foreach (var child in node.Children) {
        node.ChildAttributes[child] = attributes.Take(Identifier(child.Name));
        Name(child);
      }
    }
    Name(root);

    // ---- fields: every parameter and scalar result of any run, typed by its values
    List<Field> Fields(string prefix) {
      var names = new Names(["NAMES"]);
      return runs.SelectMany(r => r.Row.Values.Keys).Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Distinct()
        .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
        .Select(k => {
          var values = runs.Select(r => r.Row.Values.GetValueOrDefault(k)).OfType<object>().ToList();
          var type = values.All(v => v is double d && d == Math.Floor(d) && Math.Abs(d) < 1e15) ? ValueType.Integer
            : values.All(v => v is double) ? ValueType.Number
            : values.All(v => v is "True" or "False") ? ValueType.Bool : ValueType.Text;
          return new Field(k, k[prefix.Length..], names.Take(Identifier(k[prefix.Length..])), type);
        }).ToList();
    }
    var parameters = Fields("param:");
    var results = Fields("result:");

    // ---- curves: every DataTable row of the runs held in memory
    var tables = new SortedDictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
    foreach (var run in runs)
      if (run.Row.Run != null)
        foreach (var (name, item) in run.Row.Run.Results)
          if (item is DataTable table) {
            if (!tables.TryGetValue(name, out var rows)) tables[name] = rows = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in table.Rows) rows.Add(row.Name);
          }
    var tableNames = new Names(["NAMES"]);
    var curveTables = tables.Select(t => {
      var rowNames = new Names(["NAMES"]);
      var attribute = tableNames.Take(Identifier(t.Key));
      return (Name: t.Key, Attribute: attribute, Class: "_" + Pascal(attribute) + "Curves",
              Rows: t.Value.Select(r => (Name: r, Attribute: rowNames.Take(Identifier(r)))).ToList());
    }).ToList();
    var curveValues = runs.Select(r => {
      var byTable = new Dictionary<string, Dictionary<string, double[]>>();
      if (r.Row.Run != null)
        foreach (var t in curveTables)
          if (r.Row.Run.Results.TryGetValue(t.Name, out var item) && item is DataTable table) {
            var rows = byTable[t.Attribute] = [];
            foreach (var row in t.Rows)
              if (table.Rows.TryGetValue(row.Name, out var dataRow)) rows[row.Attribute] = dataRow.Values.ToArray();
          }
      return byTable;
    }).ToList();
    bool inline = curveValues.Sum(c => c.Values.Sum(t => t.Values.Sum(v => v.Length))) <= inlineCurveValues;

    // ---- the module
    var py = new StringBuilder();
    void Line(string text = "") => py.Append(text).Append('\n');

    Line("\"\"\"");
    Line($"Runs of {Doc(title)}, exported from HeuristicLab Studio on {DateTime.Now.ToString("yyyy-MM-dd HH:mm", Invariant)}.");
    Line();
    Line("The blocks are attributes of the blocks above them, as in Studio's tree:");
    Line();
    var example = Example(root);
    var exampleResult = (results.FirstOrDefault(f => f.Name == "BestQuality") ?? results.FirstOrDefault(f => f.Type is ValueType.Number or ValueType.Integer))?.Attribute ?? "...";
    var exampleTable = curveTables.FirstOrDefault(t => t.Name == "Qualities");
    if (exampleTable.Name == null && curveTables.Count > 0) exampleTable = curveTables[0];
    Line($"    from {module} import exp, compare");
    Line($"    exp{example}.runs[0].results.{exampleResult}");
    Line($"    exp{example}.values(\"{exampleResult}\")   # one value per run");
    Line($"    exp.df                       # every run as a pandas table (path, block, run, parameters, results)");
    Line($"    compare(exp{example}, ...)    # several blocks in one table, column \"group\" = which block");
    if (exampleTable.Name != null) {
      var exampleRow = exampleTable.Rows.FirstOrDefault(r => r.Name == "BestQuality");
      if (exampleRow.Name == null) exampleRow = exampleTable.Rows[0];
      Line($"    exp.curves(\"{exampleTable.Attribute}\", \"{exampleRow.Attribute}\")   # one list per run, e.g. per generation");
    }
    Line();
    Line("Blocks (name in HeuristicLab -> attribute):");
    Line();
    Tree(root, "exp", 1, Line);
    Line();
    Line("Parameter and result names are HeuristicLab's in snake case; run.params[\"PopulationSize\"] also works.");
    Line("Loading needs only Python's standard library; .df, compare() and curve_df() need pandas.");
    if (!inline) Line($"The curves are in {dataFile}, which has to stay next to this file.");
    Line("\"\"\"");
    Line();
    Line("from __future__ import annotations");
    Line();
    Line("import json");
    Line("import math");
    Line("import os");
    Line("from dataclasses import dataclass, field");
    Line("from typing import Any, ClassVar, Iterator, Optional, TypeVar");
    Line();
    Line("nan = math.nan");
    Line("inf = math.inf");
    Line();
    Line();
    WriteFields(Line, "Params", "Parameters of a run.", parameters);
    WriteFields(Line, "Results", "Scalar results of a run (tables are in Run.curves).", results);
    foreach (var t in curveTables) {
      Line("@dataclass");
      Line($"class {t.Class}:");
      Line($"    \"\"\"Rows of the table {Doc(t.Name)}: one value per step (e.g. generation).\"\"\"");
      Line($"    NAMES: ClassVar[dict[str, str]] = {{{string.Join(", ", t.Rows.Select(r => $"{Str(r.Name)}: {Str(r.Attribute)}"))}}}");
      foreach (var row in t.Rows) Line($"    {row.Attribute}: Optional[list[float]] = None");
      Line();
      Line("    def __getitem__(self, name: str) -> Optional[list[float]]:");
      Line("        return getattr(self, self.NAMES.get(name, name))");
      Line();
      Line();
    }
    Line("@dataclass");
    Line("class Curves:");
    Line("    \"\"\"The run's tables (HeuristicLab DataTables), row by row.\"\"\"");
    Line($"    NAMES: ClassVar[dict[str, str]] = {{{string.Join(", ", curveTables.Select(t => $"{Str(t.Name)}: {Str(t.Attribute)}"))}}}");
    foreach (var t in curveTables) Line($"    {t.Attribute}: {t.Class} = field(default_factory={t.Class})");
    Line();
    Line("    def __getitem__(self, name: str) -> Any:");
    Line("        return getattr(self, self.NAMES.get(name, name))");
    Line();
    Line();
    Line("_B = TypeVar(\"_B\", bound=\"Block\")");
    Line();
    Line();
    Line(Runtime);
    Line();

    // block classes, deepest first so each class exists before it is used
    foreach (var node in PostOrder(root)) {
      Line($"class {node.Class}(Block):");
      Line($"    \"\"\"{Doc(node.Kind)} {Doc(node.Name)} ({node.RunCount} run{(node.RunCount == 1 ? "" : "s")}).\"\"\"");
      Line();
      Line("    def __init__(self) -> None:");
      Line($"        super().__init__({Str(node.Name)}, {Str(node.Kind)})");
      foreach (var child in node.Children) Line($"        self.{node.ChildAttributes[child]} = self._add({child.Class}())");
      Line();
      Line();
    }
    Line($"exp = {root.Class}()");
    Line($"\"\"\"{Doc(root.Kind)} {Doc(root.Name)}: all exported runs.\"\"\"");
    Line();
    if (curveTables.Count > 0) {
      if (inline) {
        Line("_CURVES: list[dict[str, dict[str, list[float]]]] = [");
        foreach (var c in curveValues)
          Line("    {" + string.Join(", ", c.Select(t => $"{Str(t.Key)}: {{{string.Join(", ", t.Value.Select(r => $"{Str(r.Key)}: [{string.Join(", ", r.Value.Select(Float))}]"))}}}")) + "},");
        Line("]");
      } else {
        Line($"with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), {Str(dataFile)}), encoding=\"utf-8\") as _file:");
        Line("    _CURVES: list[dict[str, dict[str, list[float]]]] = json.load(_file)");
      }
    } else Line("_CURVES: list[dict[str, dict[str, list[float]]]] = []");
    Line();
    Line();
    Line("def _curves(index: int) -> Curves:");
    Line("    tables = _CURVES[index] if index < len(_CURVES) else {}");
    Line("    curves = Curves()");
    Line("    for table, rows in tables.items():");
    Line("        target = getattr(curves, table)");
    Line("        for row, values in rows.items():");
    Line("            setattr(target, row, [nan if v is None else v for v in values])");
    Line("    return curves");
    Line();
    Line();
    for (int i = 0; i < runs.Count; i++) {
      var row = runs[i].Row;
      var node = origins[i];
      Line($"exp{Access(node)}._run(Run(");
      Line($"    name={Str(row.Text("run"))},");
      Line($"    params=Params({Arguments(parameters, row)}),");
      Line($"    results=Results({Arguments(results, row)}),");
      Line($"    curves=_curves({i}),");
      Line("))");
    }
    Line();
    Line();
    Line("if __name__ == \"__main__\":");
    Line("    print(exp.tree())");

    string? data = null;
    if (!inline) {
      var json = new StringBuilder("[\n");
      for (int i = 0; i < curveValues.Count; i++) {
        json.Append("  {").Append(string.Join(", ", curveValues[i].Select(t =>
          $"{Str(t.Key)}: {{{string.Join(", ", t.Value.Select(r => $"{Str(r.Key)}: [{string.Join(", ", r.Value.Select(JsonNumber))}]"))}}}")));
        json.Append(i + 1 < curveValues.Count ? "},\n" : "}\n");
      }
      data = json.Append("]\n").ToString();
    }
    return (py.ToString(), data);
  }

  private static IEnumerable<Node> PostOrder(Node node) => node.Children.SelectMany(PostOrder).Append(node);

  /// <summary>".batch_run_1.ga" from the root to this block.</summary>
  private static string Access(Node node) => node.Parent == null ? "" : Access(node.Parent) + "." + node.Parent.ChildAttributes[node];

  /// <summary>The first algorithm (the deepest first block), for the examples.</summary>
  private static string Example(Node root) {
    var node = root;
    while (node.Children.Count > 0) node = node.Children[0];
    return Access(node);
  }

  private static void Tree(Node node, string attribute, int depth, Action<string> line) {
    line($"{new string(' ', depth * 4)}{attribute}  {node.Name} ({node.Kind}, {node.RunCount} run{(node.RunCount == 1 ? "" : "s")})");
    foreach (var child in node.Children) Tree(child, "." + node.ChildAttributes[child], depth + 1, line);
  }

  private static void WriteFields(Action<string> line, string className, string doc, IReadOnlyList<Field> fields) {
    line("@dataclass");
    line($"class {className}:");
    line($"    \"\"\"{doc} NAMES: HeuristicLab's name -> attribute.\"\"\"");
    line($"    NAMES: ClassVar[dict[str, str]] = {{{string.Join(", ", fields.Select(f => $"{Str(f.Name)}: {Str(f.Attribute)}"))}}}");
    foreach (var f in fields) {
      var type = f.Type switch { ValueType.Integer => "int", ValueType.Number => "float", ValueType.Bool => "bool", _ => "str" };
      line($"    {f.Attribute}: Optional[{type}] = None");
    }
    line("");
    line("    def __getitem__(self, name: str) -> Any:");
    line("        return getattr(self, self.NAMES.get(name, name))");
    line("");
    line("");
  }

  private static string Arguments(IReadOnlyList<Field> fields, RunRow row) =>
    string.Join(", ", fields.Where(f => row.Values.ContainsKey(f.Key)).Select(f => $"{f.Attribute}={Value(f, row.Values[f.Key])}"));

  private static string Value(Field field, object value) => field.Type switch {
    ValueType.Integer => ((long)(double)value).ToString(Invariant),
    ValueType.Number => Float((double)value),
    ValueType.Bool => (string)value,
    _ => Str(value is double d ? d.ToString("R", Invariant) : (string)value)
  };

  private static string Number(double d) =>
    double.IsNaN(d) ? "nan" : double.IsPositiveInfinity(d) ? "inf" : double.IsNegativeInfinity(d) ? "-inf"
    : d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(Invariant) : d.ToString("R", Invariant);

  /// <summary>Always a Python float ("20.0", not "20").</summary>
  private static string Float(double d) {
    var text = Number(d);
    return double.IsFinite(d) && !text.Contains('.') && !text.Contains('E') ? text + ".0" : text;
  }

  private static string JsonNumber(double d) => double.IsNaN(d) || double.IsInfinity(d) ? "null" : Float(d);

  /// <summary>A Python (and JSON) string literal.</summary>
  private static string Str(string s) {
    var sb = new StringBuilder("\"");
    foreach (var c in s)
      sb.Append(c switch {
        '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t",
        _ when c < ' ' => $"\\u{(int)c:x4}",
        _ => c.ToString()
      });
    return sb.Append('"').ToString();
  }

  /// <summary>Text for a docstring: no closing triple quote, no stray backslash.</summary>
  private static string Doc(string s) => s.Replace("\\", "\\\\").Replace("\"\"\"", "\\\"\\\"\\\"");

  // the part of the module that is the same for every export
  private const string Runtime = """"
@dataclass
class Run:
    """One run: its name, the block it was made in, its parameters, results and curves."""
    name: str
    params: Params
    results: Results
    curves: Curves
    block: Optional[Block] = field(default=None, repr=False)

    @property
    def path(self) -> str:
        """Where it was made, e.g. "Experiment / Batch run 1 / Genetic Algorithm"."""
        return self.block.path if self.block else ""

    def value(self, name: str) -> Any:
        """A result, else a parameter, by attribute or HeuristicLab name (None if the run has none)."""
        for part in (self.results, self.params):
            attribute = part.NAMES.get(name, name)
            if hasattr(part, attribute) and getattr(part, attribute) is not None:
                return getattr(part, attribute)
        return None


class Block:
    """An experiment, batch run, time-limit run or algorithm with the runs made in it or below it."""

    def __init__(self, name: str, kind: str) -> None:
        self.name = name
        self.kind = kind
        self.parent: Optional[Block] = None
        self.blocks: list[Block] = []
        self.runs: list[Run] = []
        """Every run made in this block or below it."""

    def _add(self, block: _B) -> _B:
        block.parent = self
        self.blocks.append(block)
        return block

    def _run(self, run: Run) -> None:
        run.block = self
        block: Optional[Block] = self
        while block is not None:
            block.runs.append(run)
            block = block.parent

    @property
    def path(self) -> str:
        return self.parent.path + " / " + self.name if self.parent else self.name

    def __iter__(self) -> Iterator[Run]:
        return iter(self.runs)

    def __len__(self) -> int:
        return len(self.runs)

    def __repr__(self) -> str:
        return f"<{self.kind} {self.name!r}: {len(self.runs)} runs, {len(self.blocks)} blocks>"

    def values(self, name: str) -> list[Any]:
        """One value per run: a result or parameter by attribute or HeuristicLab name ("best_quality" or "BestQuality")."""
        return [run.value(name) for run in self.runs]

    def curves(self, table: str, row: str) -> list[list[float]]:
        """One list per run: a table's row, e.g. curves("qualities", "best_quality")."""
        result = []
        for run in self.runs:
            values = run.curves[table][row]
            result.append(values if values is not None else [])
        return result

    def find(self, name: str) -> Optional[Block]:
        """The first block below (or this one) with this name in HeuristicLab."""
        if self.name == name:
            return self
        for block in self.blocks:
            found = block.find(name)
            if found is not None:
                return found
        return None

    def tree(self, indent: str = "") -> str:
        """The blocks below as text."""
        count = len(self.runs)
        lines = [f"{indent}{self.name} ({self.kind}, {count} run{'' if count == 1 else 's'})"]
        lines += [block.tree(indent + "    ") for block in self.blocks]
        return "\n".join(lines)

    @property
    def df(self) -> Any:
        """The runs as a pandas DataFrame: path, block, run, then every parameter and result."""
        import pandas as pd
        rows = []
        for run in self.runs:
            row: dict[str, Any] = {"path": run.path, "block": run.block.name if run.block else "", "run": run.name}
            for attribute in Params.NAMES.values():
                row[attribute] = getattr(run.params, attribute)
            for attribute in Results.NAMES.values():
                row[("result_" + attribute) if attribute in row else attribute] = getattr(run.results, attribute)
            rows.append(row)
        return pd.DataFrame(rows)

    def curve_df(self, table: str, row: str) -> Any:
        """A table's row of every run in long form (path, block, run, step, value): for seaborn's lineplot."""
        import pandas as pd
        rows = []
        for run in self.runs:
            values = run.curves[table][row] or []
            for step, value in enumerate(values):
                rows.append({"path": run.path, "block": run.block.name if run.block else "", "run": run.name, "step": step, "value": value})
        return pd.DataFrame(rows)


def compare(*blocks: Block) -> Any:
    """Several blocks' runs in one pandas DataFrame; the column "group" says which block (its name, or its path if names repeat)."""
    import pandas as pd
    names = [block.name for block in blocks]
    frames = []
    for block in blocks:
        frame = block.df
        frame.insert(0, "group", block.name if names.count(block.name) == 1 else block.path)
        frames.append(frame)
    return pd.concat(frames, ignore_index=True)


def compare_curves(table: str, row: str, *blocks: Block) -> Any:
    """compare() for curves: a table's row of several blocks in long form, column "group" = which block."""
    import pandas as pd
    names = [block.name for block in blocks]
    frames = []
    for block in blocks:
        frame = block.curve_df(table, row)
        frame.insert(0, "group", block.name if names.count(block.name) == 1 else block.path)
        frames.append(frame)
    return pd.concat(frames, ignore_index=True)

"""";
}
