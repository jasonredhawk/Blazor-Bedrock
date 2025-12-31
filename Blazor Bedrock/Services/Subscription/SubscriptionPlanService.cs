using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Services;
using Microsoft.EntityFrameworkCore;

namespace Blazor_Bedrock.Services.Subscription;

public interface ISubscriptionPlanService
{
    Task<List<SubscriptionPlan>> GetAllPlansAsync();
    Task<SubscriptionPlan?> GetPlanByIdAsync(int planId);
    Task<SubscriptionPlan?> GetPlanByNameAsync(string name);
    Task<SubscriptionPlan> CreatePlanAsync(SubscriptionPlan plan);
    Task<SubscriptionPlan> UpdatePlanAsync(SubscriptionPlan plan);
    Task<bool> DeletePlanAsync(int planId);
}

public class SubscriptionPlanService : ISubscriptionPlanService
{
    private readonly ApplicationDbContext _context;
    private readonly IDatabaseSyncService _dbSync;

    public SubscriptionPlanService(ApplicationDbContext context, IDatabaseSyncService dbSync)
    {
        _context = context;
        _dbSync = dbSync;
    }

    public async Task<List<SubscriptionPlan>> GetAllPlansAsync()
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.SubscriptionPlans
                .OrderBy(sp => sp.MonthlyPrice ?? 0)
                .ToListAsync();
        });
    }

    public async Task<SubscriptionPlan?> GetPlanByIdAsync(int planId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.SubscriptionPlans.FindAsync(planId);
        });
    }

    public async Task<SubscriptionPlan?> GetPlanByNameAsync(string name)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.SubscriptionPlans
                .FirstOrDefaultAsync(sp => sp.Name == name);
        });
    }

    public async Task<SubscriptionPlan> CreatePlanAsync(SubscriptionPlan plan)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            plan.CreatedAt = DateTime.UtcNow;
            _context.SubscriptionPlans.Add(plan);
            await _context.SaveChangesAsync();
            return plan;
        });
    }

    public async Task<SubscriptionPlan> UpdatePlanAsync(SubscriptionPlan plan)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            plan.UpdatedAt = DateTime.UtcNow;
            _context.SubscriptionPlans.Update(plan);
            await _context.SaveChangesAsync();
            return plan;
        });
    }

    public async Task<bool> DeletePlanAsync(int planId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var plan = await _context.SubscriptionPlans.FindAsync(planId);
            if (plan == null) return false;

            // Check if any tenants are using this plan
            var hasTenants = await _context.Tenants.AnyAsync(t => t.SubscriptionPlanId == planId);
            if (hasTenants) return false; // Cannot delete plan that's in use

            _context.SubscriptionPlans.Remove(plan);
            await _context.SaveChangesAsync();
            return true;
        });
    }
}
