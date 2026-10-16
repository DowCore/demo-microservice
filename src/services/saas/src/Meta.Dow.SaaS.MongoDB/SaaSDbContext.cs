using Meta.Dow.SaaS.Orchestration;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using Volo.Abp.Data;
using Volo.Abp.MongoDB;

namespace Meta.Dow.SaaS.MongoDB;

[ConnectionStringName(MetaDowNames.SaaSDb)]
public class SaaSDbContext : AbpMongoDbContext
{
    public IMongoCollection<FlowDefinition> FlowDefinitions => Collection<FlowDefinition>();

    public IMongoCollection<FlowVersion> FlowVersions => Collection<FlowVersion>();

    public IMongoCollection<FlowInstance> FlowInstances => Collection<FlowInstance>();

    /// <summary>
    /// 异步外呼作业（C1 修复）。原先异步 Http 走进程内 Task.Run，
    /// 没有可持久化的作业记录，结果与失败无处安放。
    /// </summary>
    public IMongoCollection<FlowAsyncJob> FlowAsyncJobs => Collection<FlowAsyncJob>();

    public IMongoCollection<DataSource> DataSources => Collection<DataSource>();

    public IMongoCollection<FlowUsage> FlowUsages => Collection<FlowUsage>();

    public IMongoCollection<MessageSource> MessageSources => Collection<MessageSource>();

    public IMongoCollection<FlowTrigger> FlowTriggers => Collection<FlowTrigger>();

    public IMongoCollection<FlowSchedule> FlowSchedules => Collection<FlowSchedule>();

    public IMongoCollection<FlowScheduleFire> FlowScheduleFires => Collection<FlowScheduleFire>();

    public IMongoCollection<TableDefinition> TableDefinitions => Collection<TableDefinition>();

    public IMongoCollection<DbQueryObject> DbQueryObjects => Collection<DbQueryObject>();

    public IMongoCollection<AppResource> AppResources => Collection<AppResource>();

    public IMongoCollection<ReportDefinition> ReportDefinitions => Collection<ReportDefinition>();

    public IMongoCollection<FormDefinition> FormDefinitions => Collection<FormDefinition>();

    public IMongoCollection<WorkflowDefinition> WorkflowDefinitions => Collection<WorkflowDefinition>();

    public IMongoCollection<WorkflowInstance> WorkflowInstances => Collection<WorkflowInstance>();

    public IMongoCollection<WorkflowTask> WorkflowTasks => Collection<WorkflowTask>();
}
