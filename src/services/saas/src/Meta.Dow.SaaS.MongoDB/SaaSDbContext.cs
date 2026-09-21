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

    public IMongoCollection<DataSource> DataSources => Collection<DataSource>();

    public IMongoCollection<FlowUsage> FlowUsages => Collection<FlowUsage>();

    public IMongoCollection<MessageSource> MessageSources => Collection<MessageSource>();

    public IMongoCollection<FlowTrigger> FlowTriggers => Collection<FlowTrigger>();

    public IMongoCollection<FlowSchedule> FlowSchedules => Collection<FlowSchedule>();
}
