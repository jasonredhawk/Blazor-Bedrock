using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Services;
using Microsoft.EntityFrameworkCore;
using TenantModel = Blazor_Bedrock.Data.Models.Tenant;

namespace Blazor_Bedrock.Services.Subscription;

public interface IOrganizationSubscriptionService
{
    Task<List<TenantWithSubscription>> GetAllOrganizationsWithSubscriptionsAsync();
    Task<TenantSubscription?> GetTenantSubscriptionAsync(int tenantId);
    Task<TenantSubscription> AssignSubscriptionAsync(int tenantId, int planId, decimal? customMonthlyPrice = null, decimal? customYearlyPrice = null);
    Task<TenantSubscription> UpdateSubscriptionAsync(int tenantId, int planId, decimal? customMonthlyPrice = null, decimal? customYearlyPrice = null);
    Task<bool> RemoveSubscriptionAsync(int tenantId);
    Task<SubscriptionPlan?> GetTenantCurrentPlanAsync(int tenantId);
}

public class TenantWithSubscription
{
    public TenantModel Tenant { get; set; } = null!;
    public SubscriptionPlan? CurrentPlan { get; set; }
    public TenantSubscription? Subscription { get; set; }
    public decimal? EffectiveMonthlyPrice => Subscription?.CustomMonthlyPrice ?? CurrentPlan?.MonthlyPrice;
    public decimal? EffectiveYearlyPrice => Subscription?.CustomYearlyPrice ?? CurrentPlan?.YearlyPrice;
}

public class OrganizationSubscriptionService : IOrganizationSubscriptionService
{
    private readonly ApplicationDbContext _context;
    private readonly IDatabaseSyncService _dbSync;

    public OrganizationSubscriptionService(ApplicationDbContext context, IDatabaseSyncService dbSync)
    {
        _context = context;
        _dbSync = dbSync;
    }

    public async Task<List<TenantWithSubscription>> GetAllOrganizationsWithSubscriptionsAsync()
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var tenants = await _context.Tenants
                .Include(t => t.SubscriptionPlan)
                .Include(t => t.CurrentSubscription)
                    .ThenInclude(ts => ts.SubscriptionPlan)
                .ToListAsync();

            return tenants.Select(t => new TenantWithSubscription
            {
                Tenant = t,
                CurrentPlan = t.SubscriptionPlan,
                Subscription = t.CurrentSubscription
            }).ToList();
        });
    }

    public async Task<TenantSubscription?> GetTenantSubscriptionAsync(int tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.TenantSubscriptions
                .Include(ts => ts.SubscriptionPlan)
                .FirstOrDefaultAsync(ts => ts.TenantId == tenantId);
        });
    }

    public async Task<TenantSubscription> AssignSubscriptionAsync(int tenantId, int planId, decimal? customMonthlyPrice = null, decimal? customYearlyPrice = null)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            // Remove existing subscription if any
            var existing = await _context.TenantSubscriptions
                .FirstOrDefaultAsync(ts => ts.TenantId == tenantId);
            
            if (existing != null)
            {
                _context.TenantSubscriptions.Remove(existing);
            }

            // Update tenant's subscription plan reference
            var tenant = await _context.Tenants.FindAsync(tenantId);
            if (tenant != null)
            {
                tenant.SubscriptionPlanId = planId;
            }

            // Create new subscription
            var subscription = new TenantSubscription
            {
                TenantId = tenantId,
                SubscriptionPlanId = planId,
                CustomMonthlyPrice = customMonthlyPrice,
                CustomYearlyPrice = customYearlyPrice,
                Status = "active",
                CreatedAt = DateTime.UtcNow
            };

            _context.TenantSubscriptions.Add(subscription);
            await _context.SaveChangesAsync();

            return subscription;
        });
    }

    public async Task<TenantSubscription> UpdateSubscriptionAsync(int tenantId, int planId, decimal? customMonthlyPrice = null, decimal? customYearlyPrice = null)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var subscription = await _context.TenantSubscriptions
                .FirstOrDefaultAsync(ts => ts.TenantId == tenantId);

            if (subscription == null)
            {
                // Create new subscription if it doesn't exist
                return await AssignSubscriptionAsync(tenantId, planId, customMonthlyPrice, customYearlyPrice);
            }

            // Update existing subscription
            subscription.SubscriptionPlanId = planId;
            subscription.CustomMonthlyPrice = customMonthlyPrice;
            subscription.CustomYearlyPrice = customYearlyPrice;
            subscription.UpdatedAt = DateTime.UtcNow;

            // Update tenant's subscription plan reference
            var tenant = await _context.Tenants.FindAsync(tenantId);
            if (tenant != null)
            {
                tenant.SubscriptionPlanId = planId;
            }

            await _context.SaveChangesAsync();
            return subscription;
        });
    }

    public async Task<bool> RemoveSubscriptionAsync(int tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var subscription = await _context.TenantSubscriptions
                .FirstOrDefaultAsync(ts => ts.TenantId == tenantId);

            if (subscription == null) return false;

            _context.TenantSubscriptions.Remove(subscription);

            // Clear tenant's subscription plan reference
            var tenant = await _context.Tenants.FindAsync(tenantId);
            if (tenant != null)
            {
                tenant.SubscriptionPlanId = null;
            }

            await _context.SaveChangesAsync();
            return true;
        });
    }

    public async Task<SubscriptionPlan?> GetTenantCurrentPlanAsync(int tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var tenant = await _context.Tenants
                .Include(t => t.SubscriptionPlan)
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            return tenant?.SubscriptionPlan;
        });
    }
}
