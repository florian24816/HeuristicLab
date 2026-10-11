using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HeuristicLab.Core;
using HeuristicLab.Next.Runtime;
using HeuristicLab.Optimization;

namespace HeuristicLab.Studio.ViewModels;

/// <summary>
/// Names an algorithm after the settings that make it different (Shift+Enter while editing a field
/// across algorithms): "Local Search (LS)" with MaximumIterations = 300 becomes "LS_maxIt300", a
/// later move generator adds "_MG-StochInvMultiMG". A name of its own is kept in front
/// ("Randomname_LS_maxIt300"). A name already in this form gets the setting added, or its tag
/// replaced if that parameter is already named.
/// <para>
/// Tags: a value parameter is its words, each shortened to its first syllable, in lower camel case
/// followed by the value (maxIt300, setSeedRandFalse); an operator choice is the initials of its
/// words (MG; one word: its first syllable) and, after '-', the chosen operator shortened the same
/// way, its ending that repeats the parameter's name written as the parameter's tag (StochInvMultiMG).
/// </para>
/// </summary>
public static partial class AutoName {
  [GeneratedRegex(@"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|\d+(?:\.\d+)?")]
  private static partial Regex WordPattern();

  [GeneratedRegex(@"^(.*?)(?: \(copy \d+\))+$")]
  private static partial Regex CopySuffix();

  [GeneratedRegex(@"\(([^()]+)\)\s*$")]
  private static partial Regex Parenthesized();

  public static IReadOnlyList<string> Words(string name) => WordPattern().Matches(name).Select(m => m.Value).ToList();

  private static bool IsVowel(char c, int index) => "aeiouAEIOU".Contains(c) || (index > 0 && c is 'y' or 'Y');

  /// <summary>A word's first syllable ("Maximum" -> "Max", "Iterations" -> "It", "Stochastic" -> "Stoch"); short words, acronyms and numbers stay.</summary>
  public static string Shorten(string word) {
    if (word.Length <= 5 || word.All(c => !char.IsLower(c))) return word;
    int i = 0;
    while (i < word.Length && !IsVowel(word[i], i)) i++;
    while (i < word.Length && IsVowel(word[i], i)) i++;
    while (i < word.Length && !IsVowel(word[i], i)) i++;
    return i >= 2 && i < word.Length ? word[..i] : word;
  }

  private static string Capitalized(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

  /// <summary>Tag of a parameter: "MaximumIterations" -> "maxIt", the choice "MoveGenerator" -> "MG", the choice "Selector" -> "sel".</summary>
  public static string ParameterTag(string parameterName, bool choice) {
    var words = Words(parameterName);
    if (words.Count == 0) return Clean(parameterName);
    if (choice && words.Count >= 2) return string.Concat(words.Select(w => char.ToUpperInvariant(w[0])));
    var first = Shorten(words[0]);
    first = first.All(c => !char.IsLower(c)) ? first.ToLowerInvariant() : char.ToLowerInvariant(first[0]) + first[1..];
    return first + string.Concat(words.Skip(1).Select(w => Capitalized(Shorten(w))));
  }

  /// <summary>
  /// Tag of a chosen operator: "StochasticInversionMultiMoveGenerator" for the parameter
  /// "MoveGenerator" (tag "MG") -> "StochInvMultiMG".
  /// </summary>
  public static string ChoiceTag(string valueName, string parameterName) {
    var words = Words(valueName).ToList();
    var parameterWords = Words(parameterName);
    string ending = "";
    if (parameterWords.Count > 0 && words.Count > parameterWords.Count
        && words.Skip(words.Count - parameterWords.Count).Select(w => w.ToLowerInvariant()).SequenceEqual(parameterWords.Select(w => w.ToLowerInvariant()))) {
      words.RemoveRange(words.Count - parameterWords.Count, parameterWords.Count);
      ending = Capitalized(ParameterTag(parameterName, choice: true));
    }
    return string.Concat(words.Select(w => Capitalized(Shorten(w)))) + ending;
  }

  /// <summary>The algorithm's short name: what its name has in parentheses ("Local Search (LS)" -> "LS"), else its initials.</summary>
  public static string AlgorithmTag(IAlgorithm algorithm) {
    var name = algorithm.ItemName;
    if (Parenthesized().Match(name) is { Success: true } m) return Clean(m.Groups[1].Value);
    if (!name.Contains(' ') && name.Length <= 10) return Clean(name);
    var words = Words(name);
    return words.Count == 0 ? Clean(name) : string.Concat(words.Select(w => w.All(c => !char.IsLower(c)) ? w : w[0].ToString().ToUpperInvariant()));
  }

  /// <summary>No '_' (it separates the tags) and no spaces.</summary>
  private static string Clean(string text) => new string(text.Where(c => !char.IsWhiteSpace(c)).Select(c => c == '_' ? '-' : c).ToArray());

  /// <summary>The tag naming this field's current value, e.g. "maxIt300" or "MG-StochInvMultiMG"; null if the algorithm has no such field.</summary>
  public static string? FieldTag(IAlgorithm algorithm, string key) {
    if (MultiEdit.ParameterAt(algorithm, key) is not { } parameter) return null;
    bool choice = MultiEdit.IsChoice(key);
    var tag = ParameterTag(parameter.Name, choice);
    var value = ItemInspector.Value(parameter);
    if (choice) {
      var type = value?.GetType().Name ?? "None";
      int tick = type.IndexOf('`');
      return tag + "-" + ChoiceTag(tick < 0 ? type : type[..tick], parameter.Name);
    }
    // a value never starts with a lower-case letter, so the parameter's tag can be found again in front of it
    return tag + Capitalized(Clean(ItemInspector.Summary(value)));
  }

  /// <summary>The new name of the algorithm once this field's value is part of it.</summary>
  public static string Apply(string currentName, IAlgorithm algorithm, string key) {
    var tag = FieldTag(algorithm, key);
    if (tag == null) return currentName;
    var core = currentName.Trim();
    if (CopySuffix().Match(core) is { Success: true } copy) core = copy.Groups[1].Value;
    var algorithmTag = AlgorithmTag(algorithm);
    var tokens = core.Length == 0 ? [] : core.Split('_').ToList();
    int at = tokens.IndexOf(algorithmTag);
    List<string> prefix, tags;
    if (at >= 0) {
      prefix = tokens.Take(at + 1).ToList();
      tags = tokens.Skip(at + 1).ToList();
    } else {
      bool unnamed = core.Length == 0 || core == algorithm.ItemName;
      prefix = unnamed ? [algorithmTag] : [.. tokens, algorithmTag];
      tags = [];
    }
    // a parameter named before: its tag is replaced (the longest parameter tag a token starts with decides)
    var parameter = MultiEdit.ParameterAt(algorithm, key)!;
    var own = ParameterTag(parameter.Name, MultiEdit.IsChoice(key));
    var known = KnownTags(algorithm).Append(own).Distinct().ToList();
    int index = tags.FindIndex(t => OwnerOf(t, known) == own);
    if (index >= 0) tags[index] = tag;
    else tags.Add(tag);
    return string.Join("_", prefix.Concat(tags));
  }

  private static IEnumerable<string> KnownTags(IAlgorithm algorithm) =>
    ItemInspector.Parameters(algorithm).SelectMany(p => new[] { ParameterTag(p.Name, false), ParameterTag(p.Name, true) });

  /// <summary>Which parameter tag a name tag starts with: the longest one followed by its value (a choice after '-', a value not by more lower-case letters).</summary>
  private static string? OwnerOf(string token, IEnumerable<string> known) =>
    known.Where(k => token.Length > k.Length && token.StartsWith(k, StringComparison.Ordinal) && !char.IsLower(token[k.Length]))
      .OrderByDescending(k => k.Length).FirstOrDefault();
}
