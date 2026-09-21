using System;
using Meta.Dow.SaaS.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace Meta.Dow.SaaS.Permissions;

public class SaaSPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var saasGroup = context.AddGroup(SaaSPermissions.GroupName, L("Permission:SaaS"));

        var tenantsPermission = saasGroup.AddPermission(SaaSPermissions.Tenants.Default, L("Permission:SaaS:Tenants"));
        tenantsPermission.AddChild(SaaSPermissions.Tenants.Create, L("Permission:SaaS:Tenants.Create"));
        tenantsPermission.AddChild(SaaSPermissions.Tenants.Update, L("Permission:SaaS:Tenants.Update"));
        tenantsPermission.AddChild(SaaSPermissions.Tenants.Delete, L("Permission:SaaS:Tenants.Delete"));

        var orchestrationGroup = context.AddGroup(OrchestrationPermissions.GroupName, L("Permission:Orchestration"));

        var definitions = orchestrationGroup.AddPermission(
            OrchestrationPermissions.Definitions.Default,
            L("Permission:Orchestration:Definitions")
        );
        definitions.AddChild(OrchestrationPermissions.Definitions.Create, L("Permission:Orchestration:Definitions.Create"));
        definitions.AddChild(OrchestrationPermissions.Definitions.Update, L("Permission:Orchestration:Definitions.Update"));
        definitions.AddChild(OrchestrationPermissions.Definitions.Delete, L("Permission:Orchestration:Definitions.Delete"));
        definitions.AddChild(OrchestrationPermissions.Definitions.Publish, L("Permission:Orchestration:Definitions.Publish"));

        var instances = orchestrationGroup.AddPermission(
            OrchestrationPermissions.Instances.Default,
            L("Permission:Orchestration:Instances")
        );
        instances.AddChild(OrchestrationPermissions.Instances.Run, L("Permission:Orchestration:Instances.Run"));
        instances.AddChild(OrchestrationPermissions.Instances.Cancel, L("Permission:Orchestration:Instances.Cancel"));
        instances.AddChild(OrchestrationPermissions.Instances.Delete, L("Permission:Orchestration:Instances.Delete"));

        var dataSources = orchestrationGroup.AddPermission(
            OrchestrationPermissions.DataSources.Default,
            L("Permission:Orchestration:DataSources")
        );
        dataSources.AddChild(OrchestrationPermissions.DataSources.Create, L("Permission:Orchestration:DataSources.Create"));
        dataSources.AddChild(OrchestrationPermissions.DataSources.Update, L("Permission:Orchestration:DataSources.Update"));
        dataSources.AddChild(OrchestrationPermissions.DataSources.Delete, L("Permission:Orchestration:DataSources.Delete"));
        dataSources.AddChild(OrchestrationPermissions.DataSources.Ddl, L("Permission:Orchestration:DataSources.Ddl"));

        var tables = orchestrationGroup.AddPermission(
            OrchestrationPermissions.Tables.Default,
            L("Permission:Orchestration:Tables")
        );
        tables.AddChild(OrchestrationPermissions.Tables.Create, L("Permission:Orchestration:Tables.Create"));
        tables.AddChild(OrchestrationPermissions.Tables.Update, L("Permission:Orchestration:Tables.Update"));
        tables.AddChild(OrchestrationPermissions.Tables.Delete, L("Permission:Orchestration:Tables.Delete"));
        tables.AddChild(OrchestrationPermissions.Tables.Ddl, L("Permission:Orchestration:Tables.Ddl"));

        var resources = orchestrationGroup.AddPermission(
            OrchestrationPermissions.Resources.Default,
            L("Permission:Orchestration:Resources")
        );
        resources.AddChild(OrchestrationPermissions.Resources.Create, L("Permission:Orchestration:Resources.Create"));
        resources.AddChild(OrchestrationPermissions.Resources.Update, L("Permission:Orchestration:Resources.Update"));
        resources.AddChild(OrchestrationPermissions.Resources.Delete, L("Permission:Orchestration:Resources.Delete"));
        resources.AddChild(OrchestrationPermissions.Resources.Publish, L("Permission:Orchestration:Resources.Publish"));
        resources.AddChild(OrchestrationPermissions.Resources.Execute, L("Permission:Orchestration:Resources.Execute"));

        var reports = orchestrationGroup.AddPermission(
            OrchestrationPermissions.Reports.Default,
            L("Permission:Orchestration:Reports")
        );
        reports.AddChild(OrchestrationPermissions.Reports.Create, L("Permission:Orchestration:Reports.Create"));
        reports.AddChild(OrchestrationPermissions.Reports.Update, L("Permission:Orchestration:Reports.Update"));
        reports.AddChild(OrchestrationPermissions.Reports.Delete, L("Permission:Orchestration:Reports.Delete"));
        reports.AddChild(OrchestrationPermissions.Reports.Publish, L("Permission:Orchestration:Reports.Publish"));
        reports.AddChild(OrchestrationPermissions.Reports.Execute, L("Permission:Orchestration:Reports.Execute"));

        var messageSources = orchestrationGroup.AddPermission(
            OrchestrationPermissions.MessageSources.Default,
            L("Permission:Orchestration:MessageSources")
        );
        messageSources.AddChild(OrchestrationPermissions.MessageSources.Create, L("Permission:Orchestration:MessageSources.Create"));
        messageSources.AddChild(OrchestrationPermissions.MessageSources.Update, L("Permission:Orchestration:MessageSources.Update"));
        messageSources.AddChild(OrchestrationPermissions.MessageSources.Delete, L("Permission:Orchestration:MessageSources.Delete"));

        var triggers = orchestrationGroup.AddPermission(
            OrchestrationPermissions.Triggers.Default,
            L("Permission:Orchestration:Triggers")
        );
        triggers.AddChild(OrchestrationPermissions.Triggers.Create, L("Permission:Orchestration:Triggers.Create"));
        triggers.AddChild(OrchestrationPermissions.Triggers.Update, L("Permission:Orchestration:Triggers.Update"));
        triggers.AddChild(OrchestrationPermissions.Triggers.Delete, L("Permission:Orchestration:Triggers.Delete"));

        var schedules = orchestrationGroup.AddPermission(
            OrchestrationPermissions.Schedules.Default,
            L("Permission:Orchestration:Schedules")
        );
        schedules.AddChild(OrchestrationPermissions.Schedules.Create, L("Permission:Orchestration:Schedules.Create"));
        schedules.AddChild(OrchestrationPermissions.Schedules.Update, L("Permission:Orchestration:Schedules.Update"));
        schedules.AddChild(OrchestrationPermissions.Schedules.Delete, L("Permission:Orchestration:Schedules.Delete"));
        schedules.AddChild(OrchestrationPermissions.Schedules.Run, L("Permission:Orchestration:Schedules.Run"));

        orchestrationGroup.AddPermission(
            OrchestrationPermissions.Sql.Write,
            L("Permission:Orchestration:Sql.Write")
        );
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<SaaSResource>(name);
    }
}
