using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Volo.Abp;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 求值编号条件组合式，如 <c>1 and (2 or 3)</c>、<c>not 4 or (1 and 2)</c>。
/// </summary>
public static class ConditionCombineEvaluator
{
    private static readonly Regex TokenRegex = new(
        @"\s*(?<tok>and|or|not|\(|\)|\d+)\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    public static bool Evaluate(string? combine, IReadOnlyDictionary<int, bool> itemResults)
    {
        if (string.IsNullOrWhiteSpace(combine))
        {
            return true;
        }

        var tokens = Tokenize(combine);
        if (tokens.Count == 0)
        {
            return true;
        }

        var index = 0;
        var value = ParseOr(tokens, ref index, itemResults);
        if (index != tokens.Count)
        {
            throw new UserFriendlyException($"Invalid combine expression near '{tokens[index]}': {combine}");
        }

        return value;
    }

    public static void Validate(string? combine, IEnumerable<int> availableNos)
    {
        if (string.IsNullOrWhiteSpace(combine))
        {
            return;
        }

        var nos = availableNos.ToHashSet();
        foreach (var tok in Tokenize(combine))
        {
            if (int.TryParse(tok, out var no) && !nos.Contains(no))
            {
                throw new UserFriendlyException($"Combine references missing condition no {no}: {combine}");
            }
        }

        // parse to ensure syntax
        var results = nos.ToDictionary(x => x, _ => true);
        Evaluate(combine, results);
    }

    private static List<string> Tokenize(string combine)
    {
        var list = new List<string>();
        var pos = 0;
        while (pos < combine.Length)
        {
            var m = TokenRegex.Match(combine, pos);
            if (!m.Success || m.Index != pos)
            {
                throw new UserFriendlyException($"Invalid combine expression: {combine}");
            }

            list.Add(m.Groups["tok"].Value.ToLowerInvariant());
            pos = m.Index + m.Length;
        }

        return list;
    }

    private static bool ParseOr(List<string> tokens, ref int index, IReadOnlyDictionary<int, bool> items)
    {
        var left = ParseAnd(tokens, ref index, items);
        while (index < tokens.Count && tokens[index] == "or")
        {
            index++;
            left = left | ParseAnd(tokens, ref index, items);
        }

        return left;
    }

    private static bool ParseAnd(List<string> tokens, ref int index, IReadOnlyDictionary<int, bool> items)
    {
        var left = ParseUnary(tokens, ref index, items);
        while (index < tokens.Count && tokens[index] == "and")
        {
            index++;
            left = left & ParseUnary(tokens, ref index, items);
        }

        return left;
    }

    private static bool ParseUnary(List<string> tokens, ref int index, IReadOnlyDictionary<int, bool> items)
    {
        if (index >= tokens.Count)
        {
            throw new UserFriendlyException("Unexpected end of combine expression.");
        }

        if (tokens[index] == "not")
        {
            index++;
            return !ParseUnary(tokens, ref index, items);
        }

        if (tokens[index] == "(")
        {
            index++;
            var value = ParseOr(tokens, ref index, items);
            if (index >= tokens.Count || tokens[index] != ")")
            {
                throw new UserFriendlyException("Missing ')' in combine expression.");
            }

            index++;
            return value;
        }

        if (int.TryParse(tokens[index], out var no))
        {
            index++;
            if (!items.TryGetValue(no, out var result))
            {
                throw new UserFriendlyException($"Condition no {no} not found.");
            }

            return result;
        }

        throw new UserFriendlyException($"Unexpected token '{tokens[index]}' in combine expression.");
    }
}
