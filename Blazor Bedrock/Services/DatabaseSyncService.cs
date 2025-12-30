namespace Blazor_Bedrock.Services;

/// <summary>
/// Service to synchronize database operations to prevent concurrent access issues with DbContext in Blazor Server.
/// Uses AsyncLocal to track re-entrant calls on the same async context to prevent deadlocks.
/// </summary>
public interface IDatabaseSyncService
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation);
    Task ExecuteAsync(Func<Task> operation);
}

public class DatabaseSyncService : IDatabaseSyncService
{
    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
    private static readonly AsyncLocal<int> _recursionDepth = new AsyncLocal<int>();

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        // If we're already inside a sync operation on this async context, don't wait again
        if (_recursionDepth.Value > 0)
        {
            return await operation();
        }

        await _semaphore.WaitAsync();
        try
        {
            _recursionDepth.Value = _recursionDepth.Value + 1;
            try
            {
                return await operation();
            }
            finally
            {
                _recursionDepth.Value = _recursionDepth.Value - 1;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ExecuteAsync(Func<Task> operation)
    {
        // If we're already inside a sync operation on this async context, don't wait again
        if (_recursionDepth.Value > 0)
        {
            await operation();
            return;
        }

        await _semaphore.WaitAsync();
        try
        {
            _recursionDepth.Value = _recursionDepth.Value + 1;
            try
            {
                await operation();
            }
            finally
            {
                _recursionDepth.Value = _recursionDepth.Value - 1;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }
}

