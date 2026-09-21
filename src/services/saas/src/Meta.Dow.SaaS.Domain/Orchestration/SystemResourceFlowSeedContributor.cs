using System;
using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 种子：系统资源 CRUD 逻辑（只读 DSL，一键创建资源时默认绑定）。
/// </summary>
public class SystemResourceFlowSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<FlowDefinition, Guid> _definitionRepository;
    private readonly IRepository<FlowVersion, Guid> _versionRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICurrentTenant _currentTenant;

    public SystemResourceFlowSeedContributor(
        IRepository<FlowDefinition, Guid> definitionRepository,
        IRepository<FlowVersion, Guid> versionRepository,
        IGuidGenerator guidGenerator,
        ICurrentTenant currentTenant
    )
    {
        _definitionRepository = definitionRepository;
        _versionRepository = versionRepository;
        _guidGenerator = guidGenerator;
        _currentTenant = currentTenant;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        using (_currentTenant.Change(context?.TenantId))
        {
            await SeedOneAsync(
                context?.TenantId,
                SystemResourceFlowKeys.Query,
                "系统资源查询",
                "ResourceQuery",
                "query",
                """
                [
                  { "name": "resourceCode", "type": "string", "required": true, "source": "input" },
                  { "name": "page", "type": "number", "source": "input" },
                  { "name": "pageSize", "type": "number", "source": "input" },
                  { "name": "sorting", "type": "string", "source": "input" },
                  { "name": "filters", "type": "object", "source": "input" },
                  { "name": "columns", "type": "array", "source": "input" }
                ]
                """,
                """
                [
                  { "name": "resourceCode", "from": "input.resourceCode" },
                  { "name": "page", "from": "input.page" },
                  { "name": "pageSize", "from": "input.pageSize" },
                  { "name": "sorting", "from": "input.sorting" },
                  { "name": "filters", "from": "input.filters" },
                  { "name": "columns", "from": "input.columns" }
                ]
                """,
                """
                [
                  { "name": "items", "type": "array", "from": "items" },
                  { "name": "total", "type": "number", "from": "total" }
                ]
                """,
                """
                [
                  { "name": "items", "type": "array", "from": "items", "visibleTo": { "mode": "all" } },
                  { "name": "total", "type": "number", "from": "total", "visibleTo": { "mode": "all" } }
                ]
                """
            );
            await SeedOneAsync(
                context?.TenantId,
                SystemResourceFlowKeys.Get,
                "系统资源详情",
                "ResourceGet",
                "get",
                """
                [
                  { "name": "resourceCode", "type": "string", "required": true, "source": "input" },
                  { "name": "id", "type": "string", "required": true, "source": "input" }
                ]
                """,
                """
                [
                  { "name": "resourceCode", "from": "input.resourceCode" },
                  { "name": "id", "from": "input.id" }
                ]
                """,
                """[{ "name": "record", "type": "object", "from": "record" }]""",
                """[{ "name": "record", "type": "object", "from": "record", "visibleTo": { "mode": "all" } }]"""
            );
            await SeedOneAsync(
                context?.TenantId,
                SystemResourceFlowKeys.Create,
                "系统资源新增",
                "ResourceCreate",
                "create",
                """
                [
                  { "name": "resourceCode", "type": "string", "required": true, "source": "input" },
                  { "name": "record", "type": "object", "required": true, "source": "input" }
                ]
                """,
                """
                [
                  { "name": "resourceCode", "from": "input.resourceCode" },
                  { "name": "record", "from": "input.record" }
                ]
                """,
                """[{ "name": "id", "type": "string", "from": "id" }]""",
                """[{ "name": "id", "type": "string", "from": "id", "visibleTo": { "mode": "all" } }]"""
            );
            await SeedOneAsync(
                context?.TenantId,
                SystemResourceFlowKeys.Update,
                "系统资源更新",
                "ResourceUpdate",
                "update",
                """
                [
                  { "name": "resourceCode", "type": "string", "required": true, "source": "input" },
                  { "name": "id", "type": "string", "required": true, "source": "input" },
                  { "name": "concurrencyStamp", "type": "string", "source": "input" },
                  { "name": "record", "type": "object", "required": true, "source": "input" }
                ]
                """,
                """
                [
                  { "name": "resourceCode", "from": "input.resourceCode" },
                  { "name": "id", "from": "input.id" },
                  { "name": "concurrencyStamp", "from": "input.concurrencyStamp" },
                  { "name": "record", "from": "input.record" }
                ]
                """,
                """
                [
                  { "name": "id", "type": "string", "from": "id" },
                  { "name": "concurrencyStamp", "type": "string", "from": "concurrencyStamp" }
                ]
                """,
                """
                [
                  { "name": "id", "type": "string", "from": "id", "visibleTo": { "mode": "all" } },
                  { "name": "concurrencyStamp", "type": "string", "from": "concurrencyStamp", "visibleTo": { "mode": "all" } }
                ]
                """
            );
            await SeedOneAsync(
                context?.TenantId,
                SystemResourceFlowKeys.Delete,
                "系统资源删除",
                "ResourceDelete",
                "delete",
                """
                [
                  { "name": "resourceCode", "type": "string", "required": true, "source": "input" },
                  { "name": "id", "type": "string", "required": true, "source": "input" }
                ]
                """,
                """
                [
                  { "name": "resourceCode", "from": "input.resourceCode" },
                  { "name": "id", "from": "input.id" }
                ]
                """,
                """
                [
                  { "name": "id", "type": "string", "from": "id" },
                  { "name": "deleted", "type": "boolean", "from": "deleted" }
                ]
                """,
                """
                [
                  { "name": "id", "type": "string", "from": "id", "visibleTo": { "mode": "all" } },
                  { "name": "deleted", "type": "boolean", "from": "deleted", "visibleTo": { "mode": "all" } }
                ]
                """
            );
        }
    }

    private async Task SeedOneAsync(
        Guid? tenantId,
        string code,
        string name,
        string nodeType,
        string nodeRef,
        string inputsJson,
        string nodeInputsJson,
        string nodeOutputsJson,
        string finalOutputsJson
    )
    {
        var dslJson = BuildDsl(code, inputsJson, nodeType, nodeRef, nodeInputsJson, nodeOutputsJson, finalOutputsJson);
        var existing = await _definitionRepository.FirstOrDefaultAsync(x => x.Code == code);
        if (existing != null)
        {
            existing.MarkSystem();
            existing.SetReusable(true);
            existing.UpdateDraft(name, "系统", graphJson: "{}", dslJson: dslJson, isReusable: true);
            var next = (existing.PublishedVersion ?? 0) + 1;
            existing.MarkPublished(next);
            await _definitionRepository.UpdateAsync(existing, autoSave: true);
            await _versionRepository.InsertAsync(
                new FlowVersion(
                    _guidGenerator.Create(),
                    existing.Id,
                    version: next,
                    graphJson: "{}",
                    dslJson: dslJson,
                    tenantId: tenantId
                ),
                autoSave: true
            );
            return;
        }

        var definitionId = _guidGenerator.Create();
        var definition = new FlowDefinition(
            definitionId,
            name,
            code,
            "系统",
            tenantId,
            isReusable: true,
            isSystem: true
        );
        definition.UpdateDraft(name, "系统", graphJson: "{}", dslJson: dslJson, isReusable: true);
        definition.MarkPublished(1);
        await _definitionRepository.InsertAsync(definition, autoSave: true);
        await _versionRepository.InsertAsync(
            new FlowVersion(
                _guidGenerator.Create(),
                definitionId,
                version: 1,
                graphJson: "{}",
                dslJson: dslJson,
                tenantId: tenantId
            ),
            autoSave: true
        );
    }

    private static string BuildDsl(
        string key,
        string inputsJson,
        string nodeType,
        string nodeRef,
        string nodeInputsJson,
        string nodeOutputsJson,
        string finalOutputsJson
    )
    {
        return $$"""
{
  "version": "1.1",
  "key": "{{key}}",
  "trigger": { "type": "Manual" },
  "inputs": {{inputsJson}},
  "outputs": {{finalOutputsJson}},
  "nodes": [
    { "id": "start", "type": "Start" },
    {
      "id": "{{nodeRef}}",
      "type": "{{nodeType}}",
      "ref": "{{nodeRef}}",
      "inputs": {{nodeInputsJson}},
      "outputs": {{nodeOutputsJson}}
    },
    {
      "id": "end",
      "type": "End",
      "finalOutputs": {{finalOutputsJson}}
    }
  ],
  "edges": [
    { "source": "start", "target": "{{nodeRef}}" },
    { "source": "{{nodeRef}}", "target": "end" }
  ]
}
""";
    }
}
