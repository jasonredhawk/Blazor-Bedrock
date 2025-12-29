namespace Blazor_Bedrock.Data.Models;

public class MigrationEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Version { get; set; } = string.Empty;
    public string SqlScript { get; set; } = string.Empty;
    public MigrationStatus Status { get; set; } = MigrationStatus.Pending;
    public DateTime? ExecutedAt { get; set; }
    public string? ExecutedBy { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum MigrationStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}

