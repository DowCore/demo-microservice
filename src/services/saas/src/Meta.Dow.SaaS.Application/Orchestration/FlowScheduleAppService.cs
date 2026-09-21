using System;
using System.Linq;
using System.Threading.Tasks;
using Cronos;
using Meta.Dow.SaaS.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Meta.Dow.SaaS.Orchestration;

public class FlowScheduleAppService : SaaSAppService, IFlowScheduleAppService
{
    private readonly IRepository<FlowSchedule, Guid> _repository;
    private readonly IRepository<FlowDefinition, Guid> _definitionRepository;
    private readonly IPublishedFlowInvoker _invoker;

    public FlowScheduleAppService(
        IRepository<FlowSchedule, Guid> repository,
        IRepository<FlowDefinition, Guid> definitionRepository,
        IPublishedFlowInvoker invoker
    )
    {
        _repository = repository;
        _definitionRepository = definitionRepository;
        _invoker = invoker;
    }

    [Authorize(OrchestrationPermissions.Schedules.Default)]
    public async Task<PagedResultDto<FlowScheduleDto>> GetListAsync(FlowScheduleGetListInput input)
    {
        var query = await _repository.GetQueryableAsync();
        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var f = input.Filter.Trim();
            query = query.Where(x =>
                x.Name.Contains(f) || x.Code.Contains(f) || x.FlowKey.Contains(f)
            );
        }

        if (input.EnabledOnly == true)
        {
            query = query.Where(x => x.IsEnabled);
        }

        var total = query.Count();
        var items = query
            .OrderByDescending(x => x.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount)
            .ToList();
        return new PagedResultDto<FlowScheduleDto>(total, items.Select(Map).ToList());
    }

    [Authorize(OrchestrationPermissions.Schedules.Default)]
    public async Task<FlowScheduleDto> GetAsync(Guid id) => Map(await _repository.GetAsync(id));

    [Authorize(OrchestrationPermissions.Schedules.Create)]
    public async Task<FlowScheduleDto> CreateAsync(CreateFlowScheduleDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var code = input.Code.Trim();
        if (await _repository.AnyAsync(x => x.Code == code))
        {
            throw new UserFriendlyException(L["Orchestration:ScheduleCodeAlreadyExists", code]);
        }

        await EnsurePublishedFlowAsync(input.FlowKey);
        ValidateCron(input.Cron, input.TimeZone);

        var entity = new FlowSchedule(
            GuidGenerator.Create(),
            code,
            input.Name.Trim(),
            input.FlowKey.Trim(),
            input.Cron.Trim(),
            input.TimeZone,
            input.VariablesJson,
            input.Description,
            CurrentTenant.Id
        );
        entity.SetNextFireAt(ComputeNext(entity.Cron, entity.TimeZone, DateTime.UtcNow));
        await _repository.InsertAsync(entity, autoSave: true);
        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Schedules.Update)]
    public async Task<FlowScheduleDto> UpdateAsync(Guid id, UpdateFlowScheduleDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var entity = await _repository.GetAsync(id);
        await EnsurePublishedFlowAsync(input.FlowKey);
        ValidateCron(input.Cron, input.TimeZone);

        entity.Update(
            input.Name.Trim(),
            input.FlowKey.Trim(),
            input.Cron.Trim(),
            input.TimeZone,
            input.VariablesJson,
            input.IsEnabled,
            input.Description
        );
        entity.SetNextFireAt(ComputeNext(entity.Cron, entity.TimeZone, DateTime.UtcNow));
        await _repository.UpdateAsync(entity, autoSave: true);
        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Schedules.Delete)]
    public async Task DeleteAsync(Guid id) => await _repository.DeleteAsync(id, autoSave: true);

    [Authorize(OrchestrationPermissions.Schedules.Run)]
    public async Task<LogicRunResultDto> RunNowAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        var result = await _invoker.InvokeAsync(
            entity.FlowKey,
            entity.VariablesJson ?? "{}",
            triggerSource: $"Schedule:{entity.Code}",
            tenantId: CurrentTenant.Id,
            filterOutputsByRole: false
        );

        return new LogicRunResultDto
        {
            Success = result.Success,
            InstanceId = result.InstanceId,
            DataJson = result.DataJson,
            Error = result.Error,
            Meta = new LogicRunMetaDto
            {
                FlowKey = result.FlowKey,
                Version = result.Version,
                ExecuteMs = result.ExecuteMs,
                TotalMs = result.ExecuteMs
            }
        };
    }

    private async Task EnsurePublishedFlowAsync(string flowKey)
    {
        var key = flowKey.Trim();
        var def = await _definitionRepository.FirstOrDefaultAsync(x => x.Code == key);
        if (def == null || def.Status != FlowDefinitionStatus.Published || !def.PublishedVersion.HasValue)
        {
            throw new UserFriendlyException(L["Orchestration:FlowNotFound", key]);
        }
    }

    internal static void ValidateCron(string cron, string? timeZone)
    {
        try
        {
            CronExpression.Parse(cron.Trim(), CronFormat.IncludeSeconds);
        }
        catch
        {
            try
            {
                CronExpression.Parse(cron.Trim());
            }
            catch (Exception ex)
            {
                throw new UserFriendlyException($"Invalid cron: {ex.Message}");
            }
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(
                string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone.Trim()
            );
        }
        catch
        {
            // Windows / Linux 时区 Id 不同；再试 UTC
            if (!string.Equals(timeZone, "UTC", StringComparison.OrdinalIgnoreCase))
            {
                throw new UserFriendlyException($"Unknown time zone: {timeZone}");
            }
        }
    }

    internal static DateTime? ComputeNext(string cron, string timeZoneId, DateTime utcNow)
    {
        var expr = TryParseCron(cron);
        if (expr == null)
        {
            return null;
        }

        // Cronos 要求 from 为 UTC；返回值统一标成 Utc，避免序列化丢 Z 后被前端当本地时间
        var fromUtc =
            utcNow.Kind == DateTimeKind.Utc
                ? utcNow
                : DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        var tz = ResolveTimeZone(timeZoneId);
        var next = expr.GetNextOccurrence(fromUtc, tz);
        return next.HasValue ? DateTime.SpecifyKind(next.Value, DateTimeKind.Utc) : null;
    }

    internal static CronExpression? TryParseCron(string cron)
    {
        try
        {
            return CronExpression.Parse(cron.Trim(), CronFormat.IncludeSeconds);
        }
        catch
        {
            try
            {
                return CronExpression.Parse(cron.Trim());
            }
            catch
            {
                return null;
            }
        }
    }

    internal static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        var id = string.IsNullOrWhiteSpace(timeZoneId) ? "UTC" : timeZoneId.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch
        {
            if (id is "Asia/Shanghai" or "China Standard Time")
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
                }
                catch
                {
                    try
                    {
                        return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
                    }
                    catch
                    {
                        /* fallthrough */
                    }
                }
            }

            return TimeZoneInfo.Utc;
        }
    }

    private static FlowScheduleDto Map(FlowSchedule x)
    {
        var utcNow = DateTime.UtcNow;
        var next = AsUtc(x.NextFireAt);
        // 调度主机未跑 / 上次触发失败时库里会停在过去；列表按 Cron 重算「真正的下次」
        if (x.IsEnabled && (next == null || next.Value <= utcNow))
        {
            next = ComputeNext(x.Cron, x.TimeZone, utcNow);
        }

        return new FlowScheduleDto
        {
            Id = x.Id,
            Code = x.Code,
            Name = x.Name,
            FlowKey = x.FlowKey,
            Cron = x.Cron,
            TimeZone = x.TimeZone,
            VariablesJson = x.VariablesJson,
            IsEnabled = x.IsEnabled,
            Description = x.Description,
            LastFiredAt = AsUtc(x.LastFiredAt),
            NextFireAt = next,
            CreationTime = x.CreationTime,
            LastModificationTime = x.LastModificationTime
        };
    }

    private static DateTime? AsUtc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
}
