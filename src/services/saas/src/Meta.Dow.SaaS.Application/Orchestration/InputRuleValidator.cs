using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Volo.Abp;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 入参校验：通用声明式规则 + 受控表达式（expr/assert），不执行任意 JS。
/// </summary>
public static class InputRuleValidator
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex UrlRegex = new(
        @"^https?://[^\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    public static void ValidateInputs(IEnumerable<FlowInputParameterDsl> inputs, FlowRuntimeContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        foreach (var param in inputs ?? [])
        {
            if (string.IsNullOrWhiteSpace(param.Name))
            {
                continue;
            }

            // 系统参数由引擎写入，不做调用方规则校验
            if (string.Equals(param.Source, "system", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ctx.Input.TryGetPropertyValue(param.Name, out var value);
            ValidateParameter(param, value, param.Name, ctx);
        }
    }

    public static void ValidateParameter(
        FlowInputParameterDsl schema,
        JsonNode? value,
        string path,
        FlowRuntimeContext ctx
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(ctx);

        var isMissing = value is null ||
                        (value is JsonValue jv && jv.TryGetValue<string>(out var s) && string.IsNullOrWhiteSpace(s));

        if (schema.Required && isMissing)
        {
            throw Fail(path, "required", schema, $"Required input '{path}' is missing.");
        }

        if (HasRule(schema, "required") && isMissing)
        {
            throw Fail(path, "required", schema, $"Required input '{path}' is missing.");
        }

        // 表达式规则可在缺值时仍求值（跨字段）；标量规则缺值则跳过
        if (schema.Rules is { Count: > 0 })
        {
            foreach (var rule in schema.Rules)
            {
                var ruleType = (rule.Type ?? "").Trim().ToLowerInvariant();
                if (ruleType is "expr" or "assert" or "js" or "when")
                {
                    ApplyExprRule(rule, path, ctx);
                }
            }
        }

        if (isMissing)
        {
            return;
        }

        ValidateTypeShape(schema, value!, path);

        if (schema.Rules is { Count: > 0 })
        {
            foreach (var rule in schema.Rules)
            {
                var ruleType = (rule.Type ?? "").Trim().ToLowerInvariant();
                if (ruleType is "expr" or "assert" or "js" or "when" or "required" or "")
                {
                    continue;
                }

                ApplyRule(schema, rule, value!, path);
            }
        }

        if (string.Equals(schema.Type, "object", StringComparison.OrdinalIgnoreCase) &&
            schema.Properties is { Count: > 0 } &&
            value is JsonObject obj)
        {
            foreach (var child in schema.Properties)
            {
                obj.TryGetPropertyValue(child.Name, out var childVal);
                ValidateParameter(child, childVal, $"{path}.{child.Name}", ctx);
            }
        }

        if (string.Equals(schema.Type, "array", StringComparison.OrdinalIgnoreCase) &&
            schema.Items != null &&
            value is JsonArray arr)
        {
            for (var i = 0; i < arr.Count; i++)
            {
                ValidateParameter(schema.Items, arr[i], $"{path}[{i}]", ctx);
            }
        }
    }

    /// <summary>
    /// expr / when / js：表达式为 true 时失败（if cond return message）。
    /// assert：表达式为 false 时失败（必须成立）。
    /// </summary>
    private static void ApplyExprRule(FlowInputRuleDsl rule, string path, FlowRuntimeContext ctx)
    {
        var expr = rule.Value?.GetValue<string>() ?? rule.Value?.ToString()?.Trim('"');
        if (string.IsNullOrWhiteSpace(expr))
        {
            return;
        }

        var ruleType = (rule.Type ?? "expr").Trim().ToLowerInvariant();
        bool hit;
        try
        {
            var result = BoolExpressionEvaluator.Evaluate(expr, ctx);
            hit = ruleType == "assert" ? !result : result;
        }
        catch (UserFriendlyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new UserFriendlyException($"Invalid validation expression on '{path}': {ex.Message}");
        }

        if (hit)
        {
            var msg = !string.IsNullOrWhiteSpace(rule.Message)
                ? rule.Message!
                : $"Validation failed on '{path}'.";
            throw new UserFriendlyException(msg);
        }
    }

    private static void ValidateTypeShape(FlowInputParameterDsl schema, JsonNode value, string path)
    {
        var type = (schema.Type ?? "string").ToLowerInvariant();
        switch (type)
        {
            case "number":
                if (!TryGetDecimal(value, out _))
                {
                    throw Fail(path, "type", schema, $"Input '{path}' must be a number.");
                }

                break;
            case "boolean":
                if (!TryGetBool(value, out _))
                {
                    throw Fail(path, "type", schema, $"Input '{path}' must be a boolean.");
                }

                break;
            case "object":
                if (value is not JsonObject)
                {
                    throw Fail(path, "type", schema, $"Input '{path}' must be an object.");
                }

                break;
            case "array":
                if (value is not JsonArray)
                {
                    throw Fail(path, "type", schema, $"Input '{path}' must be an array.");
                }

                break;
            case "guid":
                if (!Guid.TryParse(ToStringValue(value), out _))
                {
                    throw Fail(path, "guid", schema, $"Input '{path}' must be a GUID.");
                }

                break;
        }
    }

    private static void ApplyRule(FlowInputParameterDsl schema, FlowInputRuleDsl rule, JsonNode value, string path)
    {
        var type = (rule.Type ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(type) || type == "required")
        {
            return; // handled above
        }

        switch (type)
        {
            case "min":
            {
                if (!TryGetDecimal(value, out var n) || !TryGetRuleDecimal(rule, out var min))
                {
                    break;
                }

                if (n < min)
                {
                    throw Fail(path, type, rule, schema, $"Input '{path}' must be >= {min}.");
                }

                break;
            }
            case "max":
            {
                if (!TryGetDecimal(value, out var n) || !TryGetRuleDecimal(rule, out var max))
                {
                    break;
                }

                if (n > max)
                {
                    throw Fail(path, type, rule, schema, $"Input '{path}' must be <= {max}.");
                }

                break;
            }
            case "minlength":
            {
                var len = GetLength(value);
                if (!TryGetRuleInt(rule, out var minLen))
                {
                    break;
                }

                if (len < minLen)
                {
                    throw Fail(path, type, rule, schema, $"Input '{path}' length must be >= {minLen}.");
                }

                break;
            }
            case "maxlength":
            {
                var len = GetLength(value);
                if (!TryGetRuleInt(rule, out var maxLen))
                {
                    break;
                }

                if (len > maxLen)
                {
                    throw Fail(path, type, rule, schema, $"Input '{path}' length must be <= {maxLen}.");
                }

                break;
            }
            case "minitems":
            {
                if (value is not JsonArray arr || !TryGetRuleInt(rule, out var minItems))
                {
                    break;
                }

                if (arr.Count < minItems)
                {
                    throw Fail(path, type, rule, schema, $"Input '{path}' must have at least {minItems} items.");
                }

                break;
            }
            case "maxitems":
            {
                if (value is not JsonArray arr || !TryGetRuleInt(rule, out var maxItems))
                {
                    break;
                }

                if (arr.Count > maxItems)
                {
                    throw Fail(path, type, rule, schema, $"Input '{path}' must have at most {maxItems} items.");
                }

                break;
            }
            case "pattern":
            {
                var text = ToStringValue(value) ?? "";
                var pattern = rule.Value?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    break;
                }

                try
                {
                    if (!Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant))
                    {
                        throw Fail(path, type, rule, schema, $"Input '{path}' does not match pattern.");
                    }
                }
                catch (RegexParseException)
                {
                    throw new UserFriendlyException($"Invalid pattern rule on '{path}'.");
                }

                break;
            }
            case "enum":
            {
                if (rule.Value is not JsonArray options || options.Count == 0)
                {
                    break;
                }

                var actual = NormalizeComparable(value);
                var ok = options.Any(o =>
                    string.Equals(NormalizeComparable(o), actual, StringComparison.OrdinalIgnoreCase));
                if (!ok)
                {
                    throw Fail(path, type, rule, schema, $"Input '{path}' is not in allowed enum values.");
                }

                break;
            }
            case "email":
            {
                var text = ToStringValue(value) ?? "";
                if (!EmailRegex.IsMatch(text))
                {
                    throw Fail(path, type, rule, schema, $"Input '{path}' must be an email.");
                }

                break;
            }
            case "guid":
            {
                if (!Guid.TryParse(ToStringValue(value), out _))
                {
                    throw Fail(path, type, rule, schema, $"Input '{path}' must be a GUID.");
                }

                break;
            }
            case "url":
            {
                var text = ToStringValue(value) ?? "";
                if (!UrlRegex.IsMatch(text))
                {
                    throw Fail(path, type, rule, schema, $"Input '{path}' must be a URL.");
                }

                break;
            }
            default:
                // unknown declarative rule → ignore only if empty; else error
                if (!string.IsNullOrWhiteSpace(rule.Type) &&
                    rule.Type is not ("expr" or "assert" or "js" or "when"))
                {
                    throw new UserFriendlyException($"Unsupported validation rule '{rule.Type}' on '{path}'.");
                }

                break;
        }
    }

    private static bool HasRule(FlowInputParameterDsl schema, string type)
    {
        return schema.Rules?.Any(r =>
            string.Equals(r.Type, type, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static int GetLength(JsonNode value)
    {
        return value switch
        {
            JsonArray arr => arr.Count,
            JsonObject obj => obj.Count,
            _ => (ToStringValue(value) ?? "").Length
        };
    }

    private static bool TryGetDecimal(JsonNode value, out decimal number)
    {
        var parsed = JsonNodeNumbers.ToDecimal(value);
        number = parsed ?? 0;
        return parsed.HasValue;
    }

    private static bool TryGetBool(JsonNode value, out bool b)
    {
        b = false;
        if (value is JsonValue jv)
        {
            if (jv.TryGetValue<bool>(out b))
            {
                return true;
            }

            if (jv.TryGetValue<string>(out var s) && bool.TryParse(s, out b))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetRuleDecimal(FlowInputRuleDsl rule, out decimal number)
    {
        number = 0;
        return rule.Value != null && TryGetDecimal(rule.Value, out number);
    }

    private static bool TryGetRuleInt(FlowInputRuleDsl rule, out int number)
    {
        number = 0;
        if (!TryGetRuleDecimal(rule, out var d))
        {
            return false;
        }

        number = (int)d;
        return true;
    }

    private static string? ToStringValue(JsonNode? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonValue jv && jv.TryGetValue<string>(out var s))
        {
            return s;
        }

        return value.ToJsonString().Trim('"');
    }

    private static string NormalizeComparable(JsonNode? value)
    {
        return ToStringValue(value)?.Trim() ?? "";
    }

    private static UserFriendlyException Fail(
        string path,
        string ruleType,
        FlowInputParameterDsl schema,
        string fallback
    )
    {
        var msg = schema.Rules?
            .FirstOrDefault(r => string.Equals(r.Type, ruleType, StringComparison.OrdinalIgnoreCase))
            ?.Message;
        return new UserFriendlyException(string.IsNullOrWhiteSpace(msg) ? fallback : msg!);
    }

    private static UserFriendlyException Fail(
        string path,
        string ruleType,
        FlowInputRuleDsl rule,
        FlowInputParameterDsl schema,
        string fallback
    )
    {
        var msg = !string.IsNullOrWhiteSpace(rule.Message) ? rule.Message : fallback;
        return new UserFriendlyException(msg!);
    }
}
