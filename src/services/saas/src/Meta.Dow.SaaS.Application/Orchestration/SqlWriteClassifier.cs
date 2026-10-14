using System.Text;
using System.Text.RegularExpressions;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// SQL 读写判定。写闸门（<c>Orchestration.Sql.Write</c>）的唯一依据。
///
/// <para><b>为什么独立成类</b>：写闸门是安全边界，必须能被纯单测覆盖。原先判定内联在
/// <c>CodeSandboxExecutor.EnsureSqlWrite</c> 里，而后者依赖四个注入的外部执行器
/// （<c>null!</c> 测试脚手架一触达即 NRE），导致这条安全逻辑事实上无法测试。</para>
///
/// <para><b>缺陷 S4 的两个绕过点（原实现）</b>：</para>
/// <list type="bullet">
/// <item><c>WITH</c> 被当作读 ⇒ <c>WITH x AS (…) DELETE FROM t</c> 无需写权限即可执行。</item>
/// <item><c>db.query</c> 完全不检查 ⇒ 任何写语句都能从 query 入口绕过。</item>
/// </list>
///
/// <para>本类的判定是<b>保守</b>的：宁可把读误判为写（要求权限），也不可把写误判为读。
/// 对写闸门而言，误拒只是多要一个权限，漏放则是越权。</para>
/// </summary>
public static class SqlWriteClassifier
{
    /// <summary>语句起始即确定为只读的关键字。</summary>
    private static readonly string[] ReadOnlyLeadingKeywords =
    [
        "select", "show", "explain", "describe", "desc", "values"
    ];

    /// <summary>
    /// CTE（<c>WITH</c>）之后的主语句若出现这些关键字，则整条语句按写处理。
    /// <c>WITH … SELECT</c> 是读，<c>WITH … DELETE/UPDATE/INSERT</c> 是写。
    /// </summary>
    private static readonly string[] WriteKeywords =
    [
        "insert", "update", "delete", "merge", "upsert", "replace",
        "truncate", "drop", "alter", "create", "grant", "revoke",
        "rename", "comment", "call", "execute", "exec", "set", "load"
    ];

    /// <summary>行注释与块注释。判定前先剥离，避免用注释藏关键字。</summary>
    private static readonly Regex CommentRegex = new(
        @"--[^\n]*|/\*.*?\*/",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    /// <summary>
    /// 判断一条 SQL 是否会写数据。
    /// </summary>
    /// <param name="sql">原始 SQL；允许含注释、前导括号与空白。</param>
    /// <returns><c>true</c> 表示可能写数据，需要写权限。</returns>
    public static bool IsWrite(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            // 空语句不写，但也不该静默通过——调用方按写处理会要求权限，属于安全侧。
            return false;
        }

        var normalized = Normalize(sql);
        if (normalized.Length == 0)
        {
            return false;
        }

        var first = FirstKeyword(normalized);
        if (first.Length == 0)
        {
            // 认不出关键字 ⇒ 保守按写处理。
            return true;
        }

        if (ReadOnlyLeadingKeywords.Contains(first, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!first.Equals("with", StringComparison.OrdinalIgnoreCase))
        {
            // 非读关键字开头 ⇒ 写（含 insert/update/delete/ddl/未知）。
            return true;
        }

        // CTE：扫描括号外的关键字。WITH 块内的括号内容（列清单、CTE 体）已剥离。
        return ContainsWriteKeywordOutsideParens(normalized);
    }

    /// <summary>
    /// 剥离注释、字符串字面量，并去掉包裹用的多余括号与空白，
    /// 得到便于做关键字判定的规范化文本。
    /// </summary>
    private static string Normalize(string sql)
    {
        var withoutComments = CommentRegex.Replace(sql, " ");
        var sb = new StringBuilder(withoutComments.Length);
        var quote = '\0';

        for (var i = 0; i < withoutComments.Length; i++)
        {
            var c = withoutComments[i];

            if (quote != '\0')
            {
                // 字面量内部：跳过转义，避免 'it''s' 之类提前闭合。
                if (c == quote)
                {
                    if (i + 1 < withoutComments.Length && withoutComments[i + 1] == quote)
                    {
                        i++;
                        continue;
                    }

                    quote = '\0';
                }
                else if (c == '\\' && i + 1 < withoutComments.Length)
                {
                    i++;
                }

                sb.Append(' ');
                continue;
            }

            switch (c)
            {
                case '\'':
                case '"':
                case '`':
                    quote = c;
                    sb.Append(' ');
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        var text = sb.ToString().Trim();

        // 去掉包裹用的括号：((SELECT …)) ⇒ (SELECT …)
        while (text.Length > 1 && text[0] == '(' && MatchingParen(text, 0) == text.Length - 1)
        {
            text = text[1..^1].Trim();
        }

        return text;
    }

    /// <summary>返回与 <paramref name="start"/> 配对的右括号下标；不配对返回 -1。</summary>
    private static int MatchingParen(string text, int start)
    {
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>取首个单词作为关键字（小写）。跳过前导 <c>(</c> 与标点。</summary>
    private static string FirstKeyword(string normalized)
    {
        for (var i = 0; i < normalized.Length; i++)
        {
            if (!char.IsLetter(normalized[i]))
            {
                continue;
            }

            var start = i;
            while (i < normalized.Length && (char.IsLetterOrDigit(normalized[i]) || normalized[i] == '_'))
            {
                i++;
            }

            return normalized[start..i];
        }

        return "";
    }

    /// <summary>
    /// 在括号外扫描写关键字。CTE 的列清单在括号内，不会误命中；
    /// CTE 体（<c>AS ( … )</c>）同样在括号内，因此
    /// <c>WITH x AS (SELECT 1) SELECT * FROM x</c> 判为读，
    /// <c>WITH x AS (SELECT 1) DELETE FROM t</c> 判为写。
    /// </summary>
    private static bool ContainsWriteKeywordOutsideParens(string normalized)
    {
        var depth = 0;

        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];

            if (c == '(')
            {
                depth++;
                continue;
            }

            if (c == ')')
            {
                depth--;
                continue;
            }

            if (depth != 0 || !char.IsLetter(c))
            {
                continue;
            }

            // 单词起始：前一个字符必须是分隔符，避免从标识符中间切出假关键字。
            if (i > 0 && (char.IsLetterOrDigit(normalized[i - 1]) || normalized[i - 1] == '_'))
            {
                continue;
            }

            var start = i;
            while (i < normalized.Length && (char.IsLetterOrDigit(normalized[i]) || normalized[i] == '_'))
            {
                i++;
            }

            var word = normalized[start..i];
            if (WriteKeywords.Contains(word, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
