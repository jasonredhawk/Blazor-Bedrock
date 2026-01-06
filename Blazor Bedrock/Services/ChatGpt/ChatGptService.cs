using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Infrastructure.ExternalApis;
using Blazor_Bedrock.Services;
using Blazor_Bedrock.Services.Document;
using Blazor_Bedrock.Services.ApiConfiguration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

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
    Task<string> SendConversationMessageWithDocumentsAsync(string userId, int? tenantId, int conversationId, string message);
    Task<ChatGptConversation> CreateConversationAsync(string userId, int? tenantId, string title, string? model = null, int? promptId = null, int? documentId = null, List<string>? selectedSheetNames = null, IProgress<string>? progress = null);
    Task UpdateConversationDocumentAsync(int conversationId, int? documentId, List<string>? selectedSheetNames = null, IProgress<string>? progress = null);
    Task UpdateConversationSheetSelectionAsync(int conversationId, List<string>? selectedSheetNames);
    Task<List<ChatGptConversation>> GetConversationsAsync(string userId, int? tenantId);
    Task<List<ChatGptMessage>> GetConversationMessagesAsync(int conversationId);
    Task<ChatGptMessage> SaveMessageAsync(ChatGptMessage message);
    Task UpdateConversationTimestampAsync(int conversationId);
    Task<bool> DeleteConversationAsync(int conversationId, string userId, int? tenantId);
    Task UpdateConversationTitleAsync(int conversationId, string title);
    Task UpdateConversationPromptAsync(int conversationId, int? promptId);
    Task<string> GenerateConversationTitleAsync(string userId, int? tenantId, string firstMessage);
    Task<byte[]> ExportConversationToDocxAsync(int conversationId, string userId, int? tenantId);
    Task<bool> DeleteConversationMessagesAsync(int conversationId, string userId, int? tenantId);
}

public class ChatGptService : IChatGptService
{
    private readonly ApplicationDbContext _context;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IDocumentProcessor _documentProcessor;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ChatGptService> _logger;
    private readonly IDatabaseSyncService _dbSync;
    private readonly IOpenAIFileThreadService _fileThreadService;
    private readonly IDocumentService _documentService;
    private readonly IPromptService _promptService;
    private readonly IApiConfigurationService _apiConfigurationService;

    public ChatGptService(
        ApplicationDbContext context,
        IDataProtectionProvider dataProtectionProvider,
        IDocumentProcessor documentProcessor,
        HttpClient httpClient,
        ILogger<ChatGptService> logger,
        IDatabaseSyncService dbSync,
        IOpenAIFileThreadService fileThreadService,
        IDocumentService documentService,
        IPromptService promptService,
        IApiConfigurationService apiConfigurationService)
    {
        _context = context;
        _dataProtectionProvider = dataProtectionProvider;
        _documentProcessor = documentProcessor;
        _httpClient = httpClient;
        _logger = logger;
        _dbSync = dbSync;
        _fileThreadService = fileThreadService;
        _documentService = documentService;
        _promptService = promptService;
        _apiConfigurationService = apiConfigurationService;
        
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Blazor-Bedrock/1.0");
    }

    public async Task<string> GetDecryptedApiKeyAsync(string userId, int? tenantId)
    {
        // Get API key from MasterAdmin-level configuration
        var apiKey = await _apiConfigurationService.GetConfigurationValueAsync("ChatGPT", "ApiKey");
        
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("ChatGPT API key is not configured. Please configure it in MasterAdmin > Features.");
        }

        return apiKey;
    }

    public async Task<string?> GetPreferredModelAsync(int? tenantId)
    {
        // Get preferred model from MasterAdmin-level configuration
        return await _apiConfigurationService.GetConfigurationValueAsync("ChatGPT", "PreferredModel");
    }

    public async Task SaveApiKeyAsync(string userId, int? tenantId, string apiKey, string? preferredModel = null)
    {
        // This method is deprecated - API keys should be configured in MasterAdmin > Features
        throw new InvalidOperationException("API key configuration has been moved to MasterAdmin > Features page. Please configure the ChatGPT API key there.");
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

    public async Task<ChatGptConversation> CreateConversationAsync(string userId, int? tenantId, string title, string? model = null, int? promptId = null, int? documentId = null, List<string>? selectedSheetNames = null, IProgress<string>? progress = null)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var conversation = new ChatGptConversation
            {
                UserId = userId,
                TenantId = tenantId,
                Title = title,
                Model = model,
                PromptId = promptId,
                DocumentId = documentId,
                SelectedSheetNames = selectedSheetNames != null && selectedSheetNames.Any() 
                    ? System.Text.Json.JsonSerializer.Serialize(selectedSheetNames) 
                    : null
            };

            // If document is attached, upload to OpenAI and create thread
            if (documentId.HasValue && tenantId.HasValue)
            {
                try
                {
                    var apiKey = await GetDecryptedApiKeyAsync(userId, tenantId);
                    var document = await _documentService.GetDocumentByIdAsync(documentId.Value, userId, tenantId.Value);
                    
                    if (document != null)
                    {
                        // For CSV files, convert to text format for better OpenAI processing
                        // OpenAI's file_search doesn't fully support CSV, so we convert it to text
                        Stream uploadStream;
                        string uploadFileName;
                        bool isCsv = document.ContentType?.Contains("csv", StringComparison.OrdinalIgnoreCase) == true ||
                                    document.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
                        
                        if (isCsv)
                        {
                            progress?.Report($"Converting CSV to text format (this may take a moment for large files)... (10%)");
                            // Convert CSV to text format - this ensures all rows are included
                            // OpenAI's file_search doesn't fully support CSV, so converting to text improves processing
                            using var csvStream = new MemoryStream(document.FileContent);
                            var textContent = await _documentProcessor.ExtractTextFromCsvAsync(csvStream);
                            var textBytes = System.Text.Encoding.UTF8.GetBytes(textContent);
                            uploadStream = new MemoryStream(textBytes);
                            uploadFileName = Path.ChangeExtension(document.FileName, ".txt");
                            
                            var rowCount = textContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
                            _logger.LogInformation("Converted CSV file {FileName} ({OriginalSize} bytes) to text format: {RowCount} rows, {TextSize} bytes", 
                                document.FileName, document.FileContent.Length, rowCount, textBytes.Length);
                            
                            if (rowCount < 1000)
                            {
                                _logger.LogWarning("CSV file {FileName} appears to have fewer rows than expected. Original file size: {Size} bytes", 
                                    document.FileName, document.FileContent.Length);
                            }
                        }
                        else
                        {
                            uploadStream = new MemoryStream(document.FileContent);
                            uploadFileName = document.FileName;
                        }
                        
                        // Upload document to OpenAI
                        progress?.Report($"Uploading {uploadFileName}... (20%)");
                        var openAiFileId = await _fileThreadService.UploadFileAsync(uploadStream, uploadFileName, apiKey);
                        
                        // Dispose the stream if we created it for CSV conversion
                        if (isCsv)
                        {
                            uploadStream.Dispose();
                        }
                        
                        progress?.Report($"File uploaded successfully. Creating vector store... (30%)");
                        
                        // Get prompt instructions if prompt is selected
                        string? assistantInstructions = null;
                        if (promptId.HasValue)
                        {
                            var prompt = await _promptService.GetPromptByIdAsync(promptId.Value, tenantId);
                            if (prompt != null)
                            {
                                assistantInstructions = prompt.PromptText;
                                // Remove {documentText} placeholder since documents are handled via file_search
                                assistantInstructions = assistantInstructions.Replace("{documentText}", "");
                                assistantInstructions = assistantInstructions.Trim();
                            }
                        }
                        
                        // Create assistant with file_search capability and vector store containing the file
                        // Note: CreateAssistantAsync will handle vector store creation internally
                        var assistantId = await _fileThreadService.CreateAssistantAsync(apiKey, model, new List<string> { openAiFileId }, progress, assistantInstructions);
                        
                        // Create thread
                        progress?.Report($"Creating conversation thread... (90%)");
                        var threadId = await _fileThreadService.CreateThreadAsync(apiKey);
                        progress?.Report($"Upload complete! (100%)");
                        
                        // Store OpenAI IDs and document ID
                        conversation.OpenAiThreadId = threadId;
                        conversation.OpenAiAssistantId = assistantId;
                        conversation.OpenAiFileIds = System.Text.Json.JsonSerializer.Serialize(new List<string> { openAiFileId });
                        conversation.UploadedDocumentIds = System.Text.Json.JsonSerializer.Serialize(new List<int> { documentId.Value });
                        
                        _logger.LogInformation("Successfully uploaded document {DocumentId} to OpenAI. Thread: {ThreadId}, Assistant: {AssistantId}, File: {FileId}", 
                            documentId.Value, threadId, assistantId, openAiFileId);
                    }
                    else
                    {
                        _logger.LogWarning("Document {DocumentId} not found when trying to upload to OpenAI", documentId.Value);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error setting up OpenAI thread for document {DocumentId} in conversation. Error: {ErrorMessage}", 
                        documentId.Value, ex.Message);
                    // Continue without thread - will fall back to regular chat API
                    // The UI will check if thread/file IDs are set and show appropriate message
                }
            }

            _context.ChatGptConversations.Add(conversation);
            await _context.SaveChangesAsync();
            return conversation;
        });
    }

    public async Task UpdateConversationDocumentAsync(int conversationId, int? documentId, List<string>? selectedSheetNames = null, IProgress<string>? progress = null)
    {
        await _dbSync.ExecuteAsync(async () =>
        {
            var existingConversation = await _context.ChatGptConversations.FindAsync(conversationId);
            if (existingConversation != null)
            {
                var previousDocumentId = existingConversation.DocumentId;
                existingConversation.DocumentId = documentId;
                existingConversation.SelectedSheetNames = selectedSheetNames != null && selectedSheetNames.Any() 
                    ? System.Text.Json.JsonSerializer.Serialize(selectedSheetNames) 
                    : null;
                
                // If document changed, update OpenAI thread
                if (documentId != previousDocumentId && existingConversation.TenantId.HasValue)
                {
                    try
                    {
                        var apiKey = await GetDecryptedApiKeyAsync(existingConversation.UserId, existingConversation.TenantId);
                        
                        if (documentId.HasValue)
                        {
                            // Upload new document and create/update assistant and thread
                            var document = await _documentService.GetDocumentByIdAsync(documentId.Value, existingConversation.UserId, existingConversation.TenantId.Value);
                            if (document != null)
                            {
                                // For CSV files, convert to text format for better OpenAI processing
                                Stream uploadStream;
                                string uploadFileName;
                                bool isCsv = document.ContentType?.Contains("csv", StringComparison.OrdinalIgnoreCase) == true ||
                                            document.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
                                
                                if (isCsv)
                                {
                                    progress?.Report($"Converting CSV to text format (this may take a moment for large files)... (10%)");
                                    // Convert CSV to text format - this ensures all rows are included
                                    // OpenAI's file_search doesn't fully support CSV, so converting to text improves processing
                                    using var csvStream = new MemoryStream(document.FileContent);
                                    var textContent = await _documentProcessor.ExtractTextFromCsvAsync(csvStream);
                                    var textBytes = System.Text.Encoding.UTF8.GetBytes(textContent);
                                    uploadStream = new MemoryStream(textBytes);
                                    uploadFileName = Path.ChangeExtension(document.FileName, ".txt");
                                    
                                    var rowCount = textContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
                                    _logger.LogInformation("Converted CSV file {FileName} ({OriginalSize} bytes) to text format: {RowCount} rows, {TextSize} bytes", 
                                        document.FileName, document.FileContent.Length, rowCount, textBytes.Length);
                                    
                                    if (rowCount < 1000)
                                    {
                                        _logger.LogWarning("CSV file {FileName} appears to have fewer rows than expected. Original file size: {Size} bytes", 
                                            document.FileName, document.FileContent.Length);
                                    }
                                }
                                else
                                {
                                    uploadStream = new MemoryStream(document.FileContent);
                                    uploadFileName = document.FileName;
                                }
                                
                                // Upload document to OpenAI
                                progress?.Report($"Uploading {uploadFileName}... (20%)");
                                var openAiFileId = await _fileThreadService.UploadFileAsync(uploadStream, uploadFileName, apiKey);
                                
                                // Dispose the stream if we created it for CSV conversion
                                if (isCsv)
                                {
                                    uploadStream.Dispose();
                                }
                                
                                progress?.Report($"File uploaded successfully. Creating vector store... (30%)");
                                
                                // Get existing file IDs
                                var existingFileIds = !string.IsNullOrEmpty(existingConversation.OpenAiFileIds)
                                    ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(existingConversation.OpenAiFileIds) ?? new List<string>()
                                    : new List<string>();
                                
                                if (!existingFileIds.Contains(openAiFileId))
                                {
                                    existingFileIds.Add(openAiFileId);
                                }
                                
                                // Create or get assistant
                                if (string.IsNullOrEmpty(existingConversation.OpenAiAssistantId))
                                {
                                    // Get prompt instructions if prompt is selected
                                    string? assistantInstructions = null;
                                    if (existingConversation.PromptId.HasValue)
                                    {
                                        var prompt = await _promptService.GetPromptByIdAsync(existingConversation.PromptId.Value, existingConversation.TenantId);
                                        if (prompt != null)
                                        {
                                            assistantInstructions = prompt.PromptText;
                                            // Remove {documentText} placeholder since documents are handled via file_search
                                            assistantInstructions = assistantInstructions.Replace("{documentText}", "");
                                            assistantInstructions = assistantInstructions.Trim();
                                        }
                                    }
                                    
                                    // Create new assistant with all files in vector store
                                    var assistantId = await _fileThreadService.CreateAssistantAsync(apiKey, existingConversation.Model, existingFileIds, progress, assistantInstructions);
                                    existingConversation.OpenAiAssistantId = assistantId;
                                }
                                else
                                {
                                    // Add new file to existing assistant's vector store
                                    // Get the existing vector store ID from the assistant
                                    var assistantJson = await _fileThreadService.GetAssistantAsync(existingConversation.OpenAiAssistantId, apiKey);
                                    using var assistantDoc = System.Text.Json.JsonDocument.Parse(assistantJson);
                                    var assistantRoot = assistantDoc.RootElement;
                                    
                                    string? existingVectorStoreId = null;
                                    if (assistantRoot.TryGetProperty("tool_resources", out var toolResources) &&
                                        toolResources.TryGetProperty("file_search", out var fileSearch) &&
                                        fileSearch.TryGetProperty("vector_store_ids", out var vectorStoreIds) &&
                                        vectorStoreIds.ValueKind == System.Text.Json.JsonValueKind.Array &&
                                        vectorStoreIds.GetArrayLength() > 0)
                                    {
                                        existingVectorStoreId = vectorStoreIds[0].GetString();
                                    }
                                    
                                    if (!string.IsNullOrEmpty(existingVectorStoreId))
                                    {
                                        // Add file to existing vector store
                                        await _fileThreadService.AddFilesToVectorStoreAsync(existingVectorStoreId, new List<string> { openAiFileId }, apiKey, progress);
                                    }
                                    else
                                    {
                                        // No existing vector store - create one and update assistant
                                        progress?.Report("Creating vector store... (40%)");
                                        var vectorStoreId = await _fileThreadService.CreateVectorStoreAsync(apiKey);
                                        await _fileThreadService.AddFilesToVectorStoreAsync(vectorStoreId, existingFileIds, apiKey, progress);
                                        progress?.Report("Updating assistant... (90%)");
                                        await _fileThreadService.UpdateAssistantWithVectorStoreAsync(existingConversation.OpenAiAssistantId, vectorStoreId, apiKey);
                                        progress?.Report("Upload complete! (100%)");
                                    }
                                }
                                
                                // Create new thread if one doesn't exist, or keep existing thread
                                if (string.IsNullOrEmpty(existingConversation.OpenAiThreadId))
                                {
                                    progress?.Report("Creating conversation thread... (90%)");
                                    var threadId = await _fileThreadService.CreateThreadAsync(apiKey);
                                    existingConversation.OpenAiThreadId = threadId;
                                    progress?.Report("Upload complete! (100%)");
                                }
                                else
                                {
                                    progress?.Report("Upload complete! (100%)");
                                }
                                
                                existingConversation.OpenAiFileIds = System.Text.Json.JsonSerializer.Serialize(existingFileIds);
                                
                                // Update uploaded document IDs - add new document to existing list
                                var existingDocIds = !string.IsNullOrEmpty(existingConversation.UploadedDocumentIds)
                                    ? System.Text.Json.JsonSerializer.Deserialize<List<int>>(existingConversation.UploadedDocumentIds) ?? new List<int>()
                                    : new List<int>();
                                
                                if (!existingDocIds.Contains(documentId.Value))
                                {
                                    existingDocIds.Add(documentId.Value);
                                }
                                
                                existingConversation.UploadedDocumentIds = System.Text.Json.JsonSerializer.Serialize(existingDocIds);
                            }
                        }
                        else
                        {
                            // Document removed - clear OpenAI thread info
                            existingConversation.OpenAiThreadId = null;
                            existingConversation.OpenAiAssistantId = null;
                            existingConversation.OpenAiFileIds = null;
                            existingConversation.UploadedDocumentIds = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error updating OpenAI thread for document in conversation {ConversationId}", conversationId);
                        // Continue without thread update
                    }
                }
                
                existingConversation.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        });
    }

    public async Task UpdateConversationSheetSelectionAsync(int conversationId, List<string>? selectedSheetNames)
    {
        await _dbSync.ExecuteAsync(async () =>
        {
            var existingConversation = await _context.ChatGptConversations.FindAsync(conversationId);
            if (existingConversation != null)
            {
                existingConversation.SelectedSheetNames = selectedSheetNames != null && selectedSheetNames.Any() 
                    ? System.Text.Json.JsonSerializer.Serialize(selectedSheetNames) 
                    : null;
                existingConversation.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        });
    }

    public async Task<List<ChatGptConversation>> GetConversationsAsync(string userId, int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.ChatGptConversations
                .Where(c => c.UserId == userId && (tenantId == null || c.TenantId == tenantId))
                .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
                .ToListAsync();
        });
    }

    public async Task<List<ChatGptMessage>> GetConversationMessagesAsync(int conversationId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.ChatGptMessages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();
        });
    }

    public async Task<ChatGptMessage> SaveMessageAsync(ChatGptMessage message)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            _context.ChatGptMessages.Add(message);
            await _context.SaveChangesAsync();
            return message;
        });
    }

    public async Task UpdateConversationTimestampAsync(int conversationId)
    {
        await _dbSync.ExecuteAsync(async () =>
        {
            var existingConversation = await _context.ChatGptConversations.FindAsync(conversationId);
            if (existingConversation != null)
            {
                existingConversation.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        });
    }

    public async Task<bool> DeleteConversationAsync(int conversationId, string userId, int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var conversation = await _context.ChatGptConversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && 
                                          c.UserId == userId && 
                                          (tenantId == null || c.TenantId == tenantId));

            if (conversation == null)
            {
                return false;
            }

            // Messages will be deleted automatically due to cascade delete
            _context.ChatGptConversations.Remove(conversation);
            await _context.SaveChangesAsync();
            return true;
        });
    }

    public async Task UpdateConversationTitleAsync(int conversationId, string title)
    {
        await _dbSync.ExecuteAsync(async () =>
        {
            var existingConversation = await _context.ChatGptConversations.FindAsync(conversationId);
            if (existingConversation != null)
            {
                existingConversation.Title = title;
                existingConversation.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        });
    }

    public async Task UpdateConversationPromptAsync(int conversationId, int? promptId)
    {
        await _dbSync.ExecuteAsync(async () =>
        {
            var existingConversation = await _context.ChatGptConversations.FindAsync(conversationId);
            if (existingConversation != null)
            {
                existingConversation.PromptId = promptId;
                existingConversation.UpdatedAt = DateTime.UtcNow;
                
                // If conversation uses Assistants API, update the assistant's instructions
                if (!string.IsNullOrEmpty(existingConversation.OpenAiAssistantId) && existingConversation.TenantId.HasValue)
                {
                    try
                    {
                        var apiKey = await GetDecryptedApiKeyAsync(existingConversation.UserId, existingConversation.TenantId);
                        
                        // Get prompt instructions
                        string assistantInstructions;
                        if (promptId.HasValue)
                        {
                            var prompt = await _promptService.GetPromptByIdAsync(promptId.Value, existingConversation.TenantId);
                            if (prompt != null)
                            {
                                assistantInstructions = prompt.PromptText;
                                // Remove {documentText} placeholder since documents are handled via file_search
                                assistantInstructions = assistantInstructions.Replace("{documentText}", "");
                                assistantInstructions = assistantInstructions.Trim();
                            }
                            else
                            {
                                assistantInstructions = "You are a helpful assistant that can answer questions about uploaded documents. Use the file_search tool to find relevant information from the documents.";
                            }
                        }
                        else
                        {
                            assistantInstructions = "You are a helpful assistant that can answer questions about uploaded documents. Use the file_search tool to find relevant information from the documents.";
                        }
                        
                        await _fileThreadService.UpdateAssistantInstructionsAsync(existingConversation.OpenAiAssistantId, assistantInstructions, apiKey);
                        _logger.LogInformation("Updated assistant {AssistantId} instructions for conversation {ConversationId}", 
                            existingConversation.OpenAiAssistantId, conversationId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error updating assistant instructions for conversation {ConversationId}", conversationId);
                        // Continue - prompt is still saved to conversation
                    }
                }
                
                await _context.SaveChangesAsync();
            }
        });
    }

    public async Task<string> SendConversationMessageWithDocumentsAsync(string userId, int? tenantId, int conversationId, string message)
    {
        if (tenantId == null)
        {
            throw new InvalidOperationException("Tenant ID is required. Please select an organization.");
        }

        var conversation = await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.ChatGptConversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId && c.TenantId == tenantId);
        });

        if (conversation == null)
        {
            throw new InvalidOperationException("Conversation not found");
        }

        // If conversation has OpenAI thread, assistant, and file IDs, use Assistants API
        if (!string.IsNullOrEmpty(conversation.OpenAiThreadId) && 
            !string.IsNullOrEmpty(conversation.OpenAiAssistantId) && 
            !string.IsNullOrEmpty(conversation.OpenAiFileIds))
        {
            try
            {
                var apiKey = await GetDecryptedApiKeyAsync(userId, tenantId);
                var fileIds = System.Text.Json.JsonSerializer.Deserialize<List<string>>(conversation.OpenAiFileIds) ?? new List<string>();
                
                _logger.LogInformation("Using Assistants API - Thread: {ThreadId}, Assistant: {AssistantId}, Files: {FileCount}", 
                    conversation.OpenAiThreadId, conversation.OpenAiAssistantId, fileIds.Count);
                
                var response = await _fileThreadService.AskQuestionAsync(
                    message, 
                    conversation.OpenAiThreadId, 
                    conversation.OpenAiAssistantId,
                    fileIds, 
                    apiKey);
                
                _logger.LogInformation("Assistants API response received successfully");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error using OpenAI Assistants API: {ErrorMessage}. Thread: {ThreadId}, Assistant: {AssistantId}", 
                    ex.Message, conversation.OpenAiThreadId, conversation.OpenAiAssistantId);
                // Fall through to regular chat API
            }
        }
        else
        {
            _logger.LogWarning("Conversation {ConversationId} missing required IDs for Assistants API. Thread: {ThreadId}, Assistant: {AssistantId}, Files: {FileIds}", 
                conversationId, 
                conversation.OpenAiThreadId ?? "null",
                conversation.OpenAiAssistantId ?? "null",
                conversation.OpenAiFileIds ?? "null");
        }

        // Fall back to regular chat API
        var messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = message } };
        return await SendConversationMessageAsync(userId, tenantId, messages);
    }

    public async Task<string> GenerateConversationTitleAsync(string userId, int? tenantId, string firstMessage)
    {
        try
        {
            // Create a simple prompt to generate a short title
            var titlePrompt = $"Generate a short, descriptive title (maximum 5-7 words) for a conversation that starts with this message: \"{firstMessage}\"\n\nReturn only the title, nothing else. Make it concise and descriptive.";
            
            var messages = new List<ChatMessage>
            {
                new ChatMessage
                {
                    Role = "system",
                    Content = "You are a helpful assistant that generates concise, descriptive titles for conversations. Always return only the title text, nothing else."
                },
                new ChatMessage
                {
                    Role = "user",
                    Content = titlePrompt
                }
            };

            var apiKey = await GetDecryptedApiKeyAsync(userId, tenantId);
            var preferredModel = await GetPreferredModelAsync(tenantId);
            
            // Use a cheaper/faster model for title generation
            var title = await CallChatGptWithMessagesAsync(messages, apiKey, "gpt-3.5-turbo");
            
            // Clean up the title - remove quotes, trim, and limit length
            title = title.Trim().Trim('"', '\'', '`');
            if (title.Length > 60)
            {
                title = title.Substring(0, 57) + "...";
            }
            
            return string.IsNullOrWhiteSpace(title) ? "New Conversation" : title;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate conversation title, using fallback");
            // Fallback: use first 50 characters of the message
            if (string.IsNullOrWhiteSpace(firstMessage))
            {
                return "New Conversation";
            }
            
            var fallbackTitle = firstMessage.Length > 50 
                ? firstMessage.Substring(0, 47) + "..." 
                : firstMessage;
            return fallbackTitle;
        }
    }

    public async Task<byte[]> ExportConversationToDocxAsync(int conversationId, string userId, int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            // Get conversation and verify access
            var conversation = await _context.ChatGptConversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && 
                                          c.UserId == userId && 
                                          (tenantId == null || c.TenantId == tenantId));

            if (conversation == null)
            {
                throw new InvalidOperationException("Conversation not found or access denied");
            }

            // Get messages ordered by creation time
            var messages = await _context.ChatGptMessages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();

            // Create DOCX document in memory
            using var memoryStream = new MemoryStream();
            using (var wordDocument = WordprocessingDocument.Create(memoryStream, WordprocessingDocumentType.Document))
            {
                // Add main document part
                var mainPart = wordDocument.AddMainDocumentPart();
                mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
                var body = mainPart.Document.AppendChild(new Body());

                // Add title
                var titleParagraph = body.AppendChild(new Paragraph());
                var titleRun = titleParagraph.AppendChild(new Run());
                titleRun.AppendChild(new RunProperties(new Bold(), new FontSize { Val = "32" }));
                titleRun.AppendChild(new Text(conversation.Title));
                
                // Add metadata paragraph
                var metaParagraph = body.AppendChild(new Paragraph());
                var metaRun = metaParagraph.AppendChild(new Run());
                metaRun.AppendChild(new RunProperties(new FontSize { Val = "22" }));
                var metadataText = $"Created: {conversation.CreatedAt:yyyy-MM-dd HH:mm:ss UTC}";
                if (conversation.UpdatedAt.HasValue)
                {
                    metadataText += $" | Updated: {conversation.UpdatedAt.Value:yyyy-MM-dd HH:mm:ss UTC}";
                }
                if (!string.IsNullOrEmpty(conversation.Model))
                {
                    metadataText += $" | Model: {conversation.Model}";
                }
                metaRun.AppendChild(new Text(metadataText));

                // Add spacing between metadata and messages
                body.AppendChild(new Paragraph());

                // Add messages
                foreach (var message in messages)
                {
                    // Message header with role and timestamp
                    var headerParagraph = body.AppendChild(new Paragraph());
                    var headerRun = headerParagraph.AppendChild(new Run());
                    headerRun.AppendChild(new RunProperties(
                        new Bold(),
                        new FontSize { Val = "20" },
                        new Color { Val = message.Role == "user" ? "0066CC" : "00AA00" }
                    ));
                    var roleDisplay = message.Role switch
                    {
                        "user" => "User",
                        "assistant" => "Assistant",
                        "system" => "System",
                        _ => message.Role
                    };
                    headerRun.AppendChild(new Text($"{roleDisplay} - {message.CreatedAt:yyyy-MM-dd HH:mm:ss UTC}"));

                    // Convert markdown to plain text (remove markdown syntax for simple display)
                    var content = message.Content;
                    // Basic markdown cleanup - remove code blocks, bold, italic markers
                    content = System.Text.RegularExpressions.Regex.Replace(content, @"```[\s\S]*?```", "[Code Block]", System.Text.RegularExpressions.RegexOptions.Multiline);
                    content = System.Text.RegularExpressions.Regex.Replace(content, @"`([^`]+)`", "$1");
                    content = System.Text.RegularExpressions.Regex.Replace(content, @"\*\*([^\*]+)\*\*", "$1");
                    content = System.Text.RegularExpressions.Regex.Replace(content, @"\*([^\*]+)\*", "$1");
                    
                    // Split content into paragraphs (preserve line breaks)
                    var paragraphs = content.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var para in paragraphs)
                    {
                        var contentParagraph = body.AppendChild(new Paragraph());
                        var contentRun = contentParagraph.AppendChild(new Run());
                        contentRun.AppendChild(new RunProperties(new FontSize { Val = "22" }));
                        // Replace single line breaks with spaces within paragraphs
                        contentRun.AppendChild(new Text(para.Replace("\n", " ").Replace("\r", " ").Trim()));
                    }
                    
                    // If no paragraphs were found (empty or single paragraph), add the content
                    if (paragraphs.Length == 0)
                    {
                        var contentParagraph = body.AppendChild(new Paragraph());
                        var contentRun = contentParagraph.AppendChild(new Run());
                        contentRun.AppendChild(new RunProperties(new FontSize { Val = "22" }));
                        contentRun.AppendChild(new Text(content.Trim()));
                    }

                    // Add spacing between messages
                    body.AppendChild(new Paragraph());
                    body.AppendChild(new Paragraph());
                }

                // Save the document
                mainPart.Document.Save();
            }

            return memoryStream.ToArray();
        });
    }

    public async Task<bool> DeleteConversationMessagesAsync(int conversationId, string userId, int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            // Verify the conversation exists and user has access
            var conversation = await _context.ChatGptConversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && 
                                          c.UserId == userId && 
                                          (tenantId == null || c.TenantId == tenantId));

            if (conversation == null)
            {
                return false;
            }

            // Delete all messages for this conversation
            var messages = await _context.ChatGptMessages
                .Where(m => m.ConversationId == conversationId)
                .ToListAsync();

            _context.ChatGptMessages.RemoveRange(messages);

            // Update conversation timestamp
            conversation.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        });
    }
}