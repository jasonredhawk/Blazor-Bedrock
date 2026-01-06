using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Blazor_Bedrock.Services.ApiConfiguration;

public interface IApiConfigurationService
{
    Task<Dictionary<string, string>> GetConfigurationAsync(string serviceName);
    Task<string?> GetConfigurationValueAsync(string serviceName, string key);
    Task SaveConfigurationAsync(string serviceName, Dictionary<string, string> configuration, string userId);
    Task<bool> HasConfigurationAsync(string serviceName);
}

public class ApiConfigurationService : IApiConfigurationService
{
    private readonly ApplicationDbContext _context;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IDatabaseSyncService _dbSync;

    public ApiConfigurationService(
        ApplicationDbContext context,
        IDataProtectionProvider dataProtectionProvider,
        IDatabaseSyncService dbSync)
    {
        _context = context;
        _dataProtectionProvider = dataProtectionProvider;
        _dbSync = dbSync;
    }

    public async Task<Dictionary<string, string>> GetConfigurationAsync(string serviceName)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var config = await _context.ApiConfigurations
                .FirstOrDefaultAsync(ac => ac.ServiceName == serviceName && ac.IsActive);

            if (config == null)
            {
                return new Dictionary<string, string>();
            }

            try
            {
                var protector = _dataProtectionProvider.CreateProtector($"ApiConfiguration_{serviceName}");
                var decryptedJson = protector.Unprotect(config.EncryptedConfiguration);
                
                try
                {
                    var configuration = JsonSerializer.Deserialize<Dictionary<string, string>>(decryptedJson);
                    return configuration ?? new Dictionary<string, string>();
                }
                catch (JsonException)
                {
                    // JSON deserialization failed - return empty dictionary
                    return new Dictionary<string, string>();
                }
            }
            catch (System.Security.Cryptography.CryptographicException ex) when (ex.Message.Contains("key") && ex.Message.Contains("not found"))
            {
                // Data protection key mismatch - old encrypted data can't be decrypted
                // This happens when data protection keys change (e.g., after adding key persistence)
                // Return empty dictionary so user can save new credentials
                return new Dictionary<string, string>();
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // Other decryption errors - return empty dictionary
                return new Dictionary<string, string>();
            }
        });
    }

    public async Task<string?> GetConfigurationValueAsync(string serviceName, string key)
    {
        var configuration = await GetConfigurationAsync(serviceName);
        return configuration.TryGetValue(key, out var value) ? value : null;
    }

    public async Task SaveConfigurationAsync(string serviceName, Dictionary<string, string> configuration, string userId)
    {
        await _dbSync.ExecuteAsync(async () =>
        {
            try
            {
                var protector = _dataProtectionProvider.CreateProtector($"ApiConfiguration_{serviceName}");
                var json = JsonSerializer.Serialize(configuration);
                var encryptedJson = protector.Protect(json);

                var existing = await _context.ApiConfigurations
                    .FirstOrDefaultAsync(ac => ac.ServiceName == serviceName);

                if (existing != null)
                {
                    existing.EncryptedConfiguration = encryptedJson;
                    existing.UpdatedAt = DateTime.UtcNow;
                    existing.UpdatedByUserId = userId;
                    existing.IsActive = true;
                }
                else
                {
                    _context.ApiConfigurations.Add(new Blazor_Bedrock.Data.Models.ApiConfiguration
                    {
                        ServiceName = serviceName,
                        EncryptedConfiguration = encryptedJson,
                        CreatedByUserId = userId,
                        UpdatedByUserId = userId,
                        IsActive = true
                    });
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Log the error but rethrow so caller can handle it
                throw new InvalidOperationException($"Failed to save configuration for {serviceName}: {ex.Message}", ex);
            }
        });
    }

    public async Task<bool> HasConfigurationAsync(string serviceName)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.ApiConfigurations
                .AnyAsync(ac => ac.ServiceName == serviceName && ac.IsActive);
        });
    }
}
