using System;
using System.Linq;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Meta.Dow.SaaS.Orchestration;

public class MessageSourceAppService : SaaSAppService, IMessageSourceAppService
{
    private readonly IRepository<MessageSource, Guid> _repository;

    public MessageSourceAppService(IRepository<MessageSource, Guid> repository)
    {
        _repository = repository;
    }

    [Authorize(OrchestrationPermissions.MessageSources.Default)]
    public async Task<PagedResultDto<MessageSourceDto>> GetListAsync(MessageSourceGetListInput input)
    {
        var query = await _repository.GetQueryableAsync();
        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var f = input.Filter.Trim();
            query = query.Where(x => x.Name.Contains(f) || x.Code.Contains(f));
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

        return new PagedResultDto<MessageSourceDto>(total, items.Select(Map).ToList());
    }

    [Authorize(OrchestrationPermissions.MessageSources.Default)]
    public async Task<MessageSourceDto> GetAsync(Guid id) => Map(await _repository.GetAsync(id));

    /// <summary>触发器下拉：启用中的消息连接</summary>
    [Authorize(OrchestrationPermissions.Triggers.Default)]
    public async Task<ListResultDto<MessageSourceLookupDto>> GetLookupAsync()
    {
        var query = await _repository.GetQueryableAsync();
        var items = query
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Name)
            .ToList()
            .Select(x => new MessageSourceLookupDto
            {
                Code = x.Code,
                Name = x.Name,
                Provider = x.Provider
            })
            .ToList();
        return new ListResultDto<MessageSourceLookupDto>(items);
    }

    [Authorize(OrchestrationPermissions.MessageSources.Default)]
    public Task<ListResultDto<MessageSourceProviderOptionDto>> GetProvidersAsync()
    {
        var items = MessageSourceProvider.All
            .Select(p => new MessageSourceProviderOptionDto
            {
                Provider = p,
                DisplayName = MessageSourceProvider.DisplayName(p),
                ConnectionHint = MessageSourceProvider.ConnectionHint(p),
                ConsumeSupported = MessageSourceProvider.IsSupportedForConsume(p)
            })
            .ToList();
        return Task.FromResult(new ListResultDto<MessageSourceProviderOptionDto>(items));
    }

    [Authorize(OrchestrationPermissions.MessageSources.Create)]
    public async Task<MessageSourceDto> CreateAsync(CreateMessageSourceDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var code = input.Code.Trim();
        if (await _repository.AnyAsync(x => x.Code == code))
        {
            throw new UserFriendlyException(L["Orchestration:MessageSourceCodeAlreadyExists", code]);
        }

        if (MessageSourceProvider.RequiresConnectionString(input.Provider) &&
            string.IsNullOrWhiteSpace(input.ConnectionString))
        {
            throw new UserFriendlyException(
                L["Orchestration:MessageSourceConnectionRequired", MessageSourceProvider.DisplayName(input.Provider)]
            );
        }

        var entity = new MessageSource(
            GuidGenerator.Create(),
            code,
            input.Name.Trim(),
            input.Provider,
            input.ConnectionString,
            input.Description,
            CurrentTenant.Id
        );
        await _repository.InsertAsync(entity, autoSave: true);
        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.MessageSources.Update)]
    public async Task<MessageSourceDto> UpdateAsync(Guid id, UpdateMessageSourceDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var entity = await _repository.GetAsync(id);
        string? conn = null;
        if (input.ClearConnectionString)
        {
            conn = "";
        }
        else if (input.ConnectionString != null)
        {
            conn = input.ConnectionString;
        }

        entity.Update(
            input.Name.Trim(),
            input.Provider,
            input.IsEnabled,
            input.Description,
            conn
        );

        if (MessageSourceProvider.RequiresConnectionString(entity.Provider) &&
            string.IsNullOrWhiteSpace(entity.ConnectionString))
        {
            throw new UserFriendlyException(
                L["Orchestration:MessageSourceConnectionRequired", MessageSourceProvider.DisplayName(entity.Provider)]
            );
        }

        await _repository.UpdateAsync(entity, autoSave: true);
        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.MessageSources.Delete)]
    public async Task DeleteAsync(Guid id) => await _repository.DeleteAsync(id, autoSave: true);

    private static MessageSourceDto Map(MessageSource x) =>
        new()
        {
            Id = x.Id,
            Code = x.Code,
            Name = x.Name,
            Provider = x.Provider,
            IsEnabled = x.IsEnabled,
            Description = x.Description,
            HasConnectionString = !string.IsNullOrWhiteSpace(x.ConnectionString),
            ConnectionStringHint = string.IsNullOrWhiteSpace(x.ConnectionString)
                ? "(平台默认 rabbitmq)"
                : Mask(x.ConnectionString!),
            CreationTime = x.CreationTime,
            LastModificationTime = x.LastModificationTime
        };

    private static string Mask(string value)
    {
        if (value.Length <= 12)
        {
            return "***";
        }

        return value[..6] + "***" + value[^4..];
    }
}
