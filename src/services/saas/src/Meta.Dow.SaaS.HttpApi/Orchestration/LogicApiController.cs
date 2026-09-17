using System.Text.Json;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Orchestration;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace Meta.Dow.SaaS.Orchestration;

[Area(SaaSRemoteServiceConsts.ModuleName)]
[RemoteService(Name = SaaSRemoteServiceConsts.RemoteServiceName)]
[Route("api/logic")]
public class LogicApiController : SaaSController
{
    private readonly ILogicApiAppService _service;

    public LogicApiController(ILogicApiAppService service)
    {
        _service = service;
    }

    /// <summary>已发布逻辑系统 API：POST /api/logic/{flowKey}</summary>
    [HttpPost]
    [Route("{flowKey}")]
    public async Task<IActionResult> RunAsync(string flowKey, [FromBody] JsonElement? body)
    {
        var bodyJson = body is { ValueKind: JsonValueKind.Object or JsonValueKind.Array }
            ? body.Value.GetRawText()
            : "{}";

        var result = await _service.RunAsync(flowKey, bodyJson);

        object? data = null;
        if (!string.IsNullOrWhiteSpace(result.DataJson))
        {
            data = JsonSerializer.Deserialize<object>(result.DataJson);
        }

        return Ok(new
        {
            success = result.Success,
            instanceId = result.InstanceId,
            data,
            error = result.Error,
            meta = new
            {
                visibleFields = result.Meta.VisibleFields,
                omittedFieldCount = result.Meta.OmittedFieldCount,
                flowKey = result.Meta.FlowKey,
                version = result.Meta.Version
            }
        });
    }
}

[Area(SaaSRemoteServiceConsts.ModuleName)]
[RemoteService(Name = SaaSRemoteServiceConsts.RemoteServiceName)]
[Route("api/orchestration")]
public class OrchestrationSystemController : SaaSController
{
    private readonly ILogicApiAppService _service;

    public OrchestrationSystemController(ILogicApiAppService service)
    {
        _service = service;
    }

    [HttpGet]
    [Route("system-parameters")]
    public Task<SystemParameterCatalogDto> GetSystemParametersAsync()
    {
        return _service.GetSystemParametersAsync();
    }

    [HttpPost]
    [Route("system-parameters/preview-date")]
    public Task<DateExpressionPreviewDto> PreviewDateExpressionAsync([FromBody] DateExpressionPreviewInput input)
    {
        return _service.PreviewDateExpressionAsync(input);
    }
}
