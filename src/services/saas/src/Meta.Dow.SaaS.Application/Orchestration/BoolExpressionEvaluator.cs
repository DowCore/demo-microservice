using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Volo.Abp;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 受控布尔表达式求值（非任意 JS）。
/// 支持：input.xxx / sys.xxx / 字面量、比较、&amp;&amp; || ! ()，以及 and / or / not。
/// 示例：<c>input.age &lt; 0 || input.age &gt; 100</c>
/// </summary>
public static class BoolExpressionEvaluator
{
    private static readonly Regex TokenRegex = new(
        @"\s*(?<tok>" +
        @"&&|\|\||==|!=|>=|<=|>|<|" +
        @"\(|\)|!|" +
        @"and\b|or\b|not\b|" +
        @"true\b|false\b|null\b|" +
        @"'(?:\\'|[^'])*'|""(?:\\""|[^""])*""|" +
        @"-?\d+(?:\.\d+)?|" +
        @"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*" +
        @")\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    public static bool Evaluate(string? expression, FlowRuntimeContext ctx)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var tokens = Tokenize(expression);
        if (tokens.Count == 0)
        {
            return false;
        }

        var index = 0;
        var value = ParseOr(tokens, ref index, ctx);
        if (index != tokens.Count)
        {
            throw new UserFriendlyException($"Invalid expression near '{tokens[index]}': {expression}");
        }

        return value;
    }

    public static void ValidateSyntax(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return;
        }

        // dry parse with empty context
        var ctx = new FlowRuntimeContext();
        Evaluate(expression, ctx);
    }

    private static List<string> Tokenize(string expression)
    {
        var list = new List<string>();
        var pos = 0;
        while (pos < expression.Length)
        {
            var m = TokenRegex.Match(expression, pos);
            if (!m.Success || m.Index != pos)
            {
                throw new UserFriendlyException($"Invalid expression syntax near: {expression[pos..]}");
            }

            list.Add(m.Groups["tok"].Value);
            pos = m.Index + m.Length;
        }

        return list;
    }

    private static bool ParseOr(List<string> tokens, ref int index, FlowRuntimeContext ctx)
    {
        var left = ParseAnd(tokens, ref index, ctx);
        while (index < tokens.Count && IsOr(tokens[index]))
        {
            index++;
            left = left || ParseAnd(tokens, ref index, ctx);
        }

        return left;
    }

    private static bool ParseAnd(List<string> tokens, ref int index, FlowRuntimeContext ctx)
    {
        var left = ParseNot(tokens, ref index, ctx);
        while (index < tokens.Count && IsAnd(tokens[index]))
        {
            index++;
            left = left && ParseNot(tokens, ref index, ctx);
        }

        return left;
    }

    private static bool ParseNot(List<string> tokens, ref int index, FlowRuntimeContext ctx)
    {
        if (index < tokens.Count && IsNot(tokens[index]))
        {
            index++;
            return !ParseNot(tokens, ref index, ctx);
        }

        return ParseComparison(tokens, ref index, ctx);
    }

    private static bool ParseComparison(List<string> tokens, ref int index, FlowRuntimeContext ctx)
    {
        var left = ParsePrimary(tokens, ref index, ctx);
        if (index >= tokens.Count || !IsCompareOp(tokens[index]))
        {
            return IsTruthy(left);
        }

        var op = tokens[index++];
        var right = ParsePrimary(tokens, ref index, ctx);
        return Compare(left, right, op);
    }

    private static object? ParsePrimary(List<string> tokens, ref int index, FlowRuntimeContext ctx)
    {
        if (index >= tokens.Count)
        {
            throw new UserFriendlyException("Unexpected end of expression.");
        }

        var tok = tokens[index++];
        if (tok == "(")
        {
            var value = ParseOr(tokens, ref index, ctx);
            if (index >= tokens.Count || tokens[index] != ")")
            {
                throw new UserFriendlyException("Missing ')' in expression.");
            }

            index++;
            return value;
        }

        if (tok.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (tok.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (tok.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if ((tok.StartsWith('\'') && tok.EndsWith('\'')) || (tok.StartsWith('"') && tok.EndsWith('"')))
        {
            return Unescape(tok[1..^1]);
        }

        if (decimal.TryParse(tok, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        // path: input.age / sys.userId / age (legacy → vars/input)
        return FlowContextResolver.ResolvePath(NormalizePath(tok), ctx);
    }

    private static string NormalizePath(string tok)
    {
        if (tok.StartsWith("input.", StringComparison.OrdinalIgnoreCase) ||
            tok.StartsWith("sys.", StringComparison.OrdinalIgnoreCase) ||
            tok.StartsWith("vars.", StringComparison.OrdinalIgnoreCase) ||
            tok.StartsWith("results.", StringComparison.OrdinalIgnoreCase))
        {
            return tok;
        }

        // bare field → input.field
        return "input." + tok;
    }

    private static string Unescape(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                sb.Append(s[++i]);
            }
            else
            {
                sb.Append(s[i]);
            }
        }

        return sb.ToString();
    }

    private static bool IsOr(string tok) =>
        tok is "||" || tok.Equals("or", StringComparison.OrdinalIgnoreCase);

    private static bool IsAnd(string tok) =>
        tok is "&&" || tok.Equals("and", StringComparison.OrdinalIgnoreCase);

    private static bool IsNot(string tok) =>
        tok is "!" || tok.Equals("not", StringComparison.OrdinalIgnoreCase);

    private static bool IsCompareOp(string tok) =>
        tok is "==" or "!=" or ">=" or "<=" or ">" or "<";

    private static bool IsTruthy(object? value) =>
        value switch
        {
            null => false,
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s) && !s.Equals("false", StringComparison.OrdinalIgnoreCase),
            IConvertible c => Convert.ToDecimal(c, CultureInfo.InvariantCulture) != 0,
            _ => true
        };

    private static bool Compare(object? left, object? right, string op)
    {
        if (left is IConvertible && right is IConvertible &&
            decimal.TryParse(Convert.ToString(left, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var ln) &&
            decimal.TryParse(Convert.ToString(right, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var rn))
        {
            return op switch
            {
                ">" => ln > rn,
                "<" => ln < rn,
                ">=" => ln >= rn,
                "<=" => ln <= rn,
                "==" => ln == rn,
                "!=" => ln != rn,
                _ => false
            };
        }

        var ls = Convert.ToString(left, CultureInfo.InvariantCulture) ?? "";
        var rs = Convert.ToString(right, CultureInfo.InvariantCulture) ?? "";
        return op switch
        {
            "==" => string.Equals(ls, rs, StringComparison.OrdinalIgnoreCase),
            "!=" => !string.Equals(ls, rs, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
