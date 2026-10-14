namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 编排执行期的安全与弹性开关。对应 appsettings 的 <c>Orchestration</c> 节点。
/// </summary>
public class OrchestrationOptions
{
    public const string SectionName = "Orchestration";

    /// <summary>
    /// Http 出站可信主机白名单。
    ///
    /// <para><b>为什么需要白名单</b>：出站默认只允许公网地址（见 <c>HttpEgressGuard</c>）。
    /// 但平台自身的内部编排服务、已备案的企业 API 网关需要被回调，
    /// 这些无法用 IP 段表达，因此在此显式声明。
    /// 名单内主机<b>先于</b>内网判定放行。</para>
    ///
    /// <para>留空即表示「不允许任何内网出站」。</para>
    /// </summary>
    public string[] HttpAllowedHosts { get; set; } = [];

    /// <summary>
    /// 是否禁止 Http 节点跟随重定向。
    ///
    /// <para><b>为什么默认禁止</b>：重定向是 SSRF 的经典绕过手法——
    /// 公网地址 302 跳到 <c>169.254.169.254</c>。判定发生在首跳，
    /// 跟随重定向等于放弃判定。业务确实需要跳转时，应显式开启。</para>
    /// </summary>
    public bool HttpAllowRedirects { get; set; }
}
