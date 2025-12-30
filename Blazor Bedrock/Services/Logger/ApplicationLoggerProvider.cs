using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Blazor_Bedrock.Services.Logger;

public class ApplicationLoggerProvider : ILoggerProvider
{
    private IServiceProvider _serviceProvider;
    private IApplicationLoggerService? _applicationLoggerService;
    private readonly object _lock = new();

    public ApplicationLoggerProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void UpdateServiceProvider(IServiceProvider serviceProvider)
    {
        lock (_lock)
        {
            _serviceProvider = serviceProvider;
            _applicationLoggerService = null; // Reset to force re-resolution
        }
    }

    private IApplicationLoggerService GetLoggerService()
    {
        if (_applicationLoggerService == null)
        {
            lock (_lock)
            {
                if (_applicationLoggerService == null)
                {
                    _applicationLoggerService = _serviceProvider.GetRequiredService<IApplicationLoggerService>();
                }
            }
        }
        return _applicationLoggerService;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new ApplicationLogger(categoryName, GetLoggerService);
    }

    public void Dispose()
    {
        // Nothing to dispose
    }
}

public class ApplicationLogger : ILogger
{
    private readonly string _categoryName;
    private readonly Func<IApplicationLoggerService> _getLoggerService;

    public ApplicationLogger(string categoryName, Func<IApplicationLoggerService> getLoggerService)
    {
        _categoryName = categoryName;
        _getLoggerService = getLoggerService;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        // Always enabled - let the ApplicationLoggerService handle filtering
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        
        // Forward to ApplicationLoggerService
        var loggerService = _getLoggerService();
        loggerService.Log(logLevel, message, _categoryName, exception);
    }
}
