using System.Globalization;
using System.Text.Json.Nodes;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Users;

namespace Meta.Dow.SaaS.Orchestration;

public class SystemContextSnapshot
{
    public DateTimeOffset Now { get; init; }

    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Utc;

    public JsonObject Sys { get; init; } = new();
}

public class SystemContextBuilder : ITransientDependency
{
    private readonly ICurrentUser _currentUser;
    private readonly Volo.Abp.MultiTenancy.ICurrentTenant _currentTenant;

    public SystemContextBuilder(
        ICurrentUser currentUser,
        Volo.Abp.MultiTenancy.ICurrentTenant currentTenant
    )
    {
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    public SystemContextSnapshot Build(string? timeZoneId = null)
    {
        var tz = ResolveTimeZone(timeZoneId);
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        var sys = new JsonObject
        {
            ["userId"] = _currentUser.Id?.ToString(),
            ["userName"] = _currentUser.UserName,
            ["userEmail"] = _currentUser.Email,
            ["tenantId"] = _currentTenant.Id?.ToString(),
            ["tenantName"] = _currentTenant.Name,
            ["culture"] = CultureInfo.CurrentUICulture.Name,
            ["timeZone"] = tz.Id,
            ["correlationId"] = Guid.NewGuid().ToString("N"),
            ["Now"] = now.ToString("o"),
            ["Today"] = DateExpressionEvaluator.Evaluate("sys.Today", now, tz).ToString("o"),
            ["Yesterday"] = DateExpressionEvaluator.Evaluate("sys.Yesterday", now, tz).ToString("o"),
            ["Tomorrow"] = DateExpressionEvaluator.Evaluate("sys.Tomorrow", now, tz).ToString("o"),
            ["MonthStart"] = DateExpressionEvaluator.Evaluate("sys.MonthStart", now, tz).ToString("o"),
            ["MonthEnd"] = DateExpressionEvaluator.Evaluate("sys.MonthEnd", now, tz).ToString("o"),
            ["WeekStart"] = DateExpressionEvaluator.Evaluate("sys.WeekStart", now, tz).ToString("o"),
            ["WeekEnd"] = DateExpressionEvaluator.Evaluate("sys.WeekEnd", now, tz).ToString("o"),
            ["YearStart"] = DateExpressionEvaluator.Evaluate("sys.YearStart", now, tz).ToString("o"),
            ["YearEnd"] = DateExpressionEvaluator.Evaluate("sys.YearEnd", now, tz).ToString("o"),
        };

        // camelCase aliases for selectors
        sys["now"] = sys["Now"]?.DeepClone();
        sys["today"] = sys["Today"]?.DeepClone();
        sys["monthStart"] = sys["MonthStart"]?.DeepClone();

        return new SystemContextSnapshot
        {
            Now = now,
            TimeZone = tz,
            Sys = sys
        };
    }

    public JsonNode? ResolveSystemValue(string? systemKey, string? systemExpr, SystemContextSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(systemExpr))
        {
            var dt = DateExpressionEvaluator.Evaluate(systemExpr, snapshot.Now, snapshot.TimeZone);
            return JsonValue.Create(dt.ToString("o"));
        }

        if (string.IsNullOrWhiteSpace(systemKey))
        {
            return null;
        }

        var key = systemKey.Trim();
        if (key.StartsWith("sys.", StringComparison.OrdinalIgnoreCase))
        {
            key = key["sys.".Length..];
        }

        if (snapshot.Sys.TryGetPropertyValue(key, out var value))
        {
            return value?.DeepClone();
        }

        // PascalCase fallback
        var pascal = char.ToUpperInvariant(key[0]) + key[1..];
        if (snapshot.Sys.TryGetPropertyValue(pascal, out value))
        {
            return value?.DeepClone();
        }

        throw new UserFriendlyException($"Unknown system key: sys.{key}");
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
            }
            catch (TimeZoneNotFoundException)
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
                }
                catch
                {
                    return TimeZoneInfo.Local;
                }
            }
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            return TimeZoneInfo.Local;
        }
    }
}
