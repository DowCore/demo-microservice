using System.Globalization;
using System.Text.RegularExpressions;
using Volo.Abp;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 求值日期表达式，如 <c>sys.Now - 3d</c>、<c>sys.Today + 1M | startOfDay</c>。
/// </summary>
public static class DateExpressionEvaluator
{
    private static readonly Regex ExprRegex = new(
        @"^\s*(?<anchor>sys\.\w+)\s*(?:(?<op>[+\-])\s*(?<amount>\d+)\s*(?<unit>[smhdwMy]))?\s*(?:\|\s*(?<boundary>\w+))?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    private static readonly Dictionary<string, string> AliasMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sys.now"] = "sys.Now",
        ["sys.today"] = "sys.Today",
        ["sys.yesterday"] = "sys.Yesterday",
        ["sys.tomorrow"] = "sys.Tomorrow",
        ["sys.monthstart"] = "sys.MonthStart",
        ["sys.monthend"] = "sys.MonthEnd",
        ["sys.weekstart"] = "sys.WeekStart",
        ["sys.weekend"] = "sys.WeekEnd",
        ["sys.yearstart"] = "sys.YearStart",
        ["sys.yearend"] = "sys.YearEnd",
    };

    public static bool LooksLikeDateExpression(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var t = text.Trim();
        return t.StartsWith("sys.", StringComparison.OrdinalIgnoreCase) &&
               (t.Contains('+') || t.Contains('-') || t.Contains('|') ||
                AliasMap.ContainsKey(t) ||
                t.Equals("sys.Now", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("sys.Today", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("sys.Yesterday", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("sys.Tomorrow", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("sys.MonthStart", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("sys.MonthEnd", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("sys.WeekStart", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("sys.WeekEnd", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("sys.YearStart", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("sys.YearEnd", StringComparison.OrdinalIgnoreCase));
    }

    public static DateTimeOffset Evaluate(string expression, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        var match = ExprRegex.Match(expression);
        if (!match.Success)
        {
            throw new UserFriendlyException($"Invalid date expression: {expression}");
        }

        var anchorRaw = match.Groups["anchor"].Value;
        var anchorKey = NormalizeAnchor(anchorRaw);
        var anchor = ResolveAnchor(anchorKey, now, timeZone);

        if (match.Groups["op"].Success)
        {
            var sign = match.Groups["op"].Value == "-" ? -1 : 1;
            var amount = int.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture) * sign;
            var unit = match.Groups["unit"].Value;
            anchor = ApplyOffset(anchor, amount, unit);
        }

        if (match.Groups["boundary"].Success)
        {
            anchor = ApplyBoundary(anchor, match.Groups["boundary"].Value, timeZone);
        }

        return anchor;
    }

    public static string NormalizeAnchor(string anchor)
    {
        return AliasMap.TryGetValue(anchor, out var canonical) ? canonical : anchor;
    }

    private static DateTimeOffset ResolveAnchor(string key, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(now, timeZone);
        return key switch
        {
            "sys.Now" => local,
            "sys.Today" => StartOfDay(local),
            "sys.Yesterday" => StartOfDay(local).AddDays(-1),
            "sys.Tomorrow" => StartOfDay(local).AddDays(1),
            "sys.MonthStart" => new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, local.Offset),
            "sys.MonthEnd" => new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, local.Offset).AddMonths(1),
            "sys.WeekStart" => StartOfWeek(local),
            "sys.WeekEnd" => StartOfWeek(local).AddDays(7),
            "sys.YearStart" => new DateTimeOffset(local.Year, 1, 1, 0, 0, 0, local.Offset),
            "sys.YearEnd" => new DateTimeOffset(local.Year + 1, 1, 1, 0, 0, 0, local.Offset),
            _ => throw new UserFriendlyException($"Unknown date anchor: {key}")
        };
    }

    private static DateTimeOffset ApplyOffset(DateTimeOffset value, int amount, string unit)
    {
        return unit switch
        {
            "s" => value.AddSeconds(amount),
            "m" => value.AddMinutes(amount),
            "h" => value.AddHours(amount),
            "d" => value.AddDays(amount),
            "w" => value.AddDays(amount * 7),
            "M" => value.AddMonths(amount),
            "y" => value.AddYears(amount),
            _ => throw new UserFriendlyException($"Unknown date unit: {unit}")
        };
    }

    private static DateTimeOffset ApplyBoundary(DateTimeOffset value, string boundary, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(value, timeZone);
        return boundary.ToLowerInvariant() switch
        {
            "startofday" => StartOfDay(local),
            "endofday" => StartOfDay(local).AddDays(1),
            "startofmonth" => new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, local.Offset),
            "endofmonth" => new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, local.Offset).AddMonths(1),
            "startofyear" => new DateTimeOffset(local.Year, 1, 1, 0, 0, 0, local.Offset),
            "endofyear" => new DateTimeOffset(local.Year + 1, 1, 1, 0, 0, 0, local.Offset),
            _ => throw new UserFriendlyException($"Unknown boundary function: {boundary}")
        };
    }

    private static DateTimeOffset StartOfDay(DateTimeOffset local)
    {
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, local.Offset);
    }

    private static DateTimeOffset StartOfWeek(DateTimeOffset local)
    {
        // Monday as week start
        var diff = ((int)local.DayOfWeek + 6) % 7;
        return StartOfDay(local).AddDays(-diff);
    }
}
