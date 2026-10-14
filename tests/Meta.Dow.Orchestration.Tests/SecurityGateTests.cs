using System.Net;
using Meta.Dow.SaaS.Orchestration;
using Xunit;

namespace Meta.Dow.Orchestration.Tests;

/// <summary>
/// 出站与内联 SQL 的安全闸门测试（A7 / A8）。
///
/// 这两条闸门原先都无法测试：
/// <list type="bullet">
/// <item>SQL 写判定内联在 <c>CodeSandboxExecutor</c> 里，而该类依赖四个注入的外部执行器，
///       测试脚手架一构造就NRE；</item>
/// <item>出站 URL 全程没有任何校验，因此没有"应该被拒"的样本可比对。</item>
/// </list>
/// 修复后两者都拆成纯静态类，判定逻辑可被完整覆盖。
///
/// <b>测试取向</b>：闸门类代码的核心属性是<b>保守</b>——宁可误拒（多要权限）不可漏放（越权）。
/// 因此断言全部按"漏放方向"写：任何可能构成绕过的样本都必须判为禁止。
/// </summary>
public class SecurityGateTests
{
    // ==================================================================
    // A8：SQL 写判定
    // ==================================================================

    [Theory]
    // 明确只读
    [InlineData("SELECT * FROM t")]
    [InlineData("select id from t where name = 'x'")]
    [InlineData("  \n\t SELECT 1  ")]
    [InlineData("(SELECT 1)")]
    [InlineData("((SELECT 1))")]
    [InlineData("SHOW TABLES")]
    [InlineData("EXPLAIN SELECT 1")]
    [InlineData("DESCRIBE t")]
    // WITH … SELECT 是读（这是 A8 修复的核心：原先一律当读，故这里必须仍是读）
    [InlineData("WITH x AS (SELECT 1 AS a) SELECT * FROM x")]
    [InlineData("WITH RECURSIVE r AS (SELECT 1 UNION ALL SELECT 2) SELECT * FROM r")]
    public void Read_only_sql_is_not_write(string sql)
    {
        Assert.False(SqlWriteClassifier.IsWrite(sql));
    }

    [Theory]
    // 直接写
    [InlineData("DELETE FROM t")]
    [InlineData("delete from t where id = 1")]
    [InlineData("UPDATE t SET a = 1")]
    [InlineData("INSERT INTO t VALUES (1)")]
    [InlineData("MERGE INTO t USING s ON …")]
    // DDL 同样按写处理（改结构也是写权限，不该只拦 DML）
    [InlineData("DROP TABLE t")]
    [InlineData("TRUNCATE TABLE t")]
    [InlineData("ALTER TABLE t ADD COLUMN c int")]
    [InlineData("CREATE TABLE t (id int)")]
    // ★ A8 核心绕过点：WITH 前缀的写语句，原先被当成读，无需写权限即可执行
    [InlineData("WITH x AS (SELECT 1 AS a) DELETE FROM t")]
    [InlineData("WITH x AS (SELECT id FROM t) UPDATE t SET a = 1")]
    [InlineData("WITH x AS (SELECT 1) INSERT INTO t SELECT * FROM x")]
    public void Write_sql_is_write(string sql)
    {
        Assert.True(SqlWriteClassifier.IsWrite(sql));
    }

    [Theory]
    // 注释先被剥离：剥完后的真实语义决定判定。
    [InlineData("/* delete */ SELECT 1", false)]              // 剥掉注释后是 SELECT ⇒ 读
    [InlineData("SELECT 1 -- delete\n", false)]              // 行注释里的 delete 不应被误判成写
    [InlineData("/* x */ DELETE FROM t", true)]              // 剥掉注释后是 DELETE ⇒ 写
    [InlineData("-- lead\nDROP TABLE t", true)]              // 注释在首行也不影响判定
    [InlineData("/**/ /* nested */ UPDATE t SET a=1", true)]  // 嵌套块注释
    // 字面量内容不应影响判定：写语句里含'delete' 仍是写，读语句里含 'delete' 仍是读。
    [InlineData("INSERT INTO t VALUES ('delete')", true)]
    [InlineData("SELECT * FROM t WHERE name = 'DROP TABLE'", false)]
    // 转义引号：'it''s delete' 不能提前闭合字面量
    [InlineData("SELECT 'it''s delete' AS s", false)]
    [InlineData("SELECT 'a\\' DELETE FROM t' AS s", false)]
    public void Comments_and_literals_cannot_hide_or_fake_the_keyword(string sql, bool expectedWrite)
    {
        Assert.Equal(expectedWrite, SqlWriteClassifier.IsWrite(sql));
    }

    [Fact]
    public void Literal_only_keyword_does_not_flip_a_select_into_write()
    {
        // 单点断言：SELECT里带'delete' 必须是读。这条若失败说明字面量剥离坏了。
        Assert.False(SqlWriteClassifier.IsWrite("SELECT 'insert update delete drop' AS danger"));
    }

    [Fact]
    public void Comment_only_delete_is_still_write()
    {
        Assert.True(SqlWriteClassifier.IsWrite("/* innocent */ DELETE FROM t"));
    }

    [Fact]
    public void Unknown_statement_is_treated_as_write()
    {
        // 认不出关键字时保守按写处理：多要权限的代价远小于漏放。
        Assert.True(SqlWriteClassifier.IsWrite("VACUUM ANALYZE t"));
        Assert.True(SqlWriteClassifier.IsWrite("???"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_sql_is_not_write(string? sql)
    {
        Assert.False(SqlWriteClassifier.IsWrite(sql));
    }

    // ==================================================================
    // A7：出站 SSRF 闸门
    // ==================================================================

    [Theory]
    // 云元数据是本条闸门的首要目标
    [InlineData("169.254.169.254")]   // AWS / GCP / Azure
    [InlineData("100.100.100.200")]   // 阿里云
    [InlineData("192.0.0.192")]       // Oracle Cloud
    // 回环与本机
    [InlineData("127.0.0.1")]
    [InlineData("127.1.2.3")]
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]           // 0.0.0.0/8 整段
    // RFC1918
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]    // 172.16/12 上界
    [InlineData("192.168.1.1")]
    // 链路本地
    [InlineData("169.254.1.1")]
    // 运营商级 NAT
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.254")]
    // 保留 / 组播
    [InlineData("192.0.0.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.254")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    public void Non_routable_ipv4_is_forbidden(string ip)
    {
        Assert.True(HttpEgressGuard.IsForbiddenAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]     // example.com
    [InlineData("172.32.0.1")]        // 172.16/12 的下界之外，合法公网
    [InlineData("172.15.255.255")]
    [InlineData("100.63.255.255")]    // 100.64/10 之外
    [InlineData("100.128.0.1")]
    [InlineData("11.0.0.1")]           // 与 10/8 相邻但不同
    [InlineData("9.255.255.255")]
    public void Public_ipv4_is_allowed(string ip)
    {
        Assert.False(HttpEgressGuard.IsForbiddenAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    // IPv6 回环与未指定
    [InlineData("::1")]
    [InlineData("::")]
    // 链路本地 fe80::/10
    [InlineData("fe80::1")]
    [InlineData("febf::1")]
    // 唯一本地地址 fc00::/7
    [InlineData("fc00::1")]
    [InlineData("fd00::1")]
    [InlineData("fdff::1")]
    public void Non_routable_ipv6_is_forbidden(string ip)
    {
        Assert.True(HttpEgressGuard.IsForbiddenAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    // ★ IPv4-mapped IPv6：绕过的经典手法。地址看起来是 IPv6，实质指向内网 IPv4。
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:192.168.0.1")]
    public void IPv4_mapped_ipv6_pointing_at_internal_is_forbidden(string ip)
    {
        Assert.True(HttpEgressGuard.IsForbiddenAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("2606:4700:4700::1111")]  // 公网 IPv6
    [InlineData("::ffff:8.8.8.8")]        // 映射到公网 IPv4，应放行
    public void Public_ipv6_is_allowed(string ip)
    {
        Assert.False(HttpEgressGuard.IsForbiddenAddress(IPAddress.Parse(ip)));
    }

    // ------------------------------------------------------------------
    // EnsureAllowed 的协议层判定（不触网，纯字符串层）
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://internal/data")]
    [InlineData("gopher://127.0.0.1:6379/_INFO")]
    [InlineData("jar:net:///evil.jar!/")]
    public void Non_http_scheme_is_rejected(string url)
    {
        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => HttpEgressGuard.EnsureAllowed(url));
        Assert.Contains("scheme", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Localhost_hostname_is_rejected_without_dns()
    {
        // localhost 走字面量判定，不需要 DNS，也不会因解析失败而给出含糊错误。
        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(
            () => HttpEgressGuard.EnsureAllowed("http://localhost:8080/admin"));
        Assert.Contains("localhost", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Loopback_literal_is_rejected()
    {
        Assert.Throws<Volo.Abp.UserFriendlyException>(
            () => HttpEgressGuard.EnsureAllowed("http://127.0.0.1:6379/"));
    }

    [Fact]
    public void Metadata_literal_is_rejected()
    {
        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(
            () => HttpEgressGuard.EnsureAllowed(
                "http://169.254.169.254/latest/meta-data/iam/security-credentials/"));
        Assert.Contains("non-routable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mock_scheme_passes_without_touching_network()
    {
        // mock:// 是设计器预览伪协议，不产生真实外呼，必须放行。
        HttpEgressGuard.EnsureAllowed("mock://anything/at/all");
    }

    [Fact]
    public void Allowlisted_host_bypasses_address_check()
    {
        // 平台自身的内部编排服务需要被回调——豁免必须真实生效，
        // 否则配置了 Orchestration:Http:AllowedHosts 也等于没配。
        HttpEgressGuard.EnsureAllowed("http://127.0.0.1:5000/hook", ["127.0.0.1"]);
        HttpEgressGuard.EnsureAllowed("http://localhost:5000/hook", ["LocalHost"]);
    }

    [Fact]
    public void Allowlist_does_not_cover_other_hosts()
    {
        Assert.Throws<Volo.Abp.UserFriendlyException>(
            () => HttpEgressGuard.EnsureAllowed("http://169.254.169.254/", ["127.0.0.1"]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_url_is_rejected(string? url)
    {
        Assert.Throws<Volo.Abp.UserFriendlyException>(() => HttpEgressGuard.EnsureAllowed(url));
    }

    [Fact]
    public void Relative_url_is_rejected()
    {
        Assert.Throws<Volo.Abp.UserFriendlyException>(() => HttpEgressGuard.EnsureAllowed("/relative/path"));
    }
}