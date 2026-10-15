using System.Globalization;
using System.Text;
using HeuristicLab.Analysis;

namespace HeuristicLab.Next.Runtime.Runs;

/// <summary>A run to export and where it was made: names from the experiment down to its block.</summary>
/// <param name="Kinds">The blocks' kinds along the path (Experiment, Batch run, Time-limit run, Algorithm, Results folder).</param>
public sealed record PythonExportRun(IReadOnlyList<string> Path, IReadOnlyList<string> Kinds, RunRow Row);

/// <summary>
/// Exports runs for Python in three files: "&lt;name&gt;_structure.py" mirrors the experiment (every
/// experiment, batch run, time-limit run and algorithm an attribute of the block above it:
/// exp.batch_run_1.ga; typed params / results / curves, as generated classes so editors complete them;
/// .df / compare() for pandas), "&lt;name&gt;_data.json" holds the values (loaded by the structure on
/// import), "&lt;name&gt;_analysis.py" holds examples (syntax, a box plot, a curve) using only values every
/// exported run has. Only the standard library is needed to load; pandas for tables, seaborn for plots.
/// </summary>
public static class PythonExport {
  private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

  private static readonly HashSet<string> Keywords = [
    "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif",
    "else", "except", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or",
    "pass", "raise", "return", "try", "while", "with", "yield", "match", "case", "type"
  ];

  // a block's own members: a child block may not hide them
  private static readonly HashSet<string> BlockMembers = [
    "name", "kind", "path", "label", "parent", "blocks", "runs", "made_here", "walk", "values", "curves", "df", "curve_df", "find", "compare", "tree"
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

  /// <summary>The files of one export.</summary>
  /// <param name="Structure">"&lt;name&gt;_structure.py": the experiment's blocks and the types of its values; loads the data.</param>
  /// <param name="Data">"&lt;name&gt;_data.json": every run's parameters, results and curves.</param>
  /// <param name="Analysis">"&lt;name&gt;_analysis.py": examples to start from (box plot, curve, the syntax).</param>
  public sealed record Files(string Structure, string Data, string Analysis);

  /// <summary>The files for a name ("New experiment 1" -> New_experiment_1_structure.py, ..._data.json, ..._analysis.py).</summary>
  public static Files FilesFor(string folder, string name) {
    var module = ModuleName(name);
    return new(System.IO.Path.Combine(folder, module + "_structure.py"), System.IO.Path.Combine(folder, module + "_data.json"),
               System.IO.Path.Combine(folder, module + "_analysis.py"));
  }

  /// <summary>
  /// Writes structure and data. The analysis is the user's to change: written only if it does not
  /// exist yet. Returns the files written.
  /// </summary>
  public static IReadOnlyList<string> Write(Files files, string title, IReadOnlyList<PythonExportRun> runs) {
    var (structure, data, analysis) = Generate(System.IO.Path.GetFileNameWithoutExtension(files.Structure), System.IO.Path.GetFileName(files.Data), title, runs);
    var utf8 = new UTF8Encoding(false);
    File.WriteAllText(files.Structure, structure, utf8);
    File.WriteAllText(files.Data, data, utf8);
    var written = new List<string> { files.Structure, files.Data };
    if (!File.Exists(files.Analysis)) {
      File.WriteAllText(files.Analysis, analysis, utf8);
      written.Add(files.Analysis);
    }
    return written;
  }

  /// <summary>The structure module, the data (JSON) and the analysis script.</summary>
  /// <param name="module">The structure module's name (what the analysis imports).</param>
  /// <param name="dataFile">The data file's name, next to the module.</param>
  public static (string Structure, string Data, string Analysis) Generate(string module, string dataFile, string title, IReadOnlyList<PythonExportRun> runs) {
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
    var blocks = PreOrder(root).ToList();

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
      return new CurveTable(t.Key, attribute, "_" + Pascal(attribute) + "Curves",
        t.Value.Select(r => new CurveRow(r, rowNames.Take(Identifier(r)))).ToList());
    }).ToList();
    var curveValues = runs.Select(r => {
      var byTable = new Dictionary<CurveTable, Dictionary<CurveRow, double[]>>();
      if (r.Row.Run != null)
        foreach (var t in curveTables)
          if (r.Row.Run.Results.TryGetValue(t.Name, out var item) && item is DataTable table) {
            var rows = byTable[t] = [];
            foreach (var row in t.Rows)
              if (table.Rows.TryGetValue(row.Name, out var dataRow)) rows[row] = dataRow.Values.ToArray();
          }
      return byTable;
    }).ToList();

    // ---- the structure module
    var py = new StringBuilder();
    void Line(string text = "") => py.Append(text).Append('\n');

    Line("\"\"\"");
    Line($"Runs of {Doc(title)}, exported from HeuristicLab Studio on {DateTime.Now.ToString("yyyy-MM-dd HH:mm", Invariant)}.");
    Line();
    Line($"The experiment's structure: its blocks and the types of its values, so editors can complete them.");
    Line($"The values are in {dataFile} (loaded on import; keep it next to this file). Examples: {module.Replace("_structure", "")}_analysis.py.");
    Line();
    Line("Blocks (name in HeuristicLab -> attribute):");
    Line();
    Tree(root, "exp", 1, Line);
    Line();
    Line("Parameter and result names are HeuristicLab's in snake case; run.params[\"PopulationSize\"] also works.");
    Line("Loading needs only Python's standard library; .df, compare() and the curve tables need pandas.");
    Line("\"\"\"");
    Line();
    Line("from __future__ import annotations");
    Line();
    Line("import json");
    Line("import os");
    Line("from dataclasses import dataclass, field");
    Line("from typing import Any, ClassVar, Iterator, Optional, TypeVar");
    Line();
    Line();
    WriteFields(Line, "Params", "Parameters of a run: its settings.", parameters);
    WriteFields(Line, "Results", "Scalar results of a run: what it found (tables are in Run.curves).", results);
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
    Line("PARAMETERS: list[str] = list(Params.NAMES.values())");
    Line("\"\"\"The parameters' attributes, also the parameter columns of .df.\"\"\"");
    Line("RESULTS: list[str] = [\"result_\" + a if a in Params.NAMES.values() else a for a in Results.NAMES.values()]");
    Line("\"\"\"The results' columns of .df (\"result_\" in front where a parameter has the same name).\"\"\"");
    Line();
    Line();
    Line("def _load() -> None:");
    Line($"    with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), {Str(dataFile)}), encoding=\"utf-8\") as file:");
    Line("        data = json.load(file)");
    Line($"    blocks: list[Block] = [{string.Join(", ", blocks.Select(b => "exp" + Access(b)))}]");
    Line("    for entry in data[\"runs\"]:");
    Line("        curves = Curves()");
    Line("        for table, rows in entry[\"curves\"].items():");
    Line("            for row, values in rows.items():");
    Line("                setattr(getattr(curves, table), row, values)");
    Line("        run = Run(entry[\"name\"], Params(**entry[\"params\"]), Results(**entry[\"results\"]), curves)");
    Line("        blocks[entry[\"block\"]]._run(run)");
    Line();
    Line();
    Line("_load()");
    Line();
    Line("if __name__ == \"__main__\":");
    Line("    print(exp.tree())");

    // ---- the data: one run per line
    var json = new StringBuilder("{\"runs\": [\n");
    for (int i = 0; i < runs.Count; i++) {
      var row = runs[i].Row;
      json.Append("  {\"block\": ").Append(blocks.IndexOf(origins[i])).Append(", \"name\": ").Append(Str(row.Text("run")));
      json.Append(", \"params\": {").Append(JsonFields(parameters, row)).Append('}');
      json.Append(", \"results\": {").Append(JsonFields(results, row)).Append('}');
      json.Append(", \"curves\": {").Append(string.Join(", ", curveValues[i].Select(t =>
        $"{Str(t.Key.Attribute)}: {{{string.Join(", ", t.Value.Select(r => $"{Str(r.Key.Attribute)}: [{string.Join(", ", r.Value.Select(JsonNumber))}]"))}}}")));
      json.Append(i + 1 < runs.Count ? "}},\n" : "}}\n");
    }
    json.Append("]}\n");

    return (py.ToString(), json.ToString(), Analysis(module, title, root, runs, parameters, results, curveTables, curveValues));
  }

  private sealed record CurveRow(string Name, string Attribute);
  private sealed record CurveTable(string Name, string Attribute, string Class, IReadOnlyList<CurveRow> Rows);

  /// <summary>
  /// The analysis script: the syntax on one run, the runs as a pandas table, a box plot of the quality
  /// per block the runs were made in and its curve per step. Its examples only use values every exported
  /// run has, so they fit any algorithm (with or without batch runs).
  /// </summary>
  private static string Analysis(string module, string title, Node root, IReadOnlyList<PythonExportRun> runs,
      IReadOnlyList<Field> parameters, IReadOnlyList<Field> results,
      IReadOnlyList<CurveTable> curveTables, IReadOnlyList<Dictionary<CurveTable, Dictionary<CurveRow, double[]>>> curveValues) {
    bool Everywhere(Field f) => runs.All(r => r.Row.Values.ContainsKey(f.Key));
    bool IsNumber(Field f) => f.Type is ValueType.Integer or ValueType.Number;
    Field? Pick(IReadOnlyList<Field> fields, Func<Field, bool> where, params string[] preferred) {
      var candidates = fields.Where(f => Everywhere(f) && where(f)).ToList();
      return preferred.Select(p => candidates.FirstOrDefault(f => f.Name == p)).FirstOrDefault(f => f != null) ?? candidates.FirstOrDefault();
    }
    var quality = Pick(results, f => IsNumber(f) && f.Name.Contains("Quality"), "BestQuality") ?? Pick(results, IsNumber);
    var textParameter = Pick(parameters, f => f.Type == ValueType.Text, "Algorithm Name", "Algorithm Type", "Problem Name");
    var numberParameter = Pick(parameters, IsNumber, "Seed", "MaximumIterations", "MaximumGenerations");
    var maximization = Pick(parameters, f => f.Type == ValueType.Bool && f.Name == "Maximization");
    // a curve every run has: the qualities' best quality if possible
    var everywhere = curveTables.SelectMany(t => t.Rows.Select(r => (Table: t, Row: r)))
      .Where(c => curveValues.All(v => v.TryGetValue(c.Table, out var rows) && rows.ContainsKey(c.Row))).ToList();
    (CurveTable Table, CurveRow Row)? curve = everywhere.Count == 0 ? null
      : everywhere.FirstOrDefault(c => c.Table.Name == "Qualities" && c.Row.Name == "BestQuality") is { Table: not null } best ? best
      : everywhere.FirstOrDefault(c => c.Table.Name == "Qualities") is { Table: not null } qualities ? qualities : everywhere[0];
    var example = "exp" + Example(root);
    var importName = module;

    var py = new StringBuilder();
    void Line(string text = "") => py.Append(text).Append('\n');
    Line("\"\"\"");
    Line($"Analysis of {Doc(title)}: examples to start from - change them, keep what you need.");
    Line();
    Line($"Run it (python {module.Replace("_structure", "")}_analysis.py), or open it in VS Code / Jupyter: every \"# %%\" starts a cell.");
    Line($"The runs come from {module}.py (the experiment's blocks) and its _data.json (the values).");
    Line("Needs pandas, seaborn and matplotlib:  pip install pandas seaborn matplotlib");
    Line("A new export from Studio does not overwrite this file (delete it to get a fresh one).");
    Line("\"\"\"");
    Line();
    Line("# %%");
    Line("import matplotlib.pyplot as plt");
    Line("import pandas as pd");
    Line("import seaborn as sns");
    Line();
    Line($"from {importName} import PARAMETERS, RESULTS, compare, compare_curves, exp");
    Line();
    Line("print(exp.tree())  # the blocks, as in Studio's tree");
    Line();
    Line("# %% The syntax: blocks, runs, parameters, results, curves");
    Line("# blocks are attributes of the blocks above them (the names are in the tree above):");
    Line($"block = {example}");
    Line("print(block.name, \"-\", block.kind, \"-\", len(block.runs), \"runs\")");
    Line();
    Line("# every block has the runs made in it or below it; exp.runs are all of them");
    Line("run = block.runs[0]");
    Line("print(run.name, \"was made in\", run.path)");
    Line();
    Line("# run.params: the settings it ran with (parameters); run.results: what it found");
    if (textParameter != null) Line($"print(\"Parameter {Doc(textParameter.Name)}:\", run.params.{textParameter.Attribute})");
    if (numberParameter != null) Line($"print(\"Parameter {Doc(numberParameter.Name)}:\", run.params.{numberParameter.Attribute})");
    if (quality != null) {
      Line($"print(\"Result {Doc(quality.Name)}:\", run.results.{quality.Attribute})");
      Line($"print(\"The same with HeuristicLab's name:\", run.results[{Str(quality.Name)}])");
    }
    Line("print(\"All parameters:\", PARAMETERS)");
    Line("print(\"All results:\", RESULTS)");
    if (curve is { } c) {
      Line();
      Line($"# run.curves: tables with one value per step (generation / iteration), here {Doc(c.Table.Name)} -> {Doc(c.Row.Name)}");
      Line($"steps = run.curves.{c.Table.Attribute}.{c.Row.Attribute} or []");
      Line("print(f\"{len(steps)} steps, first {steps[0] if steps else None}, last {steps[-1] if steps else None}\")");
    }
    if (quality != null) {
      Line();
      Line("# one value per run of a block, and plain Python on the runs");
      Line($"print(block.values(\"{quality.Attribute}\"))");
      if (maximization != null) {
        Line($"best = max if run.params.{maximization.Attribute} else min  # the problem says whether larger is better");
        Line($"best_run = best(exp.runs, key=lambda r: r.results.{quality.Attribute} or 0)");
        Line($"print(\"Best {Doc(quality.Name)}:\", best_run.results.{quality.Attribute}, \"in\", best_run.path)");
      } else {
        Line($"best_run = min(exp.runs, key=lambda r: r.results.{quality.Attribute} or 0)  # max(...) if larger is better");
        Line($"print(\"Lowest {Doc(quality.Name)}:\", best_run.results.{quality.Attribute}, \"in\", best_run.path)");
      }
    }
    Line();
    Line("# %% All runs as one pandas table: a row per run, a column per parameter and result");
    Line("df = exp.df");
    Line("print(df[[\"path\", \"run\"] + RESULTS].head())  # only the results (PARAMETERS: only the parameters)");
    if (quality != null) {
      Line($"print(df.groupby(\"path\")[\"{quality.Attribute}\"].describe())  # per block the runs were made in");
      if (textParameter != null)
        Line($"print(df[df[\"{textParameter.Attribute}\"] == run.params.{textParameter.Attribute}][[\"path\", \"run\", \"{quality.Attribute}\"]])  # filter rows by a value");
    }
    Line();
    Line("# the blocks that made runs (algorithms; e.g. one per batch run) - the groups compared below");
    Line("groups = [b for b in exp.walk() if b.made_here]");
    Line("# to compare chosen blocks instead: groups = [exp.batch_run_1, exp.batch_run_2] (names as in the tree)");
    if (quality != null) {
      Line();
      Line($"# %% Box plot: {Doc(quality.Name)} per block, every run a dot");
      Line("data = compare(*groups)  # one table, the column \"group\" says which block");
      Line($"ax = sns.boxplot(data, x=\"group\", y=\"{quality.Attribute}\", color=\"#F9B462\")");
      Line($"sns.stripplot(data, x=\"group\", y=\"{quality.Attribute}\", color=\"black\", size=4, ax=ax)");
      Line($"ax.set(title={Str(title + ": " + quality.Name)}, xlabel=\"\", ylabel={Str(quality.Name)})");
      Line("ax.tick_params(axis=\"x\", labelrotation=20)");
      Line("plt.tight_layout()");
      Line("plt.show()");
    } else {
      Line();
      Line("# (no numeric result that every run has: no box plot example)");
    }
    if (curve is { } k) {
      Line();
      Line($"# %% Curve: {Doc(k.Row.Name)} per step, mean of each block's runs with its 95 % confidence band");
      Line($"curves = compare_curves(\"{k.Table.Attribute}\", \"{k.Row.Attribute}\", *groups)  # long form: group, run, step, value");
      Line("ax = sns.lineplot(curves, x=\"step\", y=\"value\", hue=\"group\")");
      Line($"ax.set(title={Str(title + ": " + k.Table.Name + " / " + k.Row.Name)}, xlabel=\"Step (generation / iteration)\", ylabel={Str(k.Row.Name)})");
      Line("plt.tight_layout()");
      Line("plt.show()");
    } else {
      Line();
      Line("# (no table every run has: no curve example)");
    }
    return py.ToString();
  }

  private static IEnumerable<Node> PreOrder(Node node) => node.Children.SelectMany(PreOrder).Prepend(node);

  private static string JsonFields(IReadOnlyList<Field> fields, RunRow row) =>
    string.Join(", ", fields.Where(f => row.Values.ContainsKey(f.Key)).Select(f => $"{Str(f.Attribute)}: {JsonValue(f, row.Values[f.Key])}"));

  private static string JsonValue(Field field, object value) => field.Type switch {
    ValueType.Integer => ((long)(double)value).ToString(Invariant),
    ValueType.Number => JsonNumber((double)value),
    ValueType.Bool => (string)value == "True" ? "true" : "false",
    _ => Str(value is double d ? d.ToString("R", Invariant) : (string)value)
  };

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

  private static string Number(double d) =>
    double.IsNaN(d) ? "nan" : double.IsPositiveInfinity(d) ? "inf" : double.IsNegativeInfinity(d) ? "-inf"
    : d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(Invariant) : d.ToString("R", Invariant);

  /// <summary>Always a Python float ("20.0", not "20").</summary>
  private static string Float(double d) {
    var text = Number(d);
    return double.IsFinite(d) && !text.Contains('.') && !text.Contains('E') ? text + ".0" : text;
  }

  /// <summary>A float in JSON; NaN and infinities as Python's json writes and reads them.</summary>
  private static string JsonNumber(double d) =>
    double.IsNaN(d) ? "NaN" : double.IsPositiveInfinity(d) ? "Infinity" : double.IsNegativeInfinity(d) ? "-Infinity" : Float(d);

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
        """Its names from the experiment down, e.g. "Experiment / Batch run 1 / Genetic Algorithm"."""
        return self.parent.path + " / " + self.name if self.parent else self.name

    @property
    def label(self) -> str:
        """Its path below the experiment ("Batch run 1 / Genetic Algorithm"); the experiment: its name."""
        names = []
        block: Optional[Block] = self
        while block is not None and block.parent is not None:
            names.insert(0, block.name)
            block = block.parent
        return " / ".join(names) or self.name

    @property
    def made_here(self) -> list[Run]:
        """The runs made in this block itself (not in blocks below it)."""
        return [run for run in self.runs if run.block is self]

    def walk(self) -> Iterator[Block]:
        """This block and every block below it."""
        yield self
        for block in self.blocks:
            yield from block.walk()

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


def _group(block: Block, blocks: tuple[Block, ...]) -> str:
    """A block's name in a comparison; where names repeat (an algorithm in each batch run), its label."""
    return block.name if [b.name for b in blocks].count(block.name) == 1 else block.label


def compare(*blocks: Block) -> Any:
    """Several blocks' runs in one pandas DataFrame; the column "group" says which block."""
    import pandas as pd
    frames = []
    for block in blocks:
        frame = block.df
        frame.insert(0, "group", _group(block, blocks))
        frames.append(frame)
    return pd.concat(frames, ignore_index=True)


def compare_curves(table: str, row: str, *blocks: Block) -> Any:
    """compare() for curves: a table's row of several blocks in long form (group, path, block, run, step, value)."""
    import pandas as pd
    frames = []
    for block in blocks:
        frame = block.curve_df(table, row)
        frame.insert(0, "group", _group(block, blocks))
        frames.append(frame)
    return pd.concat(frames, ignore_index=True)

"""";
}
