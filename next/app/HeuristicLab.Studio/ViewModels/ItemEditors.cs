using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeuristicLab.Core;
using HeuristicLab.Data;
using HeuristicLab.Next.Runtime;

namespace HeuristicLab.Studio.ViewModels;

/// <summary>Context shared by nested editors: where to report errors and whether editing is allowed now.</summary>
public sealed class EditContext(Action<string> report, Func<bool> canEdit, Action? changed = null) {
  public void Report(string message) => report(message);
  public bool CanEdit => canEdit();

  /// <summary>Key of the field edited across algorithms (MultiEdit), if any, in this editor's algorithm.</summary>
  public Func<string?>? MultiTarget { get; init; }

  /// <summary>Shift+click on a field with this key: start or end editing it across algorithms.</summary>
  public Func<string, bool>? ToggleMulti { get; init; }

  public bool IsMultiTarget(string? key) => key != null && MultiTarget?.Invoke() == key;

  /// <summary>The same context without editing (read-only views of a value).</summary>
  public EditContext ReadOnly() => new(report, () => false);

  /// <summary>An edit is about to happen if allowed: true and the owner learns that its content changes (e.g. "unsaved").</summary>
  public bool BeginEdit() {
    if (!canEdit()) return false;
    changed?.Invoke();
    return true;
  }
}

/// <summary>
/// Parameters of an item: a list (name, short value) and the editor of the selected parameter.
/// Nested items (the chosen move generator, an analyzer, an operator) use the same view, stacked.
/// </summary>
public partial class ParameterListViewModel : ViewModelBase {
  /// <param name="path">Address of the item for editing across algorithms (MultiEdit); null: not addressable.</param>
  public ParameterListViewModel(IParameterizedItem item, EditContext context, bool nested = false, string? path = null) {
    Item = item;
    Nested = nested;
    Path = path;
    Parameters = ItemInspector.Parameters(item)
      .Select(p => new ParameterNodeViewModel(p, context, this, path == null ? null : MultiEdit.Parameter(path, p.Name))).ToList();
    Selected = Parameters.FirstOrDefault();
  }

  public IParameterizedItem Item { get; }
  public bool Nested { get; }
  public string? Path { get; }
  public IReadOnlyList<ParameterNodeViewModel> Parameters { get; }
  public bool IsEmpty => Parameters.Count == 0;

  /// <summary>Top level: list left, editor right. Nested: list above, editor below.</summary>
  public Dock ListDock => Nested ? Dock.Top : Dock.Left;
  public double ListWidth => Nested ? double.NaN : 300;
  public double ListMaxHeight => Nested ? 220 : double.PositiveInfinity;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(SelectedText))]
  public partial ParameterNodeViewModel? Selected { get; set; }

  /// <summary>Between a nested list and the editor below it: what the editor shows.</summary>
  public string SelectedText => Selected == null ? "" : $"{Selected.Name} selected. Details/Options:";

  public void RefreshSummaries() { foreach (var p in Parameters) p.RefreshSummary(); }

  /// <summary>Editing became possible or impossible (the algorithm started or stopped): pictograms of all shown levels.</summary>
  public void RefreshEditability() { foreach (var p in Parameters) p.RefreshEditability(); }
}

public sealed record ChoiceItem(string Display, IItem Item);

/// <summary>One parameter: type, "Show in Run", value choice (for selections) and the value editor.</summary>
public partial class ParameterNodeViewModel : ViewModelBase, IMultiField {
  private readonly EditContext context;
  private readonly IParameter parameter;
  private ValueEditorViewModel? editor;

  public ParameterNodeViewModel(IParameter parameter, EditContext context, ParameterListViewModel owner, string? path = null) {
    this.parameter = parameter;
    this.context = context;
    Owner = owner;
    Path = path;
    Choices = ItemInspector.Choices(parameter)?.Select(v => new ChoiceItem(v is INamedItem n ? n.Name : v.ItemName, v)).ToList() ?? [];
  }

  public ParameterListViewModel Owner { get; }
  public string Name => parameter.Name;

  /// <summary>Address of this parameter for editing across algorithms; null: not addressable.</summary>
  public string? Path { get; }
  public string? ChoiceKey => Path == null ? null : Path + MultiEdit.ChoiceField;

  /// <summary>The choice is the field edited across algorithms (shown purple).</summary>
  public bool IsMultiTarget => context.IsMultiTarget(ChoiceKey);

  public bool ToggleMulti() => IsChoice && ChoiceKey != null && context.ToggleMulti?.Invoke(ChoiceKey) == true;

  /// <summary>
  /// Pictogram of the parameter's kind: a choice among given values (constrained), a value that may
  /// be empty (optional), or a value (value, fixed value, value lookup).
  /// </summary>
  public string KindPictogram {
    get {
      string type = parameter.GetType().Name;
      string kind = type.Contains("ConstrainedValueParameter", StringComparison.Ordinal) ? "constrained_value_parameter"
        : type.StartsWith("OptionalValueParameter", StringComparison.Ordinal) ? "optional_value_parameter"
        : "value_parameter";
      // value and choice parameters that cannot be changed now (e.g. while the algorithm runs)
      return kind != "optional_value_parameter" && !context.CanEdit ? "parameter_currently_not_editable" : kind;
    }
  }

  public string KindTip => KindPictogram switch {
    "constrained_value_parameter" => $"{ItemInspector.TypeName(parameter.GetType())}: one of the offered values",
    "optional_value_parameter" => $"{ItemInspector.TypeName(parameter.GetType())}: may have no value",
    "parameter_currently_not_editable" => $"{ItemInspector.TypeName(parameter.GetType())}: cannot be changed now (while it runs)",
    _ => ItemInspector.TypeName(parameter.GetType())
  };
  public string Description => parameter.Description;
  public string DataType => ItemInspector.TypeName(parameter.DataType) +
    (ItemInspector.Value(parameter) is IItem v && v.GetType() != parameter.DataType ? $" ({ItemInspector.TypeName(v.GetType())})" : "");
  public string Summary => ItemInspector.Summary(ItemInspector.Value(parameter) ?? (parameter is IValueParameter ? null : parameter.ActualValue));

  /// <summary>Whether the value is recorded in each run ("Show in Run").</summary>
  public bool HasShowInRun => parameter is IValueParameter;
  public bool ShowInRun {
    get => (parameter as IValueParameter)?.GetsCollected ?? false;
    set {
      if (parameter is IValueParameter vp && context.BeginEdit()) vp.GetsCollected = value;
      OnPropertyChanged();
    }
  }

  public IReadOnlyList<ChoiceItem> Choices { get; }
  public bool IsChoice => Choices.Count > 0;

  public ChoiceItem? SelectedChoice {
    get => Choices.FirstOrDefault(c => ReferenceEquals(c.Item, ItemInspector.Value(parameter)));
    set {
      if (value == null || parameter is not IValueParameter vp || ReferenceEquals(vp.Value, value.Item)) return;
      if (!context.BeginEdit()) { OnPropertyChanged(); return; }
      vp.Value = value.Item;
      ValueChanged();
    }
  }

  /// <summary>Editor of the current value (built when first shown).</summary>
  public ValueEditorViewModel Editor => editor ??= new ValueEditorViewModel(ItemInspector.Value(parameter), context, ValueEdited, path: Path);

  /// <summary>Optional data (e.g. a best known solution) can be cleared; operators cannot.</summary>
  public bool CanClear => parameter is IValueParameter { Value: not null and not IOperator } && !IsChoice && parameter is not IFixedValueParameter;

  [RelayCommand]
  private void Clear() {
    if (parameter is not IValueParameter vp || !context.BeginEdit()) return;
    try {
      vp.Value = null;
    } catch (Exception e) when (e is ArgumentException or InvalidOperationException) {
      context.Report($"{Name} cannot be cleared: {e.Message}");
    }
    ValueChanged();
  }

  private void ValueEdited() => Owner.RefreshSummaries();

  private void ValueChanged() {
    editor = null;
    OnPropertyChanged(nameof(Editor));
    OnPropertyChanged(nameof(SelectedChoice));
    OnPropertyChanged(nameof(DataType));
    OnPropertyChanged(nameof(CanClear));
    Owner.RefreshSummaries();
  }

  public void RefreshSummary() {
    OnPropertyChanged(nameof(Summary));
    OnPropertyChanged(nameof(KindPictogram));
    OnPropertyChanged(nameof(KindTip));
  }

  /// <summary>After editing became possible or impossible: this parameter and the nested ones already shown.</summary>
  public void RefreshEditability() {
    RefreshSummary();
    editor?.RefreshEditability();
  }
}

/// <summary>A row of an array (one cell) or matrix (one cell per column).</summary>
public sealed class CellRowViewModel(int index, IReadOnlyList<CellViewModel> cells) {
  public int Index { get; } = index;
  public IReadOnlyList<CellViewModel> Cells { get; } = cells;
}

public partial class CellViewModel(IItem value, int row, int column, string text, EditContext context, Action edited) : ViewModelBase {
  private string text = text;
  public string Text {
    get => text;
    set {
      if (value == text) return;
      var error = context.BeginEdit() ? ItemInspector.SetCell(valueItem, row, column, value) : "Not editable now.";
      if (error == null) { text = value; edited(); }
      else context.Report($"Cell [{row}, {column}]: {error}");
      OnPropertyChanged();
    }
  }
  private readonly IItem valueItem = value;
}

/// <summary>One item of a checked list (e.g. an analyzer): enabled or not, with its own parameters.</summary>
public partial class CheckedEntryViewModel(CheckedList list, int index, EditContext context, string? listPath = null) : ViewModelBase {
  public int Index { get; } = index;
  /// <summary>Position in the execution order, from 1.</summary>
  public string Number => $"{Index + 1}.";
  public string Name => list[Index] is INamedItem n ? n.Name : list[Index].ItemName;
  public string Kind => list[Index].ItemName;

  public bool IsChecked {
    get => list.IsChecked(Index);
    set {
      if (context.BeginEdit()) list.SetChecked(Index, value);
      OnPropertyChanged();
    }
  }

  private ParameterListViewModel? parameters;
  private bool parametersBuilt;
  internal ParameterListViewModel? BuiltParameters => parameters;

  /// <summary>The operator's own options (built once, so a selection inside them stays).</summary>
  public ParameterListViewModel? Parameters {
    get {
      if (!parametersBuilt) {
        parametersBuilt = true;
        parameters = list[Index] is IParameterizedItem p && p.Parameters.Any(x => !x.Hidden)
          ? new ParameterListViewModel(p, context, nested: true, listPath == null ? null : MultiEdit.Entry(listPath, Name)) : null;
      }
      return parameters;
    }
  }
}

/// <summary>
/// Editor of one value by kind: checkbox, text, table (arrays and matrices), checked list with
/// order (analyzers), or the parameters of a nested item; anything else read-only text.
/// </summary>
public partial class ValueEditorViewModel : ViewModelBase, IMultiField {
  // wider matrices (e.g. a 130x130 distance matrix) are shown as text rows
  private const int MaxEditableColumns = 40;
  private readonly EditContext context;
  private readonly Action edited;
  private readonly CheckedList? checkedList;

  public ValueEditorViewModel(IItem? value, EditContext context, Action? edited = null, bool readOnly = false, string? path = null) {
    Value = value;
    Path = path;
    this.context = context;
    this.edited = edited ?? (() => { });
    ReadOnly = readOnly;
    Kind = ItemInspector.KindOf(value);
    if (Kind == ValueKind.CheckedList) {
      checkedList = CheckedList.TryCreate(value)!;
      AddChoices = checkedList.AddableTypes().Select(t => new MenuChoice(t.Name, new RelayCommand(() => Add(t)))).ToList();
      RebuildEntries();
    }
  }

  public IItem? Value { get; }
  /// <summary>Address of the value for editing across algorithms; null: not addressable.</summary>
  public string? Path { get; }
  public string? ValueKey => Path == null || ReadOnly || Kind is not (ValueKind.Text or ValueKind.Bool) ? null : Path + MultiEdit.ValueField;

  /// <summary>The value (text or checkbox) is the field edited across algorithms (shown purple).</summary>
  public bool IsMultiTarget => context.IsMultiTarget(ValueKey);

  public bool ToggleMulti() => ValueKey != null && context.ToggleMulti?.Invoke(ValueKey) == true;
  public ValueKind Kind { get; }
  public bool ReadOnly { get; }
  public bool IsNone => Kind == ValueKind.None;
  public bool IsBool => Kind == ValueKind.Bool;
  public bool IsText => Kind == ValueKind.Text;
  public bool IsTable => Kind is ValueKind.Array or ValueKind.Matrix;
  public bool IsCheckedList => Kind == ValueKind.CheckedList;
  public bool IsOther => Kind == ValueKind.Other;

  /// <summary>Name of the item (operators and other named items).</summary>
  public string? ObjectName => Value is INamedItem n ? n.Name : null;
  public string TypeName => Value?.ItemName ?? "";

  /// <summary>Operators can pause the engine before they execute.</summary>
  public bool HasBreakpoint => Value is IOperator;
  public bool Breakpoint {
    get => (Value as IOperator)?.Breakpoint ?? false;
    set {
      if (Value is IOperator op && context.BeginEdit()) op.Breakpoint = value;
      OnPropertyChanged();
    }
  }

  public bool Bool {
    get => (Value as BoolValue)?.Value ?? false;
    set {
      if (Value is BoolValue b && !ReadOnly && !b.ReadOnly && context.BeginEdit()) { b.Value = value; edited(); }
      OnPropertyChanged();
    }
  }

  public string Text {
    get => Value is IStringConvertibleValue v ? v.GetValue() : Value?.ToString() ?? "";
    set {
      if (Value is null || value == Text) return;
      var error = ReadOnly || !context.BeginEdit() ? "Not editable now." : ItemInspector.SetText(Value, value);
      if (error != null) context.Report(error); else edited();
      OnPropertyChanged();
    }
  }

  public string TableInfo => Value switch {
    IStringConvertibleArray a => $"Length: {a.Length}",
    IStringConvertibleMatrix m => $"Rows: {m.Rows}, columns: {m.Columns}" + (m.Columns > MaxEditableColumns ? " (shown as text)" : ""),
    _ => ""
  };

  /// <summary>Extra facts, e.g. a permutation's type (relative undirected, absolute, ...).</summary>
  public string? Details => Value?.GetType().GetProperty("PermutationType")?.GetValue(Value) is object type ? $"Type: {type}" : null;

  public IReadOnlyList<CellRowViewModel> Rows => rows ??= BuildRows();
  private IReadOnlyList<CellRowViewModel>? rows;

  public IReadOnlyList<string> TextRows => Value is IStringConvertibleMatrix { Columns: > MaxEditableColumns } m
    ? ItemInspector.Cells(m).Select((r, i) => $"{i}: {string.Join("  ", r)}").ToList() : [];

  public bool IsWideMatrix => TextRows.Count > 0;
  public bool IsGrid => IsTable && !IsWideMatrix;

  private IReadOnlyList<CellRowViewModel> BuildRows() {
    bool editable = !ReadOnly;
    void Edited() => edited();
    var item = Value!;
    switch (Value) {
      case IStringConvertibleArray a:
        return ItemInspector.Cells(a).Select((t, i) => new CellRowViewModel(i, [new CellViewModel(item, i, 0, t, Guard(editable), Edited)])).ToList();
      case IStringConvertibleMatrix { Columns: <= MaxEditableColumns } m:
        return ItemInspector.Cells(m).Select((r, i) => new CellRowViewModel(i, r.Select((t, c) => new CellViewModel(item, i, c, t, Guard(editable), Edited)).ToList())).ToList();
      default:
        return [];
    }
  }

  private EditContext Guard(bool editable) => editable ? context : context.ReadOnly();

  // ---- checked lists (analyzers)

  public const int OperatorsTab = 0, ParametersTab = 1;

  /// <summary>Tab of a checked list's editor: its operators, or the parameters of the list's owner.</summary>
  [ObservableProperty]
  public partial int CheckedListTab { get; set; }

  public ObservableCollection<CheckedEntryViewModel> Entries { get; } = [];
  public IReadOnlyList<MenuChoice> AddChoices { get; } = [];

  [ObservableProperty]
  [NotifyCanExecuteChangedFor(nameof(MoveUpCommand), nameof(MoveDownCommand), nameof(RemoveCommand))]
  [NotifyPropertyChangedFor(nameof(SelectedEntryText))]
  public partial CheckedEntryViewModel? SelectedEntry { get; set; }

  /// <summary>Between the list and the selected operator's options.</summary>
  public string SelectedEntryText => SelectedEntry == null ? ""
    : SelectedEntry.Parameters == null ? $"{SelectedEntry.Name} selected. It has no options."
    : $"{SelectedEntry.Name} selected. Details/Options:";

  private void RebuildEntries(int? select = null) {
    Entries.Clear();
    for (int i = 0; i < checkedList!.Count; i++) Entries.Add(new CheckedEntryViewModel(checkedList, i, context, Path));
    SelectedEntry = select is int s && s >= 0 && s < Entries.Count ? Entries[s] : Entries.FirstOrDefault();
    edited();
  }

  [RelayCommand(CanExecute = nameof(CanMoveUp))]
  private void MoveUp() { if (!context.BeginEdit()) return; int i = SelectedEntry!.Index; checkedList!.Move(i, i - 1); RebuildEntries(i - 1); }
  private bool CanMoveUp() => !ReadOnly && SelectedEntry is { Index: > 0 };

  [RelayCommand(CanExecute = nameof(CanMoveDown))]
  private void MoveDown() { if (!context.BeginEdit()) return; int i = SelectedEntry!.Index; checkedList!.Move(i, i + 1); RebuildEntries(i + 1); }
  private bool CanMoveDown() => !ReadOnly && SelectedEntry != null && SelectedEntry.Index < Entries.Count - 1;

  [RelayCommand(CanExecute = nameof(CanRemove))]
  private void Remove() {
    if (!context.BeginEdit()) return;
    int i = SelectedEntry!.Index;
    checkedList!.RemoveAt(i);
    RebuildEntries(Math.Min(i, checkedList.Count - 1));
  }
  private bool CanRemove() => !ReadOnly && SelectedEntry != null;

  private void Add(CatalogEntry type) {
    if (ReadOnly || !context.BeginEdit()) return;
    try {
      checkedList!.Add(type);
      RebuildEntries(checkedList.Count - 1);
    } catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.Reflection.TargetInvocationException) {
      context.Report($"Cannot add {type.Name}: {e.GetBaseException().Message}");
    }
  }

  // ---- nested items

  /// <summary>Parameters of the value itself (move generator, MultiAnalyzer, ...), if it has any.</summary>
  public ParameterListViewModel? Nested => nested ??= Value is IParameterizedItem p && p.Parameters.Any(x => !x.Hidden)
    ? new ParameterListViewModel(p, ReadOnly ? context.ReadOnly() : context, nested: true, ReadOnly ? null : Path) : null;
  private ParameterListViewModel? nested;
  public bool HasNested => Nested != null;

  public void RefreshEditability() {
    nested?.RefreshEditability();
    foreach (var entry in Entries) entry.BuiltParameters?.RefreshEditability();
  }
}
