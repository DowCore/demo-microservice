using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Meta.Dow.Orchestration.Tests;

/// <summary>
/// 配置安全护栏。
/// <para>
/// 2026-10-10 事故：DbMigrator 的 appsettings.secrets.json 用同一个 <c>"Clients"</c> 键又声明了一个数组，
/// 而 .NET 配置对数组是<b>按索引</b>合并的——索引 0 只覆盖了 ClientId，其余字段
/// （GrantTypes/Scopes/RedirectUris/RootUrls）继续沿用 appsettings.json 同索引的值，
/// 于是客户端列表整体“错位混血”：MetaDow_Vue 不再是任何条目的 ClientId ⇒ 从配置里消失
/// ⇒ 被清理逻辑软删除 ⇒ 前端登录报 invalid_request / ID2052（client_id is invalid）。
/// </para>
/// <para>
/// 因此立下不变量：<b>secrets 覆盖文件里不允许出现 JSON 数组</b>。
/// 需要覆盖数组语义时改用对象键（对象按 key 合并，不会错位），例如 <c>ClientSecrets:{clientId}</c>。
/// 同时扫描 *.example 模板，避免模板继续教错写法。
/// </para>
/// </summary>
public class SecretsConfigSafetyTests
{
    [Fact]
    public void Secret_override_files_must_not_contain_json_arrays()
    {
        var root = FindRepositoryRoot();

        var files = Directory
            .EnumerateFiles(root, "appsettings*.secrets.json*", SearchOption.AllDirectories)
            .Where(path => !IsIgnored(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(files);

        var violations = new List<string>();

        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            CollectArrayPaths(
                document.RootElement,
                "$",
                Path.GetRelativePath(root, file).Replace('\\', '/'),
                violations
            );
        }

        Assert.True(
            violations.Count == 0,
            "secrets 覆盖文件里不得出现数组（数组按索引合并会造成配置错位、对象按 key 合并才安全）："
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations)
        );
    }

    private static void CollectArrayPaths(
        JsonElement element,
        string path,
        string file,
        List<string> violations
    )
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                violations.Add($"  {file} → {path}");
                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectArrayPaths(property.Value, path + "." + property.Name, file, violations);
                }

                break;
        }
    }

    private static bool IsIgnored(string path)
    {
        var normalized = path.Replace('\\', '/');

        return normalized.Contains("/bin/")
            || normalized.Contains("/obj/")
            || normalized.Contains("/node_modules/");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meta.Dow.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("未能定位仓库根目录（未找到 Meta.Dow.slnx）。");
    }
}
