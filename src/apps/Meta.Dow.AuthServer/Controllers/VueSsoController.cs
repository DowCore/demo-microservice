using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;

namespace Meta.Dow.Controllers;

/// <summary>
/// AuthServer 同域发起 Vue SSO，避免从 http://5666 跨站跳回 https://7600 时丢 Identity Cookie。
/// Visit → /vue-sso → /connect/authorize（同站，已登录则直接发 code）→ Vue callback。
/// </summary>
[Route("vue-sso")]
public class VueSsoController(IDistributedCache cache, IConfiguration configuration) : AbpController
{
    private const string CacheKeyPrefix = "MetaDow:VuePkce:";
    private static readonly TimeSpan PkceTtl = TimeSpan.FromMinutes(10);

    private readonly IDistributedCache _cache = cache;
    private readonly IConfiguration _configuration = configuration;

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> StartAsync()
    {
        var clientId = _configuration["App:VueClientId"] ?? "MetaDow_Vue";
        var redirectUri =
            _configuration["App:VueRedirectUri"] ?? "http://localhost:5666/auth/oidc-callback";
        var scope =
            _configuration["App:VueScope"]
            ?? "openid profile email offline_access MetaDowIdentityService MetaDowAdministration MetaDowSaaS";

        var verifier = CreateCodeVerifier();
        var challenge = CreateCodeChallenge(verifier);
        var state = CreateCodeVerifier()[..43];

        await _cache
            .SetStringAsync(
                CacheKeyPrefix + state,
                verifier,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = PkceTtl }
            )
            .ConfigureAwait(false);

        var query = new QueryString()
            .Add("client_id", clientId)
            .Add("redirect_uri", redirectUri)
            .Add("response_type", "code")
            .Add("scope", scope)
            .Add("state", state)
            .Add("code_challenge", challenge)
            .Add("code_challenge_method", "S256");

        return Redirect("/connect/authorize" + query.Value);
    }

    /// <summary>
    /// Vue 回调用 state 一次性取回 code_verifier（Visit 桥接流程无 sessionStorage）。
    /// </summary>
    [HttpGet("pkce")]
    [AllowAnonymous]
    public async Task<IActionResult> RedeemPkceAsync([FromQuery] string state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            throw new BusinessException("StateIsRequired");
        }

        var key = CacheKeyPrefix + state;
        var verifier = await _cache.GetStringAsync(key).ConfigureAwait(false);
        if (string.IsNullOrEmpty(verifier))
        {
            return NotFound(new { error = "pkce_expired_or_invalid" });
        }

        await _cache.RemoveAsync(key).ConfigureAwait(false);
        return Ok(new { code_verifier = verifier });
    }

    private static string CreateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string CreateCodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
