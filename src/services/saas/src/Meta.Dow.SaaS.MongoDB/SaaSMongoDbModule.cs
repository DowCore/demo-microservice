using System;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Volo.Abp.MongoDB;
using Volo.Abp.TenantManagement.MongoDB;
using Volo.Abp.Uow;

namespace Meta.Dow.SaaS.MongoDB;

[DependsOn(typeof(AbpMongoDbModule))]
[DependsOn(typeof(AbpTenantManagementMongoDbModule))]
[DependsOn(typeof(SaaSDomainModule))]
[DependsOn(typeof(MetaDowSharedModule))]
public class SaaSMongoDbModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Configure<AbpUnitOfWorkDefaultOptions>(options =>
            options.TransactionBehavior = UnitOfWorkTransactionBehavior.Disabled);

        Configure<AbpDbConnectionOptions>(options =>
            options.Databases.Configure(MetaDowNames.SaaSDb, db => db.MappedConnections.Add("AbpTenantManagement")));

        context.Services.AddMongoDbContext<SaaSDbContext>(options =>
            options.AddDefaultRepositories(includeAllEntities: true)
        );
    }

    /// <summary>
    /// W9：启动时补齐关键索引。原先 ABP 集合零显式索引，Code/flowKey 查询与待办过滤全是全表扫，
    /// 且 Code 仅靠 AnyAsync 预检，并发创建可产生重复。索引创建失败仅告警，不阻断启动。
    /// </summary>
    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        await base.OnApplicationInitializationAsync(context);
        await EnsureIndexesAsync(context.ServiceProvider);
    }

    private static async Task EnsureIndexesAsync(IServiceProvider services)
    {
        var logger = services.GetService<ILogger<SaaSMongoDbModule>>();

        // 表单 / 审批流 / 逻辑编排：租户内 Code 唯一（并发创建也不能出现重复）
        await CreateIndexAsync(services, logger, db => db.Collection<FormDefinition>(),
            Builders<FormDefinition>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.Code),
            unique: true, name: "ux_form_tenant_code");

        await CreateIndexAsync(services, logger, db => db.Collection<WorkflowDefinition>(),
            Builders<WorkflowDefinition>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.Code),
            unique: true, name: "ux_workflow_tenant_code");

        await CreateIndexAsync(services, logger, db => db.Collection<FlowDefinition>(),
            Builders<FlowDefinition>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.Code),
            unique: true, name: "ux_flow_tenant_code");

        // 运行期高频查询
        await CreateIndexAsync(services, logger, db => db.Collection<WorkflowDefinition>(),
            Builders<WorkflowDefinition>.IndexKeys.Ascending(x => x.Code).Ascending(x => x.Status),
            name: "ix_workflow_code_status");

        await CreateIndexAsync(services, logger, db => db.Collection<WorkflowInstance>(),
            Builders<WorkflowInstance>.IndexKeys.Ascending(x => x.WorkflowCode).Ascending(x => x.Status),
            name: "ix_instance_code_status");

        await CreateIndexAsync(services, logger, db => db.Collection<WorkflowInstance>(),
            Builders<WorkflowInstance>.IndexKeys.Ascending(x => x.StarterUserName),
            name: "ix_instance_starter");

        await CreateIndexAsync(services, logger, db => db.Collection<WorkflowTask>(),
            Builders<WorkflowTask>.IndexKeys.Ascending(x => x.InstanceId).Ascending(x => x.NodeId).Ascending(x => x.Kind).Ascending(x => x.Status),
            name: "ix_task_instance_node");

        await CreateIndexAsync(services, logger, db => db.Collection<WorkflowTask>(),
            Builders<WorkflowTask>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.Kind),
            name: "ix_task_status_kind");

        await CreateIndexAsync(services, logger, db => db.Collection<FlowScheduleFire>(),
            Builders<FlowScheduleFire>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.ScheduleId)
                .Ascending(x => x.NominalTimeUtc),
            unique: true,
            name: "ux_fire_cron_nominal",
            partial: Builders<FlowScheduleFire>.Filter.Eq(x => x.TriggerKind, FlowScheduleTriggerKind.Cron));

        await CreateIndexAsync(services, logger, db => db.Collection<FlowScheduleFire>(),
            Builders<FlowScheduleFire>.IndexKeys
                .Ascending(x => x.ScheduleId)
                .Ascending(x => x.Status)
                .Ascending(x => x.NextRetryAt),
            name: "ix_fire_schedule_status_retry");

        await CreateIndexAsync(services, logger, db => db.Collection<FlowScheduleFire>(),
            Builders<FlowScheduleFire>.IndexKeys.Ascending(x => x.ScheduleId).Descending(x => x.StartedAt),
            name: "ix_fire_schedule_started");
    }

    private static async Task CreateIndexAsync<T>(
        IServiceProvider services,
        ILogger? logger,
        Func<SaaSDbContext, IMongoCollection<T>> collection,
        IndexKeysDefinition<T> keys,
        bool unique = false,
        string? name = null,
        FilterDefinition<T>? partial = null)
    {
        try
        {
            var db = services.GetRequiredService<SaaSDbContext>();
            var options = new CreateIndexOptions<T> { Unique = unique, Name = name, PartialFilterExpression = partial };
            await collection(db).Indexes.CreateOneAsync(new CreateIndexModel<T>(keys, options));
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "创建 MongoDB 索引 {Index} 失败（不影响启动，但可能影响性能/唯一性）", name ?? typeof(T).Name);
        }
    }
}
