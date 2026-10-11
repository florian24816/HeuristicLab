using System;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace HeuristicLab.Studio.ViewModels;

/// <summary>
/// A row of a categorized list (algorithms, problems): tinted with its category's color, the first
/// row of a category carries the category's name as heading. Value is what the row stands for.
/// </summary>
public sealed record CatalogItem(string Display, string Description, string Category, IBrush Tint, IBrush Accent, bool StartsGroup, object? Value) {
  public bool HasDescription => Description.Length > 0;

  /// <summary>Rows in the given order, a heading wherever the category changes.</summary>
  public static IReadOnlyList<CatalogItem> Group(IEnumerable<(string Display, string Description, string Category, object? Value)> rows) {
    var items = new List<CatalogItem>();
    string? previous = null;
    foreach (var (display, description, category, value) in rows) {
      var (tint, accent) = CategoryColors.For(category);
      items.Add(new CatalogItem(display, description, category, tint, accent, category != previous, value));
      previous = category;
    }
    return items;
  }
}

/// <summary>A light tint and a matching dark accent (heading) per HeuristicLab category; kept away from the logo orange.</summary>
public static class CategoryColors {
  private static readonly Dictionary<string, (string Tint, string Accent)> Known = new(StringComparer.Ordinal) {
    ["Algorithms"] = ("#E7E9F0", "#4F5870"),
    ["Algorithms / Population Based"] = ("#DDEFD9", "#357233"),
    ["Algorithms / Single Solution"] = ("#DAE6F6", "#28609F"),
    ["Algorithms / Exact"] = ("#EAE0F4", "#664392"),
    ["Data Analysis"] = ("#DCEFEE", "#256F6B"),
    ["Data Analysis / Regression"] = ("#D5ECF1", "#1C6A78"),
    ["Data Analysis / Classification"] = ("#FAEFCC", "#8A6810"),
    ["Data Analysis / Ensembles"] = ("#F1E4D6", "#7F5630"),
    ["Problems"] = ("#ECEAE3", "#625D50"),
    ["Problems / Combinatorial"] = ("#F7DEDB", "#9C3B33"),
    ["Problems / Genetic Programming"] = ("#F3DDEA", "#8E3966"),
    ["Problems / External Evaluation"] = ("#DFE5EE", "#46566E"),
    ["Testing & Analysis"] = ("#E4EDE0", "#4D6542"),
    ["Other Items"] = ("#EDEDED", "#5E5E5E"),
  };
  private static readonly (string Tint, string Accent)[] Spare = [("#E2ECF3", "#3B6585"), ("#EFE8DA", "#6F5A33"), ("#E9E2EE", "#5F4A72")];
  private static readonly Dictionary<string, (IBrush, IBrush)> brushes = [];

  public static (IBrush Tint, IBrush Accent) For(string category) {
    lock (brushes) {
      if (brushes.TryGetValue(category, out var pair)) return pair;
      var (tint, accent) = Known.TryGetValue(category, out var known) ? known : Spare[(uint)StringComparer.Ordinal.GetHashCode(category) % Spare.Length];
      return brushes[category] = (new ImmutableSolidColorBrush(Color.Parse(tint)), new ImmutableSolidColorBrush(Color.Parse(accent)));
    }
  }
}
