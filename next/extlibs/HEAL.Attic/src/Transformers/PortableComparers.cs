using System;

namespace HEAL.Attic {
  /// <summary>
  /// heuristiclab-next patch: .NET 5+ gives Dictionary&lt;string, T&gt; / HashSet&lt;string&gt; a default
  /// comparer type that .NET Framework does not have (StringEqualityComparer). Written as the
  /// comparer .NET Framework uses for strings (GenericEqualityComparer&lt;string&gt;, field-less like
  /// it), files saved on modern .NET open in HeuristicLab 3.3. Reading is unaffected.
  /// </summary>
  internal static class PortableComparers {
    private static readonly Type ModernStringComparer = Type.GetType("System.Collections.Generic.StringEqualityComparer");
    private static readonly Type GenericStringComparer = Type.GetType("System.Collections.Generic.GenericEqualityComparer`1")?.MakeGenericType(typeof(string));

    public static Type TypeToWrite(Type comparerType) =>
      comparerType == ModernStringComparer && ModernStringComparer != null && GenericStringComparer != null ? GenericStringComparer : comparerType;
  }
}
