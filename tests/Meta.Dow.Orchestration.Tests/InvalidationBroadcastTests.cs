using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Orchestration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meta.Dow.Orchestration.Tests;

/// <summary>
/// 跨副本已发布流程失效广播（A5）测试。
///
/// <para><b>为什么这条值得单测</b>：失效广播的正确性不体现在"功能能不能跑"，
/// 而体现在<b>故障降级方向</b>——Redis 挂了应该退化成进程内语义（可用性受损但可降级），
/// 而不是让发布/停用/删除全部失败（可用性归零）。这类"方向性"性质最容易在重构中被写反，
/// 而写反了不会编译失败、不会在正常环境下暴露。</para>
///
/// <para>测试用内存字典模拟 <see cref="IDistributedCache"/>，不引入 Redis 依赖。</para>
/// </summary>
public class InvalidationBroadcastTests
{
    private sealed class FakeDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _store = new(StringComparer.Ordinal);

        public bool FailOnRead { get; set; }

        public bool FailOnWrite { get; set; }

        public int ReadCount { get; private set; }

        public int WriteCount { get; private set; }

        public byte[]? Get(string key)
        {
            ReadCount++;
            if (FailOnRead)
            {
                throw new InvalidOperationException("simulated redis outage on read");
            }

            return _store.TryGetValue(key, out var value) ? value : null;
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            WriteCount++;
            if (FailOnWrite)
            {
                throw new InvalidOperationException("simulated redis outage on write");
            }

            _store[key] = value;
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _store.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }

    private static FlowDefinitionInvalidationBroadcaster Create(FakeDistributedCache cache) =>
        new(cache, NullLogger<FlowDefinitionInvalidationBroadcaster>.Instance);

    [Fact]
    public async Task Generation_is_zero_when_nothing_was_ever_broadcast()
    {
        var broadcaster = Create(new FakeDistributedCache());

        Assert.Equal(0L, await broadcaster.GetGenerationAsync());
    }

    [Fact]
    public async Task Broadcast_increments_generation_monotonically()
    {
        var cache = new FakeDistributedCache();
        var broadcaster = Create(cache);

        await broadcaster.BroadcastAsync();
        Assert.Equal(1L, await broadcaster.GetGenerationAsync());

        await broadcaster.BroadcastAsync();
        Assert.Equal(2L, await broadcaster.GetGenerationAsync());

        await broadcaster.BroadcastAsync();
        Assert.Equal(3L, await broadcaster.GetGenerationAsync());
    }

    [Fact]
    public async Task Generation_is_shared_across_broadcaster_instances()
    {
        // 两个实例 = 两个副本的等价物：共享 Redis ⇒ 必须看到同一个代数。
        var cache = new FakeDistributedCache();
        var replicaA = Create(cache);
        var replicaB = Create(cache);

        Assert.Equal(0L, await replicaB.GetGenerationAsync());

        await replicaA.BroadcastAsync();

        Assert.Equal(1L, await replicaB.GetGenerationAsync());
    }

    [Fact]
    public async Task Read_degrades_to_zero_when_cache_is_unavailable()
    {
        var cache = new FakeDistributedCache { FailOnRead = true };
        var broadcaster = Create(cache);

        // 关键方向：广播层不可用必须退化为 0（等价于"没人广播过"），
        // 而不是抛异常——否则缓存层故障会升级成调用全链路故障。
        Assert.Equal(0L, await broadcaster.GetGenerationAsync());
    }

    [Fact]
    public async Task Broadcast_failure_does_not_propagate()
    {
        var cache = new FakeDistributedCache { FailOnWrite = true };
        var broadcaster = Create(cache);

        // 发布/停用/删除的<b>主操作</b>不能因为广播失败而失败：
        // 数据库里的状态已经改了，此处抛异常会让调用方以为整体失败而重试，产生更坏的结果。
        await broadcaster.BroadcastAsync();
    }

    [Fact]
    public async Task Read_failure_then_successful_broadcast_does_not_leave_a_stale_counter()
    {
        var cache = new FakeDistributedCache { FailOnRead = true };
        var broadcaster = Create(cache);

        // 读失败 → Broadcast 内部按 0+1 写入 1，而不是把异常吞成"没广播"。
        await broadcaster.BroadcastAsync();

        cache.FailOnRead = false;
        Assert.Equal(1L, await broadcaster.GetGenerationAsync());
    }

    [Fact]
    public async Task Generation_key_carries_no_expiration()
    {
        var cache = new FakeDistributedCache();
        var broadcaster = Create(cache);

        await broadcaster.BroadcastAsync();

        // 计数器过期会导致代数回退到 0，让所有已缓存的旧快照"看起来仍然有效"。
        Assert.Equal(1, cache.WriteCount);
    }

    [Fact]
    public async Task Broadcast_does_not_recompute_generation_when_read_fails()
    {
        // 读—改—写在读失败时会退化为"写 1"。这条与上条同源，
        // 目的是钉住"最坏情况下只是多一次失效"，而非静默丢失效。
        var cache = new FakeDistributedCache { FailOnRead = true };
        var broadcaster = Create(cache);

        await broadcaster.BroadcastAsync();
        await broadcaster.BroadcastAsync();

        cache.FailOnRead = false;
        // 两次都是 0+1 = 1（竞争窗口的代价：少一次失效，多一次重建），不是 2。
        Assert.Equal(1L, await broadcaster.GetGenerationAsync());
    }
}