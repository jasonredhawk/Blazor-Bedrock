using Microsoft.JSInterop;

namespace Blazor_Bedrock.Services.Notification;

public interface INotificationService
{
    Task ShowSuccessAsync(string message);
    Task ShowErrorAsync(string message);
    Task ShowWarningAsync(string message);
    Task ShowInfoAsync(string message);
}

public class NotificationService : INotificationService
{
    private readonly IJSRuntime _jsRuntime;

    public NotificationService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task ShowSuccessAsync(string message)
    {
        await _jsRuntime.InvokeVoidAsync("showToast", "success", message);
    }

    public async Task ShowErrorAsync(string message)
    {
        await _jsRuntime.InvokeVoidAsync("showToast", "error", message);
    }

    public async Task ShowWarningAsync(string message)
    {
        await _jsRuntime.InvokeVoidAsync("showToast", "warning", message);
    }

    public async Task ShowInfoAsync(string message)
    {
        await _jsRuntime.InvokeVoidAsync("showToast", "info", message);
    }
}
