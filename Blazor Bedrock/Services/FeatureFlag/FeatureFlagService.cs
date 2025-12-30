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
            var flag = await _context.FeatureFlags
                .FirstOrDefaultAsync(f => f.Name.Equals(featureName, StringComparison.OrdinalIgnoreCase));

            if (flag != null)
            {
                flag.IsEnabled = isEnabled;
                flag.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                _cache.Remove(CacheKey);
            }
        });
    }

    private async Task<List<FeatureFlagModel>> GetCachedFlagsAsync()
    {
        if (!_cache.TryGetValue(CacheKey, out List<FeatureFlagModel>? flags))
        {
            flags = await _dbSync.ExecuteAsync(async () =>
            {
                return await _context.FeatureFlags.ToListAsync();
            });
            _cache.Set(CacheKey, flags, TimeSpan.FromMinutes(5));
        }
        return flags ?? new List<FeatureFlagModel>();
    }
}

