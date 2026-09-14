using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;

namespace Meta.Dow.SaaS.EventHandler;

/// <summary>
/// 将创建租户时的管理员邮箱写入 Tenant.ExtraProperties，供列表展示。
/// </summary>
public class TenantAdminEmailStoreHandler(
    ITenantRepository tenantRepository,
    IUnitOfWorkManager unitOfWorkManager
) : IDistributedEventHandler<TenantCreatedEto>, ITransientDependency
{
    public const string AdminEmailPropertyName = "AdminEmail";

    private readonly ITenantRepository _tenantRepository = tenantRepository;
    private readonly IUnitOfWorkManager _unitOfWorkManager = unitOfWorkManager;

    public async Task HandleEventAsync(TenantCreatedEto eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var email = eventData.Properties.GetOrDefault(AdminEmailPropertyName);
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var tenant = await _tenantRepository.FindAsync(eventData.Id);
        if (tenant is null)
        {
            return;
        }

        tenant.SetProperty(AdminEmailPropertyName, email);
        await _tenantRepository.UpdateAsync(tenant, autoSave: true);
        await uow.CompleteAsync();
    }
}
