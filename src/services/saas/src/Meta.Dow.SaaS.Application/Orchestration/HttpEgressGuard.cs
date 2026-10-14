using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Volo.Abp;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// Http 出站白名单（SSRF 防护）。
///
/// <para><b>缺陷 S5 的机理</b>：Http 节点的 url 由节点配置 + 模板拼出、headers 完全任意，
/// 全仓没有任何 host 白名单、私网 / 回环 / 链路本地拦截。多租户环境下，
/// 一个可被低代码用户配置的出站 HTTP 节点就是内网探测面：
/// 能读内网 HTTP 服务、探测端口、拿云厂商元数据（<c>169.254.169.254</c>）。</para>
///
/// <para><b>设计取舍</b>：判定放在<b>发起请求之前</b>，而不是靠 <c>HttpClient</c> 事后补救。
/// 后者（如禁止重定向、限制重定向次数）无法防住首次直连，也防不住 DNS 重绑定。</para>
///
/// <para><b>可配置豁免</b>：平台自身的内部编排服务需要被回调，
/// 因此允许通过配置项声明可信主机白名单，而不是硬编码。</para>
/// </summary>
public static class HttpEgressGuard
{
    /// <summary>云厂商元数据端点（AWS/GCP/Azure/阿里云一致）。</summary>
    private static readonly IPAddress[] CloudMetadataAddresses =
    [
        IPAddress.Parse("169.254.169.254"),
        IPAddress.Parse("100.100.100.200"),
        IPAddress.Parse("192.0.0.192")
    ];

    /// <summary>
    /// 校验出站 URL 是否允许访问。不合法或命中内网一律拒绝。
    /// </summary>
    /// <param name="url">待校验的完整 URL。</param>
    /// <param name="allowedHosts">可信主机白名单（主机名或 IP 字面量，大小写不敏感）。</param>
    /// <exception cref="UserFriendlyException">协议不合法、缺主机、缺少出站许可，或解析到内网地址。</exception>
    public static void EnsureAllowed(string? url, string[]? allowedHosts = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new UserFriendlyException("Http requires url.");
        }

        // mock:// 是设计器预览用的伪协议，不产生真实外呼。
        if (url.StartsWith("mock://", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new UserFriendlyException($"Http url is not a valid absolute URI: {url}");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new UserFriendlyException(
                $"Http url scheme '{uri.Scheme}' is not allowed; only http and https are permitted."
            );
        }

        if (IsHostAllowlisted(uri.Host, allowedHosts))
        {
            return;
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new UserFriendlyException("Http url must not target localhost.");
        }

        IPAddress[] addresses;
        try
        {
            var parsed = Dns.GetHostAddresses(uri.Host);
            if (parsed.Length == 0)
            {
                throw new UserFriendlyException($"Http url host could not be resolved: {uri.Host}");
            }

            addresses = parsed;
        }
        catch (UserFriendlyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new UserFriendlyException($"Http url host could not be resolved: {uri.Host} ({ex.Message})");
        }

        foreach (var address in addresses)
        {
            if (IsForbiddenAddress(address))
            {
                throw new UserFriendlyException(
                    $"Http url host '{uri.Host}' resolves to a non-routable address ({address}). " +
                    "Outbound calls to private, loopback, link-local or metadata addresses are blocked. " +
                    "If this host is trusted, add it to Orchestration:Http:AllowedHosts."
                );
            }
        }
    }

    private static bool IsHostAllowlisted(string host, string[]? allowedHosts)
    {
        if (allowedHosts is not { Length: > 0 })
        {
            return false;
        }

        return allowedHosts.Any(x =>
            !string.IsNullOrWhiteSpace(x) &&
            x.Trim().Equals(host, StringComparison.OrdinalIgnoreCase)
        );
    }

    /// <summary>判断单个解析结果是否属于禁止访问的地址段。</summary>
    public static bool IsForbiddenAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (CloudMetadataAddresses.Any(x => x.Equals(address)))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return IsForbiddenAddressV6(address);
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return true;
        }

        // 0.0.0.0/8（本机）
        if (bytes[0] == 0)
        {
            return true;
        }

        // 10.0.0.0/8
        if (bytes[0] == 10)
        {
            return true;
        }

        // 172.16.0.0/12
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
        {
            return true;
        }

        // 192.168.0.0/16
        if (bytes[0] == 192 && bytes[1] == 168)
        {
            return true;
        }

        // 169.254.0.0/16 链路本地（含云元数据）
        if (bytes[0] == 169 && bytes[1] == 254)
        {
            return true;
        }

        // 100.64.0.0/10 运营商级 NAT
        if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
        {
            return true;
        }

        // 192.0.0.0/24 IETF 协议保留（含 192.0.0.192 元数据）
        if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)
        {
            return true;
        }

        // 198.18.0.0/15 基准测试
        if (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19))
        {
            return true;
        }

        // 224.0.0.0/4 组播、240.0.0.0/4 保留
        if (bytes[0] >= 224)
        {
            return true;
        }

        return false;
    }

    private static bool IsForbiddenAddressV6(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return IsForbiddenAddress(address.MapToIPv4());
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length != 16)
        {
            return true;
        }

        // fe80::/10 链路本地
        if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
        {
            return true;
        }

        // fc00::/7 唯一本地地址（ULA）
        if ((bytes[0] & 0xFE) == 0xFC)
        {
            return true;
        }

        // :: / ::1 未指定与回环
        if (address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None))
        {
            return true;
        }

        // ::ffff:0:0/96 由上面的 IPv4 映射处理

        return false;
    }
}
