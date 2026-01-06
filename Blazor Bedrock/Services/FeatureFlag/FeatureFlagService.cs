using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using FeatureFlagModel = Blazor_Bedrock.Data.Models.FeatureFlag;

namespace Blazor_Bedrock.Services.FeatureFlag;

public interface IFeatureFlagService
{
    Task<bool> IsEnabledAsync(string featureName);
    Task<List<FeatureFlagModel>> GetAllFlagsAsync();
    Task<FeatureFlagModel?> GetFlagAsync(string featureName);
    Task SetFlagAsync(string featureName, bool isEnabled);
    void ClearCache();
}

public class FeatureFlagService : IFeatureFlagService
{
    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly IDatabaseSyncService _dbSync;
    private const string CacheKey = "FeatureFlags";

    public FeatureFlagService(ApplicationDbContext context, IMemoryCache cache, IDatabaseSyncService dbSync)
    {
        _context = context;
        _cache = cache;
        _dbSync = dbSync;
    }

    public async Task<bool> IsEnabledAsync(string featureName)
    {
        var flags = await GetCachedFlagsAsync();
        var flag = flags.FirstOrDefault(f => f.Name.Equals(featureName, StringComparison.OrdinalIgnoreCase));
        return flag?.IsEnabled ?? false;
    }

    public async Task<List<FeatureFlagModel>> GetAllFlagsAsync()
    {
        return await GetCachedFlagsAsync();
    }

    public async Task<FeatureFlagModel?> GetFlagAsync(string featureName)
    {
        var flags = await GetCachedFlagsAsync();
        return flags.FirstOrDefault(f => f.Name.Equals(featureName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SetFlagAsync(string featureName, bool isEnabled)
    {
        await _dbSync.ExecuteAsync(async () =>
        {
            // Use ToLower() for case-insensitive comparison that EF Core can translate
            var featureNameLower = featureName.ToLower();
            var flag = await _context.FeatureFlags
                .FirstOrDefaultAsync(f => f.Name.ToLower() == featureNameLower);

            if (flag != null)
            {
                flag.IsEnabled = isEnabled;
                flag.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                // Create the flag if it doesn't exist
                flag = new FeatureFlagModel
                {
                    Name = featureName,
                    Description = $"Feature flag for {featureName}",
                    IsEnabled = isEnabled,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.FeatureFlags.Add(flag);
            }
            
            await _context.SaveChangesAsync();
            _cache.Remove(CacheKey);
        });
    }

    public void ClearCache()
    {
        _cache.Remove(CacheKey);
    }

    private async Task<List<FeatureFlagModel>> GetCachedFlagsAsync()
    {
        // Always get fresh data if cache is empty, otherwise use cache
        // This method is called after cache is cleared when needed
        if (!_cache.TryGetValue(CacheKey, out List<FeatureFlagModel>? flags))
        {
            flags = await _dbSync.ExecuteAsync(async () =>
            {
                return await _context.FeatureFlags.ToListAsync();
            });
            if (flags != null && flags.Any())
            {
                _cache.Set(CacheKey, flags, TimeSpan.FromMinutes(5));
            }
        }
        return flags ?? new List<FeatureFlagModel>();
    }
}

