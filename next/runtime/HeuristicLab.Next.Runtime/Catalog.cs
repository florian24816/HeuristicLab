using HeuristicLab.Core;
using HeuristicLab.Optimization;
using HeuristicLab.PluginInfrastructure;

namespace HeuristicLab.Next.Runtime;

public sealed record CatalogEntry(string Name, string Description, string TypeName, Type Type) {
  public IItem CreateInstance() => (IItem)Activator.CreateInstance(Type)!;

  /// <summary>HeuristicLab's category (its "New item" tree), e.g. "Algorithms / Population Based"; "Other Items" without one.</summary>
  public string Category => string.Join(" / ", CategoryTokens.Select(t => t.Name));

  /// <summary>Sorts the categories as HeuristicLab's tree does (by their order numbers).</summary>
  public string CategoryOrder => string.Join("/", CategoryTokens.Select(t => t.Order.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + t.Name));

  /// <summary>Order within the category (HeuristicLab's Creatable priority; lower first).</summary>
  public int Priority => CreatableAttribute.GetPriority(Type);

  private IEnumerable<(int Order, string Name)> CategoryTokens =>
    (CreatableAttribute.GetCategory(Type) ?? "Other Items")
      .Split(CreatableAttribute.Categories.SplitToken, StringSplitOptions.RemoveEmptyEntries)
      .Select(t => t.Split(CreatableAttribute.Categories.OrderToken) is [var order, var name] && int.TryParse(order, out var o) ? (o, name) : (9999, t));
}

/// <summary>Algorithms and problems that can be created, as discovered from the loaded plugins.</summary>
public static class Catalog {
  public static IReadOnlyList<CatalogEntry> Algorithms() => Entries(typeof(IAlgorithm));
  public static IReadOnlyList<CatalogEntry> Problems() => Entries(typeof(IProblem));
  /// <summary>Optimizers that are not algorithms: experiments, batch runs, time-limit runs.</summary>
  public static IReadOnlyList<CatalogEntry> MetaOptimizers() =>
    Entries(typeof(IOptimizer)).Where(e => !typeof(IAlgorithm).IsAssignableFrom(e.Type)).ToList();

  /// <summary>Entries in HeuristicLab's order: by category, then priority, then name.</summary>
  public static IReadOnlyList<CatalogEntry> ByCategory(IEnumerable<CatalogEntry> entries) =>
    entries.OrderBy(e => e.CategoryOrder, StringComparer.Ordinal).ThenBy(e => e.Priority)
      .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

  public static CatalogEntry? Find(IEnumerable<CatalogEntry> entries, string nameOrType) =>
    entries.FirstOrDefault(e => string.Equals(e.Name, nameOrType, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(e.TypeName, nameOrType, StringComparison.Ordinal)
                             || string.Equals(e.Type.Name, nameOrType, StringComparison.OrdinalIgnoreCase));

  private static IReadOnlyList<CatalogEntry> Entries(Type baseType) {
    HlRuntime.Initialize();
    return ApplicationManager.Manager.GetTypes(baseType)
      .Where(t => t.IsPublic && t.GetConstructor(Type.EmptyTypes) != null && !t.ContainsGenericParameters)
      .Select(t => new CatalogEntry(ItemAttribute.GetName(t), ItemAttribute.GetDescription(t), t.FullName!, t))
      .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }
}
