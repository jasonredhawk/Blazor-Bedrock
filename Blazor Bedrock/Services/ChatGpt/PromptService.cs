using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Services;
using Microsoft.EntityFrameworkCore;

namespace Blazor_Bedrock.Services.ChatGpt;

public interface IPromptService
{
    Task<List<ChatGptPrompt>> GetAllPromptsAsync(int? tenantId);
    Task<ChatGptPrompt?> GetPromptByIdAsync(int id, int? tenantId);
    Task<ChatGptPrompt> CreatePromptAsync(ChatGptPrompt prompt, int? tenantId);
    Task<bool> UpdatePromptAsync(int id, ChatGptPrompt prompt, int? tenantId);
    Task<bool> DeletePromptAsync(int id, int? tenantId);
}

public class PromptService : IPromptService
{
    private readonly ApplicationDbContext _context;
    private readonly IDatabaseSyncService _dbSync;

    public PromptService(ApplicationDbContext context, IDatabaseSyncService dbSync)
    {
        _context = context;
        _dbSync = dbSync;
    }

    public async Task<List<ChatGptPrompt>> GetAllPromptsAsync(int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.ChatGptPrompts
                .Where(p => p.TenantId == null || p.TenantId == tenantId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        });
    }

    public async Task<ChatGptPrompt?> GetPromptByIdAsync(int id, int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.ChatGptPrompts
                .FirstOrDefaultAsync(p => p.Id == id && (p.TenantId == null || p.TenantId == tenantId));
        });
    }

    public async Task<ChatGptPrompt> CreatePromptAsync(ChatGptPrompt prompt, int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            prompt.TenantId = tenantId;
            prompt.CreatedAt = DateTime.UtcNow;
            prompt.UpdatedAt = DateTime.UtcNow;
            
            _context.ChatGptPrompts.Add(prompt);
            await _context.SaveChangesAsync();
            return prompt;
        });
    }

    public async Task<bool> UpdatePromptAsync(int id, ChatGptPrompt prompt, int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var existing = await _context.ChatGptPrompts
                .FirstOrDefaultAsync(p => p.Id == id && (p.TenantId == null || p.TenantId == tenantId));
            
            if (existing == null)
                return false;

            existing.Name = prompt.Name;
            existing.PromptText = prompt.PromptText;
            existing.Description = prompt.Description;
            existing.IsSystemPrompt = prompt.IsSystemPrompt;
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        });
    }

    public async Task<bool> DeletePromptAsync(int id, int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var prompt = await _context.ChatGptPrompts
                .FirstOrDefaultAsync(p => p.Id == id && (tenantId == null || p.TenantId == tenantId));
            
            if (prompt == null)
                return false;

            _context.ChatGptPrompts.Remove(prompt);
            await _context.SaveChangesAsync();
            return true;
        });
    }
}

