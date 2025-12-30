using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Infrastructure.ExternalApis;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;

namespace Blazor_Bedrock.Services.ChatGpt;

public class ChatMessage
{
    public string Role { get; set; } = "user"; // "system", "user", or "assistant"
    public string Content { get; set; } = string.Empty;
}

public interface IChatGptService
{
    Task<string> GetDecryptedApiKeyAsync(string userId, int? tenantId);
    Task<string?> GetPreferredModelAsync(int? tenantId);
    Task SaveApiKeyAsync(string userId, int? tenantId, string apiKey, string? preferredModel = null);
    Task<List<string>> GetAvailableModelsAsync(string apiKey);
    Task<string> SendChatMessageAsync(string userId, int? tenantId, string message, string? model = null, string? systemPrompt = null);
    Task<string> SendConversationMessageAsync(List<ChatMessage> messages, string apiKey, string? model = null);
    Task<string> SendConversationMessageAsync(string userId, int? tenantId, List<ChatMessage> messages);
    Task<ChatGptConversation> CreateConversationAsync(string userId, int? tenantId, string title, string? model = null);
    Task<List<ChatGptConversation>> GetConversationsAsync(string userId, int? tenantId);
    Task<List<ChatGptMessage>> GetConversationMessagesAsync(int conversationId);
}

public class ChatGptService : IChatGptService
{
    private readonly ApplicationDbContext _context;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IDocumentProcessor _documentProcessor;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ChatGptService> _logger;

    public ChatGptService(
        ApplicationDbContext context,
        IDataProtectionProvider dataProtectionProvider,
        IDocumentProcessor documentProcessor,
        HttpClient httpClient,
        ILogger<ChatGptService> logger)
    {
        _context = context;
        _dataProtectionProvider = dataProtectionProvider;
        _documentProcessor = documentProcessor;
        _httpClient = httpClient;
        _logger = logger;
        
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Blazor-Bedrock/1.0");
    }

    public async Task<string> GetDecryptedApiKeyAsync(string userId, int? tenantId)
    {
        if (tenantId == null)
        {
            throw new InvalidOperationException("Tenant ID is required. Please select an organization.");
        }

        // Prioritize tenant-specific API keys (organization-level)
        var apiKey = await _context.ChatGptApiKeys
            .FirstOrDefaultAsync(k => k.TenantId == tenantId && k.IsActive);

        if (apiKey == null)
        {
            throw new InvalidOperationException($"No API key found for the current organization. Please configure an API key in Settings.");
        }

        var protector = _dataProtectionProvider.CreateProtector("ChatGptApiKey");
        return protector.Unprotect(apiKey.EncryptedApiKey);
    }

    public async Task<string?> GetPreferredModelAsync(int? tenantId)
    {
        if (tenantId == null)
        {
            return null;
        }

        var apiKey = await _context.ChatGptApiKeys
            .FirstOrDefaultAsync(k => k.TenantId == tenantId && k.IsActive);

        return apiKey?.PreferredModel;
    }

    public async Task SaveApiKeyAsync(string userId, int? tenantId, string apiKey, string? preferredModel = null)
    {
        if (tenantId == null)
        {
            throw new InvalidOperationException("Tenant ID is required. Please select an organization.");
        }

        var protector = _dataProtectionProvider.CreateProtector("ChatGptApiKey");
        var encryptedKey = protector.Protect(apiKey);

        // Look for existing tenant-specific API key (organization-level)
        var existing = await _context.ChatGptApiKeys
            .FirstOrDefaultAsync(k => k.TenantId == tenantId);

        if (existing != null)
        {
            existing.EncryptedApiKey = encryptedKey;
            existing.PreferredModel = preferredModel;
            existing.LastUsedAt = DateTime.UtcNow;
            existing.IsActive = true;
            // Ensure UserId is set (in case it wasn't before)
            if (string.IsNullOrEmpty(existing.UserId))
            {
                existing.UserId = userId;
            }
        }
        else
        {
            _context.ChatGptApiKeys.Add(new ChatGptApiKey
            {
                UserId = userId,
                TenantId = tenantId,
                EncryptedApiKey = encryptedKey,
                PreferredModel = preferredModel,
                IsActive = true
            });
        }

        await _context.SaveChangesAsync();
    }

    public async Task<List<string>> GetAvailableModelsAsync(string apiKey)
    {
        try
        {
            const string baseUrl = "https://api.openai.com";
            var url = $"{baseUrl}/v1/models";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("User-Agent", "Blazor-Bedrock/1.0");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to fetch models from OpenAI API: {StatusCode}, {Error}", 
                    response.StatusCode, errorContent);
                return GetDefaultModels();
            }

            var responseContent = await response.Content.ReadFromJsonAsync<JsonElement>();
            var models = new List<string>();

            if (responseContent.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var modelElement in data.EnumerateArray())
                {
                    if (modelElement.TryGetProperty("id", out var id))
                    {
                        var modelId = id.GetString();
                        if (!string.IsNullOrEmpty(modelId) && modelId.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase))
                        {
                            models.Add(modelId);
                        }
                    }
                }
            }

            return models.Any() ? models.OrderByDescending(m => m).ToList() : GetDefaultModels();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching available models from OpenAI API");
            return GetDefaultModels();
        }
    }

    private List<string> GetDefaultModels()
    {
        return new List<string>
        {
            "gpt-4o-2024-11-20",
            "gpt-4o-mini-2024-11-20",
            "gpt-4-turbo-2024-04-09",
            "gpt-4-0125-preview",
            "gpt-4",
            "gpt-3.5-turbo-0125",
            "gpt-3.5-turbo-16k",
            "gpt-3.5-turbo"
        };
    }

    public async Task<string> SendChatMessageAsync(string userId, int? tenantId, string message, string? model = null, string? systemPrompt = null)
    {
        var apiKey = await GetDecryptedApiKeyAsync(userId, tenantId);
        
        var messages = new List<ChatMessage>();
        
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new ChatMessage { Role = "system", Content = systemPrompt });
        }
        
        messages.Add(new ChatMessage { Role = "user", Content = message });

        return await CallChatGptWithMessagesAsync(messages, apiKey, model ?? "gpt-3.5-turbo");
    }

    public async Task<string> SendConversationMessageAsync(List<ChatMessage> messages, string apiKey, string? model = null)
    {
        return await CallChatGptWithMessagesAsync(messages, apiKey, model ?? "gpt-3.5-turbo");
    }

    public async Task<string> SendConversationMessageAsync(string userId, int? tenantId, List<ChatMessage> messages)
    {
        if (tenantId == null)
        {
            throw new InvalidOperationException("Tenant ID is required. Please select an organization.");
        }

        var apiKey = await GetDecryptedApiKeyAsync(userId, tenantId);
        var preferredModel = await GetPreferredModelAsync(tenantId);
        
        return await CallChatGptWithMessagesAsync(messages, apiKey, preferredModel ?? "gpt-3.5-turbo");
    }

    private async Task<string> CallChatGptWithMessagesAsync(List<ChatMessage> messages, string apiKey, string model)
    {
        try
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new Exception("ChatGPT API key is not configured. Please configure your API key in Settings.");
            }

            const string baseUrl = "https://api.openai.com";
            var requestBody = new
            {
                model = model,
                messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
                max_tokens = 2000,
                temperature = 0.7
            };

            // Create a new HTTP request with custom headers
            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/chat/completions");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("User-Agent", "Blazor-Bedrock/1.0");
            request.Content = JsonContent.Create(requestBody);

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("ChatGPT API error: Status {StatusCode}, Response: {ErrorContent}", 
                    response.StatusCode, errorContent);
                
                // Provide user-friendly messages for common errors
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    try
                    {
                        var errorJson = JsonDocument.Parse(errorContent);
                        if (errorJson.RootElement.TryGetProperty("error", out var error) &&
                            error.TryGetProperty("code", out var code) &&
                            code.GetString() == "insufficient_quota")
                        {
                            throw new Exception("OpenAI API quota exceeded. Please add credits to your account at https://platform.openai.com/account/billing");
                        }
                    }
                    catch (JsonException)
                    {
                        // If parsing fails, use the original error
                    }
                }
                
                throw new Exception($"ChatGPT API returned error {response.StatusCode}: {errorContent}");
            }

            var responseContent = await response.Content.ReadFromJsonAsync<JsonElement>();
            
            if (responseContent.TryGetProperty("choices", out var choices) && 
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content))
                {
                    return content.GetString() ?? "No response generated.";
                }
            }

            return "Unable to parse response from ChatGPT API.";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error calling ChatGPT API: {Message}", ex.Message);
            throw new Exception($"Error communicating with ChatGPT API: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in ChatGPT service: {Message}", ex.Message);
            throw;
        }
    }

    public async Task<ChatGptConversation> CreateConversationAsync(string userId, int? tenantId, string title, string? model = null)
    {
        var conversation = new ChatGptConversation
        {
            UserId = userId,
            TenantId = tenantId,
            Title = title,
            Model = model
        };

        _context.ChatGptConversations.Add(conversation);
        await _context.SaveChangesAsync();
        return conversation;
    }

    public async Task<List<ChatGptConversation>> GetConversationsAsync(string userId, int? tenantId)
    {
        return await _context.ChatGptConversations
            .Where(c => c.UserId == userId && (tenantId == null || c.TenantId == tenantId))
            .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<ChatGptMessage>> GetConversationMessagesAsync(int conversationId)
    {
        return await _context.ChatGptMessages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }
}