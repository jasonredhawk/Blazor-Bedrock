using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Services.FeatureFlag;
using Blazor_Bedrock.Services;
using Microsoft.EntityFrameworkCore;

namespace Blazor_Bedrock.Services.Subscription;

public interface ISubscriptionLimitationService
{
    Task<SubscriptionLimitations?> GetTenantLimitationsAsync(int? tenantId);
    Task<int> GetCurrentCountAsync(int? tenantId, LimitationType type);
}

public class SubscriptionLimitationService : ISubscriptionLimitationService
{
    private readonly ApplicationDbContext _context;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly IDatabaseSyncService _dbSync;

    public SubscriptionLimitationService(
        ApplicationDbContext context,
        IFeatureFlagService featureFlagService,
        IDatabaseSyncService dbSync)
    {
        _context = context;
        _featureFlagService = featureFlagService;
        _dbSync = dbSync;
    }

    public async Task<SubscriptionLimitations?> GetTenantLimitationsAsync(int? tenantId)
    {
        // If subscriptions feature is not enabled, return null (no limitations)
        var subscriptionsEnabled = await _featureFlagService.IsEnabledAsync("Subscriptions");
        if (!subscriptionsEnabled || !tenantId.HasValue)
        {
            return null;
        }

        return await _dbSync.ExecuteAsync(async () =>
        {
            var tenant = await _context.Tenants
                .Include(t => t.SubscriptionPlan)
                .FirstOrDefaultAsync(t => t.Id == tenantId.Value);

            if (tenant?.SubscriptionPlan == null)
            {
                return null;
            }

            return new SubscriptionLimitations
            {
                MaxUsers = tenant.SubscriptionPlan.MaxUsers,
                MaxDocuments = tenant.SubscriptionPlan.MaxDocuments,
                MaxCharts = tenant.SubscriptionPlan.MaxCharts,
                MaxConversations = tenant.SubscriptionPlan.MaxConversations,
                MaxQueriesPerConversation = tenant.SubscriptionPlan.MaxQueriesPerConversation,
                CanUseChatGptAnalysis = tenant.SubscriptionPlan.CanUseChatGptAnalysis
            };
        });
    }

    public async Task<int> GetCurrentCountAsync(int? tenantId, LimitationType type)
    {
        if (!tenantId.HasValue)
        {
            return 0;
        }

        return await _dbSync.ExecuteAsync(async () =>
        {
            return type switch
            {
                LimitationType.Users => await _context.UserTenants
                    .Where(ut => ut.TenantId == tenantId.Value)
                    .CountAsync(),
                LimitationType.Documents => await _context.Documents
                    .Where(d => d.TenantId == tenantId.Value)
                    .CountAsync(),
                LimitationType.Charts => await _context.SavedCharts
                    .Where(c => c.TenantId == tenantId.Value)
                    .CountAsync(),
                LimitationType.Conversations => await _context.ChatGptConversations
                    .Where(c => c.TenantId == tenantId.Value)
                    .CountAsync(),
                _ => 0
            };
        });
    }
}

public class SubscriptionLimitations
{
    public int? MaxUsers { get; set; }
    public int? MaxDocuments { get; set; }
    public int? MaxCharts { get; set; }
    public int? MaxConversations { get; set; }
    public int? MaxQueriesPerConversation { get; set; }
    public bool CanUseChatGptAnalysis { get; set; }
}

public enum LimitationType
{
    Users,
    Documents,
    Charts,
    Conversations
}
