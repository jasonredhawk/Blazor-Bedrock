using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Blazor_Bedrock.Services.Migrations;

public interface IMigrationService
{
    Task<List<MigrationEntry>> GetAllMigrationsAsync();
    Task<MigrationEntry?> GetMigrationAsync(int id);
    Task<MigrationEntry> CreateMigrationAsync(string name, string description, string version, string sqlScript);
    Task<bool> ExecuteMigrationAsync(int migrationId, string executedBy);
}

public class MigrationService : IMigrationService
{
    private readonly ApplicationDbContext _context;

    public MigrationService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<MigrationEntry>> GetAllMigrationsAsync()
    {
        return await _context.MigrationEntries
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();
    }

    public async Task<MigrationEntry?> GetMigrationAsync(int id)
    {
        return await _context.MigrationEntries.FindAsync(id);
    }

    public async Task<MigrationEntry> CreateMigrationAsync(string name, string description, string version, string sqlScript)
    {
        var migration = new MigrationEntry
        {
            Name = name,
            Description = description,
            Version = version,
            SqlScript = sqlScript,
            Status = MigrationStatus.Pending
        };

        _context.MigrationEntries.Add(migration);
        await _context.SaveChangesAsync();
        return migration;
    }

    public async Task<bool> ExecuteMigrationAsync(int migrationId, string executedBy)
    {
        var migration = await _context.MigrationEntries.FindAsync(migrationId);
        if (migration == null || migration.Status != MigrationStatus.Pending)
            return false;

        migration.Status = MigrationStatus.Running;
        await _context.SaveChangesAsync();

        try
        {
            // Execute SQL script
            await _context.Database.ExecuteSqlRawAsync(migration.SqlScript);
            
            migration.Status = MigrationStatus.Completed;
            migration.ExecutedAt = DateTime.UtcNow;
            migration.ExecutedBy = executedBy;
            migration.ErrorMessage = null;
        }
        catch (Exception ex)
        {
            migration.Status = MigrationStatus.Failed;
            migration.ErrorMessage = ex.Message;
        }
        finally
        {
            await _context.SaveChangesAsync();
        }

        return migration.Status == MigrationStatus.Completed;
    }
}

