using System.Text.RegularExpressions;
using Volo.Abp;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 轻量静态检查：db.query/execute/batch 的 SQL 须为静态字符串/模板字面量；
/// 未绑定数据源时禁止 db。
/// </summary>
public static partial class CodeScriptStaticAnalyzer
{
    [GeneratedRegex(@"\bdb\s*\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DbMemberRegex();

    [GeneratedRegex(
        @"\bdb\s*\.\s*(?<api>query|execute|batch)\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex DbSqlCallRegex();

    public static bool UsesDbApi(string? script)
    {
        return !string.IsNullOrWhiteSpace(script) && DbMemberRegex().IsMatch(script);
    }

    public static void Validate(string? script, bool hasDataSource)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return;
        }

        var usesDb = UsesDbApi(script);
        if (usesDb && !hasDataSource)
        {
            throw new UserFriendlyException("Script calls db.* but the node has no dataSourceId.");
        }

        if (!usesDb)
        {
            return;
        }

        foreach (Match call in DbSqlCallRegex().Matches(script))
        {
            if (!IsStaticSqlArgument(script, call.Index + call.Length))
            {
                throw new UserFriendlyException(
                    "SQL for db.query/execute/batch must be a static string literal (no variables or concatenation). " +
                    "Use '…' / \"…\" / `…` without ${} interpolation."
                );
            }
        }
    }

    /// <summary>
    /// 从调用括号后开始：跳过空白，要求 ' / " / ` 字面量，且模板不含 ${}，字面量后不能紧跟 +。
    /// </summary>
    private static bool IsStaticSqlArgument(string script, int argsStart)
    {
        var i = argsStart;
        while (i < script.Length && char.IsWhiteSpace(script[i]))
        {
            i++;
        }

        if (i >= script.Length)
        {
            return false;
        }

        var quote = script[i];
        if (quote is not ('\'' or '"' or '`'))
        {
            return false;
        }

        i++;
        while (i < script.Length)
        {
            var c = script[i];
            if (c == '\\' && i + 1 < script.Length)
            {
                i += 2;
                continue;
            }

            // 模板插值 ${ 视为非静态
            if (quote == '`' && c == '$' && i + 1 < script.Length && script[i + 1] == '{')
            {
                return false;
            }

            if (c == quote)
            {
                i++;
                while (i < script.Length && char.IsWhiteSpace(script[i]))
                {
                    i++;
                }

                // "sql" + x 禁止
                if (i < script.Length && script[i] == '+')
                {
                    return false;
                }

                return true;
            }

            i++;
        }

        return false;
    }
}
