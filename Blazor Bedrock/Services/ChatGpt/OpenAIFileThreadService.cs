using Blazor_Bedrock.Data;
using Blazor_Bedrock.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IO;

namespace Blazor_Bedrock.Services.ChatGpt;

public interface IOpenAIFileThreadService
{
    /// <summary>
    /// Checks if a file type is supported by file_search tool
    /// </summary>
    /// <param name="filename">The filename to check</param>
    /// <returns>True if the file type is supported by file_search</returns>
    bool IsFileTypeSupported(string filename);

    /// <summary>
    /// Uploads a file to OpenAI for use with assistants/threads
    /// </summary>
    /// <param name="fileStream">The file stream to upload</param>
    /// <param name="filename">The filename</param>
    /// <param name="userId">User ID for API key retrieval</param>
    /// <param name="tenantId">Tenant ID for API key retrieval</param>
    /// <returns>The file ID from OpenAI</returns>
    Task<string> UploadFileAsync(Stream fileStream, string filename, string userId, int? tenantId);

    /// <summary>
    /// Creates or gets an assistant for use with threads
    /// </summary>
    /// <param name="userId">User ID for API key retrieval</param>
    /// <param name="tenantId">Tenant ID for API key retrieval</param>
    /// <param name="model">The model to use (defaults to "gpt-4o")</param>
    /// <returns>The assistant ID from OpenAI</returns>
    Task<string> GetOrCreateAssistantAsync(string userId, int? tenantId, string? model = null);

    /// <summary>
    /// Creates a new thread in OpenAI
    /// </summary>
    /// <param name="userId">User ID for API key retrieval</param>
    /// <param name="tenantId">Tenant ID for API key retrieval</param>
    /// <returns>The thread ID from OpenAI</returns>
    Task<string> CreateThreadAsync(string userId, int? tenantId);

    /// <summary>
    /// Sends a question to a thread with optional file attachments
    /// </summary>
    /// <param name="question">The user's question</param>
    /// <param name="threadId">The thread ID to send the message to</param>
    /// <param name="fileIds">Optional list of file IDs to attach</param>
    /// <param name="filenames">Optional list of filenames corresponding to fileIds (for type checking)</param>
    /// <param name="userId">User ID for API key retrieval</param>
    /// <param name="tenantId">Tenant ID for API key retrieval</param>
    /// <param name="model">The model to use (defaults to "gpt-4o")</param>
    /// <returns>The assistant's response message</returns>
    Task<string> AskQuestionAsync(string question, string threadId, string userId, int? tenantId, List<string>? fileIds = null, List<string>? filenames = null, string? model = null);

    /// <summary>
    /// Gets the status of a run
    /// </summary>
    /// <param name="threadId">The thread ID</param>
    /// <param name="runId">The run ID</param>
    /// <param name="userId">User ID for API key retrieval</param>
    /// <param name="tenantId">Tenant ID for API key retrieval</param>
    /// <returns>The run status</returns>
    Task<string> GetRunStatusAsync(string threadId, string runId, string userId, int? tenantId);

    /// <summary>
    /// Gets all messages from a thread
    /// </summary>
    /// <param name="threadId">The thread ID</param>
    /// <param name="userId">User ID for API key retrieval</param>
    /// <param name="tenantId">Tenant ID for API key retrieval</param>
    /// <returns>List of messages</returns>
    Task<List<ThreadMessage>> GetThreadMessagesAsync(string threadId, string userId, int? tenantId);
}

public class ThreadMessage
{
    public string Id { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class OpenAIFileThreadService : IOpenAIFileThreadService
{
    private readonly HttpClient _httpClient;
    private readonly ApplicationDbContext _context;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IDatabaseSyncService _dbSync;
    private readonly ILogger<OpenAIFileThreadService> _logger;
    private const string BaseUrl = "https://api.openai.com/v1";

    public OpenAIFileThreadService(
        HttpClient httpClient,
        ApplicationDbContext context,
        IDataProtectionProvider dataProtectionProvider,
        IDatabaseSyncService dbSync,
        ILogger<OpenAIFileThreadService> logger)
    {
        _httpClient = httpClient;
        _context = context;
        _dataProtectionProvider = dataProtectionProvider;
        _dbSync = dbSync;
        _logger = logger;
        
        // Set default headers (will be overridden per request with API key)
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Blazor-Bedrock/1.0");
        _httpClient.Timeout = TimeSpan.FromMinutes(10); // Longer timeout for file uploads and runs
    }

    private async Task<string> GetApiKeyAsync(string userId, int? tenantId)
    {
        if (tenantId == null)
        {
            throw new InvalidOperationException("Tenant ID is required. Please select an organization.");
        }

        return await _dbSync.ExecuteAsync(async () =>
        {
            var apiKey = await _context.ChatGptApiKeys
                .FirstOrDefaultAsync(k => k.TenantId == tenantId && k.IsActive);

            if (apiKey == null)
            {
                throw new InvalidOperationException("No API key found for the current organization. Please configure an API key in Settings.");
            }

            var protector = _dataProtectionProvider.CreateProtector("ChatGptApiKey");
            return protector.Unprotect(apiKey.EncryptedApiKey);
        });
    }

    // File types supported by file_search tool in Threads API v2
    private static readonly HashSet<string> SupportedFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".c", ".cpp", ".cs", ".css", ".csv", ".doc", ".docx", ".go", ".html", ".java", 
        ".js", ".json", ".md", ".pdf", ".php", ".pptx", ".py", ".rb", ".sh", 
        ".tex", ".ts", ".txt"
    };

    public bool IsFileTypeSupported(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return false;
            
        var extension = Path.GetExtension(filename);
        return SupportedFileExtensions.Contains(extension);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string apiKey)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Add("User-Agent", "Blazor-Bedrock/1.0");
        request.Headers.Add("OpenAI-Beta", "assistants=v2");
        return request;
    }

    public async Task<string> UploadFileAsync(Stream fileStream, string filename, string userId, int? tenantId)
    {
        try
        {
            var apiKey = await GetApiKeyAsync(userId, tenantId);
            var request = CreateRequest(HttpMethod.Post, $"{BaseUrl}/files", apiKey);

            using var content = new MultipartFormDataContent();
            var fileContent = new StreamContent(fileStream);

            // Determine content type dynamically
            var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(filename, out var contentType))
            {
                contentType = "application/octet-stream";
            }
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            content.Add(fileContent, "file", filename);
            content.Add(new StringContent("assistants"), "purpose");

            request.Content = content;
            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to upload file to OpenAI: {StatusCode}, {Error}", response.StatusCode, errorContent);
                throw new HttpRequestException($"Failed to upload file: {response.StatusCode} - {errorContent}");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            if (!doc.RootElement.TryGetProperty("id", out var idElement))
            {
                throw new InvalidOperationException("OpenAI API did not return a file ID");
            }

            var fileId = idElement.GetString();
            if (string.IsNullOrEmpty(fileId))
            {
                throw new InvalidOperationException("OpenAI API returned an empty file ID");
            }

            _logger.LogInformation("Successfully uploaded file {Filename} with ID {FileId}", filename, fileId);
            return fileId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file {Filename}", filename);
            throw;
        }
    }

    public async Task<string> GetOrCreateAssistantAsync(string userId, int? tenantId, string? model = null)
    {
        try
        {
            var apiKey = await GetApiKeyAsync(userId, tenantId);
            model ??= "gpt-4o";

            // For simplicity, we'll create a new assistant each time
            // In production, you might want to cache/store assistant IDs per tenant
            var assistantPayload = new
            {
                model = model,
                name = "Document Assistant",
                instructions = "You are a helpful assistant that analyzes and answers questions about uploaded documents.",
                tools = new[]
                {
                    new { type = "file_search" }
                }
            };

            var assistantJson = JsonSerializer.Serialize(assistantPayload);
            var assistantRequest = CreateRequest(HttpMethod.Post, $"{BaseUrl}/assistants", apiKey);
            assistantRequest.Content = new StringContent(assistantJson, Encoding.UTF8, "application/json");
            var assistantResponse = await _httpClient.SendAsync(assistantRequest);

            if (!assistantResponse.IsSuccessStatusCode)
            {
                var errorContent = await assistantResponse.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create assistant: {StatusCode}, {Error}", assistantResponse.StatusCode, errorContent);
                throw new HttpRequestException($"Failed to create assistant: {assistantResponse.StatusCode} - {errorContent}");
            }

            var json = await assistantResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("id", out var idElement))
            {
                throw new InvalidOperationException("OpenAI API did not return an assistant ID");
            }

            var assistantId = idElement.GetString();
            if (string.IsNullOrEmpty(assistantId))
            {
                throw new InvalidOperationException("OpenAI API returned an empty assistant ID");
            }

            _logger.LogInformation("Successfully created assistant with ID {AssistantId}", assistantId);
            return assistantId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating assistant");
            throw;
        }
    }

    public async Task<string> CreateThreadAsync(string userId, int? tenantId)
    {
        try
        {
            var apiKey = await GetApiKeyAsync(userId, tenantId);
            var request = CreateRequest(HttpMethod.Post, $"{BaseUrl}/threads", apiKey);

            // Create thread with empty body (OpenAI creates thread with no initial messages)
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create thread: {StatusCode}, {Error}", response.StatusCode, errorContent);
                throw new HttpRequestException($"Failed to create thread: {response.StatusCode} - {errorContent}");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            if (!doc.RootElement.TryGetProperty("id", out var idElement))
            {
                throw new InvalidOperationException("OpenAI API did not return a thread ID");
            }

            var threadId = idElement.GetString();
            if (string.IsNullOrEmpty(threadId))
            {
                throw new InvalidOperationException("OpenAI API returned an empty thread ID");
            }

            _logger.LogInformation("Successfully created thread with ID {ThreadId}", threadId);
            return threadId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating thread");
            throw;
        }
    }

    public async Task<string> AskQuestionAsync(string question, string threadId, string userId, int? tenantId, List<string>? fileIds = null, List<string>? filenames = null, string? model = null)
    {
        try
        {
            var apiKey = await GetApiKeyAsync(userId, tenantId);
            model ??= "gpt-4o";

            // 1. Add message to thread
            // In Threads API v2, content must be an array and files are in attachments
            // Note: file_search only supports certain file types (not CSV, JSONL, etc.)
            object messagePayload;
            
            if (fileIds != null && fileIds.Any())
            {
                // Filter out unsupported file types for file_search
                var supportedFileIds = new List<string>();
                var unsupportedFiles = new List<string>();
                
                for (int i = 0; i < fileIds.Count; i++)
                {
                    var fileId = fileIds[i];
                    var filename = (filenames != null && i < filenames.Count) ? filenames[i] : null;
                    
                    if (!string.IsNullOrEmpty(filename) && IsFileTypeSupported(filename))
                    {
                        supportedFileIds.Add(fileId);
                    }
                    else if (!string.IsNullOrEmpty(filename))
                    {
                        unsupportedFiles.Add(filename);
                    }
                    else
                    {
                        // If we don't have filename, include it anyway and let OpenAI handle it
                        supportedFileIds.Add(fileId);
                    }
                }
                
                if (unsupportedFiles.Any())
                {
                    _logger.LogWarning("Skipping {Count} unsupported file(s) for file_search: {Files}", 
                        unsupportedFiles.Count, string.Join(", ", unsupportedFiles));
                }
                
                if (supportedFileIds.Any())
                {
                    // With file attachments (only supported types)
                    messagePayload = new
                    {
                        role = "user",
                        content = new[]
                        {
                            new
                            {
                                type = "text",
                                text = question
                            }
                        },
                        attachments = supportedFileIds.Select(fileId => new
                        {
                            file_id = fileId,
                            tools = new[]
                            {
                                new { type = "file_search" }
                            }
                        }).ToArray()
                    };
                }
                else
                {
                    // All files were unsupported - send message without attachments
                    messagePayload = new
                    {
                        role = "user",
                        content = question
                    };
                }
            }
            else
            {
                // Without file attachments - content can be a simple string or array
                messagePayload = new
                {
                    role = "user",
                    content = question
                };
            }
            
            var messageJson = JsonSerializer.Serialize(messagePayload);
            var messageRequest = CreateRequest(HttpMethod.Post, $"{BaseUrl}/threads/{threadId}/messages", apiKey);
            messageRequest.Content = new StringContent(messageJson, Encoding.UTF8, "application/json");
            var messageResponse = await _httpClient.SendAsync(messageRequest);

            if (!messageResponse.IsSuccessStatusCode)
            {
                var errorContent = await messageResponse.Content.ReadAsStringAsync();
                _logger.LogError("Failed to add message to thread: {StatusCode}, {Error}", messageResponse.StatusCode, errorContent);
                throw new HttpRequestException($"Failed to add message: {messageResponse.StatusCode} - {errorContent}");
            }

            // 2. Get or create an assistant (required for runs)
            var assistantId = await GetOrCreateAssistantAsync(userId, tenantId, model);

            // 3. Run the model with the assistant
            var runPayload = new
            {
                assistant_id = assistantId,
                model = model // Optional: override the assistant's default model
            };
            var runJson = JsonSerializer.Serialize(runPayload);
            var runRequest = CreateRequest(HttpMethod.Post, $"{BaseUrl}/threads/{threadId}/runs", apiKey);
            runRequest.Content = new StringContent(runJson, Encoding.UTF8, "application/json");
            var runResponse = await _httpClient.SendAsync(runRequest);

            if (!runResponse.IsSuccessStatusCode)
            {
                var errorContent = await runResponse.Content.ReadAsStringAsync();
                _logger.LogError("Failed to start run: {StatusCode}, {Error}", runResponse.StatusCode, errorContent);
                throw new HttpRequestException($"Failed to start run: {runResponse.StatusCode} - {errorContent}");
            }

            var runJsonResponse = await runResponse.Content.ReadAsStringAsync();
            using var runDoc = JsonDocument.Parse(runJsonResponse);
            
            if (!runDoc.RootElement.TryGetProperty("id", out var runIdElement))
            {
                throw new InvalidOperationException("OpenAI API did not return a run ID");
            }

            var runId = runIdElement.GetString();
            if (string.IsNullOrEmpty(runId))
            {
                throw new InvalidOperationException("OpenAI API returned an empty run ID");
            }

            _logger.LogInformation("Started run {RunId} for thread {ThreadId} with assistant {AssistantId}", runId, threadId, assistantId);

            // 4. Poll until run is completed
            string status = "";
            int pollCount = 0;
            const int maxPolls = 120; // Maximum 2 minutes (120 * 1 second)
            
            do
            {
                await Task.Delay(1000); // Wait 1 second between polls
                pollCount++;

                if (pollCount > maxPolls)
                {
                    throw new TimeoutException($"Run {runId} did not complete within {maxPolls} seconds");
                }

                var statusRequest = CreateRequest(HttpMethod.Get, $"{BaseUrl}/threads/{threadId}/runs/{runId}", apiKey);
                var statusResponse = await _httpClient.SendAsync(statusRequest);
                
                if (!statusResponse.IsSuccessStatusCode)
                {
                    var errorContent = await statusResponse.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to get run status: {StatusCode}, {Error}", statusResponse.StatusCode, errorContent);
                    throw new HttpRequestException($"Failed to get run status: {statusResponse.StatusCode} - {errorContent}");
                }

                var statusJson = await statusResponse.Content.ReadAsStringAsync();
                using var statusDoc = JsonDocument.Parse(statusJson);
                
                if (!statusDoc.RootElement.TryGetProperty("status", out var statusElement))
                {
                    throw new InvalidOperationException("OpenAI API did not return a status");
                }

                status = statusElement.GetString() ?? "";

                // Check for failed or cancelled status
                if (status == "failed" || status == "cancelled" || status == "expired")
                {
                    string? errorMessage = null;
                    if (statusDoc.RootElement.TryGetProperty("last_error", out var errorElement))
                    {
                        if (errorElement.TryGetProperty("message", out var messageElement))
                        {
                            errorMessage = messageElement.GetString();
                        }
                    }
                    throw new InvalidOperationException($"Run {runId} {status}. Error: {errorMessage ?? "Unknown error"}");
                }

            } while (status != "completed");

            _logger.LogInformation("Run {RunId} completed after {PollCount} polls", runId, pollCount);

            // 5. Get final response - get the latest assistant message
            var messagesRequest = CreateRequest(HttpMethod.Get, $"{BaseUrl}/threads/{threadId}/messages?order=desc&limit=1", apiKey);
            var messagesResponse = await _httpClient.SendAsync(messagesRequest);
            
            if (!messagesResponse.IsSuccessStatusCode)
            {
                var errorContent = await messagesResponse.Content.ReadAsStringAsync();
                _logger.LogError("Failed to get messages: {StatusCode}, {Error}", messagesResponse.StatusCode, errorContent);
                throw new HttpRequestException($"Failed to get messages: {messagesResponse.StatusCode} - {errorContent}");
            }

            var messagesJson = await messagesResponse.Content.ReadAsStringAsync();
            using var messagesDoc = JsonDocument.Parse(messagesJson);

            if (!messagesDoc.RootElement.TryGetProperty("data", out var dataElement) || 
                dataElement.ValueKind != JsonValueKind.Array || 
                dataElement.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("No messages found in thread response");
            }

            var firstMessage = dataElement[0];
            
            // Verify it's an assistant message
            if (!firstMessage.TryGetProperty("role", out var roleElement) || 
                roleElement.GetString() != "assistant")
            {
                throw new InvalidOperationException("Latest message is not from assistant");
            }

            // Extract content
            if (!firstMessage.TryGetProperty("content", out var contentElement) ||
                contentElement.ValueKind != JsonValueKind.Array ||
                contentElement.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("Message content is empty or invalid");
            }

            var firstContentItem = contentElement[0];
            if (!firstContentItem.TryGetProperty("type", out var typeElement) ||
                typeElement.GetString() != "text")
            {
                throw new InvalidOperationException("Message content type is not text");
            }

            if (!firstContentItem.TryGetProperty("text", out var textElement) ||
                !textElement.TryGetProperty("value", out var valueElement))
            {
                throw new InvalidOperationException("Message text value is missing");
            }

            var responseText = valueElement.GetString();
            if (string.IsNullOrEmpty(responseText))
            {
                throw new InvalidOperationException("Assistant response is empty");
            }

            return responseText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error asking question in thread {ThreadId}", threadId);
            throw;
        }
    }

    public async Task<string> GetRunStatusAsync(string threadId, string runId, string userId, int? tenantId)
    {
        try
        {
            var apiKey = await GetApiKeyAsync(userId, tenantId);
            var request = CreateRequest(HttpMethod.Get, $"{BaseUrl}/threads/{threadId}/runs/{runId}", apiKey);
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("status", out var statusElement))
            {
                return statusElement.GetString() ?? "unknown";
            }

            return "unknown";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting run status for run {RunId}", runId);
            throw;
        }
    }

    public async Task<List<ThreadMessage>> GetThreadMessagesAsync(string threadId, string userId, int? tenantId)
    {
        try
        {
            var apiKey = await GetApiKeyAsync(userId, tenantId);
            var request = CreateRequest(HttpMethod.Get, $"{BaseUrl}/threads/{threadId}/messages", apiKey);
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            var messages = new List<ThreadMessage>();

            if (doc.RootElement.TryGetProperty("data", out var dataElement) && 
                dataElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var messageElement in dataElement.EnumerateArray())
                {
                    var message = new ThreadMessage();

                    if (messageElement.TryGetProperty("id", out var idElement))
                    {
                        message.Id = idElement.GetString() ?? string.Empty;
                    }

                    if (messageElement.TryGetProperty("role", out var roleElement))
                    {
                        message.Role = roleElement.GetString() ?? string.Empty;
                    }

                    if (messageElement.TryGetProperty("created_at", out var createdAtElement))
                    {
                        if (createdAtElement.ValueKind == JsonValueKind.Number)
                        {
                            var unixTimestamp = createdAtElement.GetInt64();
                            message.CreatedAt = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).DateTime;
                        }
                    }

                    // Extract text content
                    if (messageElement.TryGetProperty("content", out var contentElement) &&
                        contentElement.ValueKind == JsonValueKind.Array)
                    {
                        var contentParts = new List<string>();
                        foreach (var contentItem in contentElement.EnumerateArray())
                        {
                            if (contentItem.TryGetProperty("type", out var typeElement) &&
                                typeElement.GetString() == "text")
                            {
                                if (contentItem.TryGetProperty("text", out var textElement) &&
                                    textElement.TryGetProperty("value", out var valueElement))
                                {
                                    contentParts.Add(valueElement.GetString() ?? string.Empty);
                                }
                            }
                        }
                        message.Content = string.Join("\n", contentParts);
                    }

                    messages.Add(message);
                }
            }

            return messages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting messages for thread {ThreadId}", threadId);
            throw;
        }
    }
}
