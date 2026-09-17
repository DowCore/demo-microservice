using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Volo.Abp;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>Mask 节点脱敏算子。</summary>
public static class MaskValueHelper
{
    public static JsonObject ApplyObjectRules(
        JsonObject source,
        IEnumerable<FlowMaskRuleDsl>? rules,
        FlowRuntimeContext ctx
    )
    {
        var result = (JsonObject)source.DeepClone()!;
        if (rules == null)
        {
            return result;
        }

        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Field))
            {
                continue;
            }

            ApplyRuleToObject(result, rule, ctx);
        }

        return result;
    }

    public static JsonArray ApplyItemRules(
        JsonArray rows,
        IEnumerable<FlowMaskRuleDsl>? rules,
        FlowRuntimeContext ctx
    )
    {
        var result = new JsonArray();
        foreach (var row in rows)
        {
            if (row is JsonObject obj)
            {
                result.Add(ApplyObjectRules(obj, rules, ctx));
            }
            else
            {
                result.Add(row?.DeepClone());
            }
        }

        return result;
    }

    /// <summary>落库前对入参中的脱敏字段打码，避免日志二次泄密。</summary>
    public static JsonObject RedactForLog(JsonObject source, IEnumerable<FlowMaskRuleDsl>? rules)
    {
        var clone = (JsonObject)source.DeepClone()!;
        if (rules == null)
        {
            return clone;
        }

        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Field))
            {
                continue;
            }

            var existing = FlowContextResolver.GetByPath(clone, rule.Field);
            if (existing == null)
            {
                continue;
            }

            SetByPath(clone, rule.Field, JsonValue.Create("***"));
        }

        return clone;
    }

    private static void ApplyRuleToObject(JsonObject target, FlowMaskRuleDsl rule, FlowRuntimeContext ctx)
    {
        var op = (rule.Op ?? "fixed").Trim().ToLowerInvariant();
        if (op == "drop")
        {
            RemoveByPath(target, rule.Field);
            return;
        }

        var current = FlowContextResolver.GetByPath(target, rule.Field);
        if (current == null && op != "keepEmpty")
        {
            return;
        }

        if (op == "redact")
        {
            SetByPath(target, rule.Field, null);
            return;
        }

        var text = current?.ToString() ?? "";
        if (op == "keepEmpty")
        {
            SetByPath(target, rule.Field,
                string.IsNullOrEmpty(text) ? JsonValue.Create("") : JsonValue.Create(rule.Value ?? "******"));
            return;
        }

        var masked = op switch
        {
            "fixed" => rule.Value ?? "******",
            "phone" => MaskKeep(text, 3, 4, rule.MaskChar),
            "idcard" => MaskKeep(text, 4, 4, rule.MaskChar),
            "email" => MaskEmail(text, rule.MaskChar),
            "bankcard" => MaskKeep(text, 0, 4, rule.MaskChar),
            "name" => MaskName(text, rule.MaskChar),
            "mask" => MaskKeep(text, rule.KeepStart ?? 0, rule.KeepEnd ?? 0, rule.MaskChar),
            "hash" => Hash(text, ResolveSalt(rule.SaltFrom, target, ctx)),
            _ => throw new UserFriendlyException($"Unsupported mask op '{rule.Op}'.")
        };

        SetByPath(target, rule.Field, JsonValue.Create(masked));
    }

    private static string? ResolveSalt(string? saltFrom, JsonObject target, FlowRuntimeContext ctx)
    {
        if (string.IsNullOrWhiteSpace(saltFrom))
        {
            return null;
        }

        var local = FlowContextResolver.GetByPath(target, saltFrom);
        if (local != null)
        {
            return local.ToString();
        }

        return FlowContextResolver.ResolvePath(saltFrom, ctx)?.ToString();
    }

    private static string MaskKeep(string text, int keepStart, int keepEnd, string? maskChar)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var ch = string.IsNullOrEmpty(maskChar) ? "*" : maskChar![0].ToString();
        keepStart = Math.Max(0, keepStart);
        keepEnd = Math.Max(0, keepEnd);
        if (keepStart + keepEnd >= text.Length)
        {
            return new string(ch[0], text.Length);
        }

        var mid = text.Length - keepStart - keepEnd;
        return text[..keepStart] + new string(ch[0], mid) + text[^keepEnd..];
    }

    private static string MaskEmail(string text, string? maskChar)
    {
        var at = text.IndexOf('@');
        if (at <= 0)
        {
            return MaskKeep(text, 1, 0, maskChar);
        }

        var local = text[..at];
        var domain = text[at..];
        if (local.Length <= 2)
        {
            return new string((maskChar ?? "*")[0], local.Length) + domain;
        }

        return local[0] + new string((maskChar ?? "*")[0], local.Length - 2) + local[^1] + domain;
    }

    private static string MaskName(string text, string? maskChar)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var ch = (maskChar ?? "*")[0];
        if (text.Length == 1)
        {
            return ch.ToString();
        }

        return text[0] + new string(ch, text.Length - 1);
    }

    private static string Hash(string text, string? salt)
    {
        var bytes = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(salt) ? text : text + salt);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void SetByPath(JsonObject root, string path, JsonNode? value)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return;
        }

        JsonObject current = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (current[parts[i]] is not JsonObject next)
            {
                next = new JsonObject();
                current[parts[i]] = next;
            }

            current = next;
        }

        current[parts[^1]] = value?.DeepClone();
    }

    private static void RemoveByPath(JsonObject root, string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return;
        }

        JsonObject current = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (current[parts[i]] is not JsonObject next)
            {
                return;
            }

            current = next;
        }

        current.Remove(parts[^1]);
    }
}
