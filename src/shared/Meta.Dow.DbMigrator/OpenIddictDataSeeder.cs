using JetBrains.Annotations;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using Meta.Dow.DbMigrator.Model;
using Volo.Abp;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.OpenIddict.Applications;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;

namespace Meta.Dow.DbMigrator;

public class OpenIddictDataSeeder(
    IAbpApplicationManager applicationManager,
    IOpenIddictScopeManager scopeManager,
    IPermissionDataSeeder permissionDataSeeder,
    IStringLocalizer<OpenIddictResponse> l,
    IConfiguration configuration,
    ICurrentTenant currentTenant,
    ILogger<OpenIddictDataSeeder> logger
) : ITransientDependency
{
    private readonly IConfiguration _configuration = configuration;
    private readonly ICurrentTenant _currentTenant = currentTenant;
    private readonly IAbpApplicationManager _applicationManager = applicationManager;
    private readonly IOpenIddictScopeManager _scopeManager = scopeManager;
    private readonly IPermissionDataSeeder _permissionDataSeeder = permissionDataSeeder;
    private readonly IStringLocalizer<OpenIddictResponse> l = l;
    private readonly ILogger<OpenIddictDataSeeder> _logger = logger;

    [UnitOfWork]
    public virtual async Task SeedAsync()
    {
        // 先把配置解析并校验完，再动数据库。残缺配置绝不允许走到删除逻辑。
        var clients = LoadClients();
        ValidateClients(clients);

        using (_currentTenant.Change(null))
        {
            await CreateApiResourcesAsync().ConfigureAwait(false);
            await CreateClientsAsync(clients).ConfigureAwait(false);
            await RemoveObsoleteClientsAsync(clients).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 读取客户端定义，并把 appsettings.secrets.json 里的 <c>ClientSecrets:{clientId}</c> 合并进来。
    /// </summary>
    /// <remarks>
    /// 事故复盘（2026-10-10，登录报 invalid_request / ID2052 "client_id is invalid"）：
    /// secrets 文件曾用同一个 <c>"Clients"</c> 键再声明一个只含 ClientId/ClientSecret 的数组。
    /// .NET 配置对**数组按索引合并**——secrets 的索引 0 只覆盖 ClientId，其余字段
    /// （GrantTypes/Scopes/RedirectUris/RootUrls）继续沿用 appsettings.json 索引 0 的值。
    /// 于是客户端列表整体"错位混血"：
    /// 索引 0 的 MetaDow_Vue 被改名成 Administration_API ⇒ MetaDow_Vue 从配置里消失
    /// ⇒ 被 <see cref="RemoveObsoleteClientsAsync"/> 软删除 ⇒ 前端带 client_id=MetaDow_Vue
    /// 走 authorize 时被 OpenIddict 拒绝（ID2052）。
    /// 因此密钥一律放独立的 <c>ClientSecrets</c> 对象键：对象是按 key 合并的，不会错位。
    /// </remarks>
    private List<ServiceClient> LoadClients()
    {
        var clients = _configuration.GetSection("Clients").Get<List<ServiceClient>>() ?? [];

        var secrets = _configuration.GetSection("ClientSecrets").Get<Dictionary<string, string>>() ?? [];

        foreach (var client in clients)
        {
            if (!string.IsNullOrWhiteSpace(client.ClientSecret))
            {
                continue;
            }

            if (secrets.TryGetValue(client.ClientId, out var secret) && !string.IsNullOrWhiteSpace(secret))
            {
                client.ClientSecret = secret;
            }
        }

        return clients;
    }

    /// <summary>
    /// 校验客户端定义完整性。任一项不合法即抛错中止，且此时尚未对数据库做任何写操作，
    /// 避免"配置残缺 → 误判为未登记 → 删除线上客户端"。
    /// </summary>
    private static void ValidateClients(List<ServiceClient> clients)
    {
        var problems = new List<string>();
        var seenClientIds = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < clients.Count; index++)
        {
            var client = clients[index];

            if (string.IsNullOrWhiteSpace(client.ClientId))
            {
                problems.Add($"第 {index} 项 ClientId 为空（配置可能被部分覆盖）");
                continue;
            }

            if (!seenClientIds.Add(client.ClientId))
            {
                problems.Add($"ClientId 重复：{client.ClientId}");
            }

            if (client.GrantTypes.Length == 0)
            {
                problems.Add($"{client.ClientId} 未配置 GrantTypes");
            }

            if (client.Scopes.Length == 0)
            {
                problems.Add($"{client.ClientId} 未配置 Scopes");
            }
        }

        if (problems.Count > 0)
        {
            throw new AbpException(
                "Clients 配置不完整，已中止且未改动数据库（防止以残缺配置删掉已注册客户端）："
                    + string.Join("；", problems)
            );
        }
    }

    private async Task CreateClientsAsync(List<ServiceClient> clients)
    {
        var commonScopes = new[]
        {
            OpenIddictConstants.Permissions.Scopes.Address,
            OpenIddictConstants.Permissions.Scopes.Email,
            OpenIddictConstants.Permissions.Scopes.Phone,
            OpenIddictConstants.Permissions.Scopes.Profile,
            OpenIddictConstants.Permissions.Scopes.Roles,
            OpenIddictConstants.Scopes.OpenId,
            "offline_access",
        };

        foreach (var client in clients)
        {
            await CreateClientAsync(
                    client.ClientId,
                    displayName: client.ClientId,
                    secret: client.ClientSecret,
                    scopes: commonScopes.Union(client.Scopes).ToList(),
                    grantTypes: [.. client.GrantTypes],
                    redirectUris: client.RedirectUris,
                    postLogoutRedirectUris: client.PostLogoutRedirectUris,
                    clientUri: client.RootUrls.FirstOrDefault(),
                    consentType: OpenIddictConstants.ConsentTypes.Implicit
                )
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 删除配置中已不登记的客户端。破坏性操作，需在配置里显式打开
    /// <c>PruneObsoleteClients</c> 才会执行，且逐个 ClientId 打 Warning，
    /// 避免再次出现"某个客户端静默消失"这种只能靠翻库才能定位的故障。
    /// </summary>
    private async Task RemoveObsoleteClientsAsync(List<ServiceClient> clients)
    {
        if (!_configuration.GetValue("PruneObsoleteClients", false))
        {
            _logger.LogInformation("PruneObsoleteClients 未开启，跳过未登记客户端的清理。");
            return;
        }

        var configuredClientIds = clients.Select(c => c.ClientId).ToHashSet(StringComparer.Ordinal);

        await foreach (var application in _applicationManager.ListAsync().ConfigureAwait(false))
        {
            var clientId = await _applicationManager.GetClientIdAsync(application).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(clientId) || configuredClientIds.Contains(clientId))
            {
                continue;
            }

            _logger.LogWarning("删除未在 Clients 配置中登记的客户端：{ClientId}", clientId);
            await _applicationManager.DeleteAsync(application).ConfigureAwait(false);
        }
    }

    private async Task CreateApiResourcesAsync()
    {
        var apiResources = _configuration.GetSection("ApiResource").Get<string[]>() ?? [];

        foreach (var item in apiResources)
        {
            await CreateApiResourceAsync(item).ConfigureAwait(false);
        }
    }

    private async Task CreateApiResourceAsync(string name)
    {
        if (await _scopeManager.FindByNameAsync(name).ConfigureAwait(false) == null)
        {
            await _scopeManager
                .CreateAsync(
                    new OpenIddictScopeDescriptor
                    {
                        Name = name,
                        DisplayName = name + " API",
                        Resources = { name },
                    }
                )
                .ConfigureAwait(false);
        }
    }

    private async Task CreateClientAsync(
        [NotNull] string name,
        string displayName,
        [NotNull] string consentType,
        string? secret,
        List<string> grantTypes,
        List<string> scopes,
        string[]? redirectUris = null,
        string[]? postLogoutRedirectUris = null,
        string? clientUri = null,
        List<string>? permissions = null
    )
    {
        var type = string.IsNullOrWhiteSpace(secret)
            ? OpenIddictConstants.ClientTypes.Public
            : OpenIddictConstants.ClientTypes.Confidential;

        if (
            !string.IsNullOrEmpty(secret)
            && string.Equals(type, OpenIddictConstants.ClientTypes.Public, StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new BusinessException(l["NoClientSecretCanBeSetForPublicApplications"]);
        }

        if (
            string.IsNullOrEmpty(secret)
            && string.Equals(type, OpenIddictConstants.ClientTypes.Confidential, StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new BusinessException(l["TheClientSecretIsRequiredForConfidentialApplications"]);
        }

        var client = await _applicationManager.FindByClientIdAsync(name).ConfigureAwait(false);
        if (client != null)
        {
            var descriptor = new AbpApplicationDescriptor();
            await _applicationManager.PopulateAsync(descriptor, client).ConfigureAwait(false);
            ApplyClientSettings(
                descriptor,
                type,
                grantTypes,
                scopes,
                redirectUris,
                postLogoutRedirectUris
            );
            if (!string.IsNullOrWhiteSpace(clientUri))
            {
                descriptor.ClientUri = clientUri;
            }

            await _applicationManager.UpdateAsync(client, descriptor).ConfigureAwait(false);
            return;
        }

        if (client == null)
        {
            var application = new AbpApplicationDescriptor
            {
                ClientId = name,
                ApplicationType = type,
                ClientSecret = secret,
                ConsentType = consentType,
                DisplayName = displayName,
                ClientUri = clientUri,
            };

            Check.NotNullOrEmpty(grantTypes, nameof(grantTypes));
            Check.NotNullOrEmpty(scopes, nameof(scopes));

            ApplyClientSettings(
                application,
                type,
                grantTypes,
                scopes,
                redirectUris,
                postLogoutRedirectUris
            );

            if (permissions != null)
            {
                await _permissionDataSeeder
                    .SeedAsync(ClientPermissionValueProvider.ProviderName, name, permissions, null)
                    .ConfigureAwait(false);
            }

            await _applicationManager.CreateAsync(application).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 把配置应用到客户端描述符。**配置即事实**：先清空由配置拥有的集合再重建，
    /// 这样重跑迁移能让客户端收敛回配置，而不是在历史值上不断累加。
    /// （历史事故遗留：混血的 RedirectUris/Permissions 一旦写入就再也不会被清掉。）
    /// </summary>
    private void ApplyClientSettings(
        AbpApplicationDescriptor application,
        string type,
        List<string> grantTypes,
        List<string> scopes,
        string[]? redirectUris,
        string[]? postLogoutRedirectUris
    )
    {
        application.ApplicationType = type;
        application.Permissions.Clear();
        application.Requirements.Clear();
        application.RedirectUris.Clear();
        application.PostLogoutRedirectUris.Clear();

        if (
            new[] { OpenIddictConstants.GrantTypes.AuthorizationCode, OpenIddictConstants.GrantTypes.Implicit }.All(
                grantTypes.Contains
            )
        )
        {
            application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.CodeIdToken);
            if (string.Equals(type, OpenIddictConstants.ClientTypes.Public, StringComparison.OrdinalIgnoreCase))
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.CodeIdTokenToken);
                application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.CodeToken);
            }
        }

        application.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.EndSession);

        foreach (var grantType in grantTypes)
        {
            if (grantType == OpenIddictConstants.GrantTypes.AuthorizationCode)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode);
                application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);
                if (string.Equals(type, OpenIddictConstants.ClientTypes.Public, StringComparison.OrdinalIgnoreCase))
                {
                    application.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);
                }
            }

            if (
                grantType
                is OpenIddictConstants.GrantTypes.AuthorizationCode
                    or OpenIddictConstants.GrantTypes.Implicit
            )
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
            }

            if (
                grantType
                is OpenIddictConstants.GrantTypes.AuthorizationCode
                    or OpenIddictConstants.GrantTypes.ClientCredentials
                    or OpenIddictConstants.GrantTypes.Password
                    or OpenIddictConstants.GrantTypes.RefreshToken
                    or OpenIddictConstants.GrantTypes.DeviceCode
            )
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
                application.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Revocation);
                application.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Introspection);
            }

            if (grantType == OpenIddictConstants.GrantTypes.ClientCredentials)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);
            }

            if (grantType == OpenIddictConstants.GrantTypes.Implicit)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.Implicit);
            }

            if (grantType == OpenIddictConstants.GrantTypes.Password)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.Password);
            }

            if (grantType == OpenIddictConstants.GrantTypes.RefreshToken)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.RefreshToken);
            }

            if (grantType == OpenIddictConstants.GrantTypes.DeviceCode)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.DeviceCode);
                application.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.DeviceAuthorization);
            }

            if (grantType == OpenIddictConstants.GrantTypes.Implicit)
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.IdToken);
                if (string.Equals(type, OpenIddictConstants.ClientTypes.Public, StringComparison.OrdinalIgnoreCase))
                {
                    application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.IdTokenToken);
                    application.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Token);
                }
            }
        }

        var buildInScopes = new[]
        {
            OpenIddictConstants.Permissions.Scopes.Address,
            OpenIddictConstants.Permissions.Scopes.Email,
            OpenIddictConstants.Permissions.Scopes.Phone,
            OpenIddictConstants.Permissions.Scopes.Profile,
            OpenIddictConstants.Permissions.Scopes.Roles,
            OpenIddictConstants.Scopes.OpenId,
            "offline_access",
        };

        foreach (var scope in scopes)
        {
            if (buildInScopes.Contains(scope))
            {
                application.Permissions.Add(scope);
            }
            else
            {
                application.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scope);
            }
        }

        if (redirectUris != null)
        {
            foreach (var redirectUri in redirectUris)
            {
                if (!redirectUri.IsNullOrEmpty())
                {
                    if (
                        !Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)
                        || !uri.IsWellFormedOriginalString()
                    )
                    {
                        throw new BusinessException(l["InvalidRedirectUri", redirectUri]);
                    }

                    if (application.RedirectUris.All(x => x != uri))
                    {
                        application.RedirectUris.Add(uri);
                    }
                }
            }
        }

        if (postLogoutRedirectUris != null)
        {
            foreach (var postLogoutRedirectUri in postLogoutRedirectUris)
            {
                if (!postLogoutRedirectUri.IsNullOrEmpty())
                {
                    if (
                        !Uri.TryCreate(postLogoutRedirectUri, UriKind.Absolute, out var uri)
                        || !uri.IsWellFormedOriginalString()
                    )
                    {
                        throw new BusinessException(l["InvalidPostLogoutRedirectUri", postLogoutRedirectUri]);
                    }

                    if (application.PostLogoutRedirectUris.All(x => x != uri))
                    {
                        application.PostLogoutRedirectUris.Add(uri);
                    }
                }
            }
        }
    }
}
