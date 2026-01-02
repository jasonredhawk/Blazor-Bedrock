using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.StaticFiles;

namespace Blazor_Bedrock.Services.ChatGpt;

public interface IOpenAIFileThreadService
{
    Task<string> UploadFileAsync(Stream fileStream, string fileName, string apiKey);
    Task<string> CreateThreadAsync(string apiKey);
    Task<string> CreateAssistantAsync(string apiKey, string? model = null, List<string>? fileIds = null, IProgress<string>? progress = null, string? instructions = null);
    Task<string> GetAssistantAsync(string assistantId, string apiKey);
    Task<string> UpdateAssistantWithVectorStoreAsync(string assistantId, string vectorStoreId, string apiKey);
    Task<string> UpdateAssistantInstructionsAsync(string assistantId, string instructions, string apiKey);
    Task<string> CreateVectorStoreAsync(string apiKey, string? name = null);
    Task<string> AddFilesToVectorStoreAsync(string vectorStoreId, List<string> fileIds, string apiKey, IProgress<string>? progress = null);
    Task<string> AskQuestionAsync(string question, string threadId, string assistantId, List<string> fileIds, string apiKey);
}

public class OpenAIFileThreadService : IOpenAIFileThreadService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAIFileThreadService> _logger;

    public OpenAIFileThreadService(HttpClient httpClient, ILogger<OpenAIFileThreadService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Blazor-Bedrock/1.0");
    }

    public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string apiKey)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new StreamContent(fileStream);

            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(fileName, out var contentType))
            {
                contentType = "application/octet-stream";
            }
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            content.Add(fileContent, "file", fileName);
            content.Add(new StringContent("assistants"), "purpose");

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/files")
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("User-Agent", "Blazor-Bedrock/1.0");

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("OpenAI API error uploading file: Status {StatusCode}, Response: {ErrorContent}", 
                    response.StatusCode, errorContent);
                throw new HttpRequestException($"OpenAI API returned error {response.StatusCode}: {errorContent}");
            }
            
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("id").GetString() ?? throw new Exception("Failed to get file ID from OpenAI response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file to OpenAI: {FileName}", fileName);
            throw;
        }
    }

    public async Task<string> CreateThreadAsync(string apiKey)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/threads")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("User-Agent", "Blazor-Bedrock/1.0");
            request.Headers.Add("OpenAI-Beta", "assistants=v2");

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("OpenAI API error creating thread: Status {StatusCode}, Response: {ErrorContent}", 
                    response.StatusCode, errorContent);
                throw new HttpRequestException($"OpenAI API returned error {response.StatusCode}: {errorContent}");
            }
            
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("id").GetString() ?? throw new Exception("Failed to get thread ID from OpenAI response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating OpenAI thread");
            throw;
        }
    }

    public async Task<string> CreateVectorStoreAsync(string apiKey, string? name = null)
    {
        try
        {
            var vectorStorePayload = new
            {
                name = name ?? "Document Vector Store"
            };
            var vectorStoreJson = JsonSerializer.Serialize(vectorStorePayload);
            
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/vector_stores")
            {
                Content = new StringContent(vectorStoreJson, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("User-Agent", "Blazor-Bedrock/1.0");
            request.Headers.Add("OpenAI-Beta", "assistants=v2");

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("OpenAI API error creating vector store: Status {StatusCode}, Response: {ErrorContent}", 
                    response.StatusCode, errorContent);
                throw new HttpRequestException($"OpenAI API returned error {response.StatusCode}: {errorContent}");
            }
            
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("id").GetString() ?? throw new Exception("Failed to get vector store ID from OpenAI response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating OpenAI vector store");
            throw;
        }
    }

    public async Task<string> AddFilesToVectorStoreAsync(string vectorStoreId, List<string> fileIds, string apiKey, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report("Uploading files to vector store... (50%)");
            
            var batchPayload = new
            {
                file_ids = fileIds
            };
            var batchJson = JsonSerializer.Serialize(batchPayload);
            
            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.openai.com/v1/vector_stores/{vectorStoreId}/file_batches")
            {
                Content = new StringContent(batchJson, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("User-Agent", "Blazor-Bedrock/1.0");
            request.Headers.Add("OpenAI-Beta", "assistants=v2");

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("OpenAI API error adding files to vector store: Status {StatusCode}, Response: {ErrorContent}", 
                    response.StatusCode, errorContent);
                throw new HttpRequestException($"OpenAI API returned error {response.StatusCode}: {errorContent}");
            }
            
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var batchId = doc.RootElement.GetProperty("id").GetString() ?? throw new Exception("Failed to get batch ID from OpenAI response");
            
            progress?.Report("Processing files (this may take a few minutes for large files)... (60%)");
            
            // Poll for batch completion - reduce frequency and logging
            int maxAttempts = 120; // Increased to 2 minutes for large files
            int attempts = 0;
            string status = "in_progress";
            int lastLoggedAttempt = -5; // Log every 5 attempts
            
            do
            {
                await Task.Delay(2000); // Increased from 1000ms to 2000ms to reduce API calls
                attempts++;
                
                // Only log every 5 attempts (every 10 seconds) to reduce log spam
                if (attempts - lastLoggedAttempt >= 5)
                {
                    var progressPercent = Math.Min(100, (attempts * 100) / maxAttempts);
                    progress?.Report($"Processing files... {progressPercent}% (estimated)");
                    lastLoggedAttempt = attempts;
                }
                
                var statusRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"https://api.openai.com/v1/vector_stores/{vectorStoreId}/file_batches/{batchId}");
                statusRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                statusRequest.Headers.Add("User-Agent", "Blazor-Bedrock/1.0");
                statusRequest.Headers.Add("OpenAI-Beta", "assistants=v2");
                
                var statusResponse = await _httpClient.SendAsync(statusRequest);
                
                if (!statusResponse.IsSuccessStatusCode)
                {
                    var errorContent = await statusResponse.Content.ReadAsStringAsync();
                    _logger.LogError("OpenAI API error checking batch status: Status {StatusCode}, Response: {ErrorContent}", 
                        statusResponse.StatusCode, errorContent);
                    throw new HttpRequestException($"OpenAI API returned error {statusResponse.StatusCode}: {errorContent}");
                }
                
                var statusJson = await statusResponse.Content.ReadAsStringAsync();
                using var statusDoc = JsonDocument.Parse(statusJson);
                status = statusDoc.RootElement.GetProperty("status").GetString() ?? "unknown";
                
                // Try to get file counts for better progress reporting
                if (statusDoc.RootElement.TryGetProperty("file_counts", out var fileCounts))
                {
                    if (fileCounts.TryGetProperty("in_progress", out var inProgress) && 
                        fileCounts.TryGetProperty("completed", out var completed) &&
                        fileCounts.TryGetProperty("failed", out var failed))
                    {
                        var total = inProgress.GetInt32() + completed.GetInt32() + failed.GetInt32();
                        if (total > 0)
                        {
                            var completedCount = completed.GetInt32();
                            var progressPercent = (completedCount * 100) / total;
                            progress?.Report($"Processing files... {completedCount}/{total} completed ({progressPercent}%)");
                        }
                    }
                }
                
                if (status == "failed" || status == "cancelled")
                {
                    var errorMessage = $"Vector store batch {status}";
                    if (statusDoc.RootElement.TryGetProperty("errors", out var errors))
                    {
                        errorMessage += $": {errors}";
                    }
                    throw new Exception(errorMessage);
                }
            } while (status != "completed" && attempts < maxAttempts);
            
            if (status != "completed")
            {
                throw new Exception($"Vector store batch did not complete in time. Status: {status}");
            }
            
            progress?.Report("Files processed successfully! (100%)");
            return batchId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding files to OpenAI vector store");
            progress?.Report($"Error: {ex.Message}");
            throw;
        }
    }

    public async Task<string> CreateAssistantAsync(string apiKey, string? model = null, List<string>? fileIds = null, IProgress<string>? progress = null, string? instructions = null)
    {
        try
        {
            // Create vector store if files are provided
            string? vectorStoreId = null;
            if (fileIds != null && fileIds.Any())
            {
                progress?.Report("Creating vector store...");
                vectorStoreId = await CreateVectorStoreAsync(apiKey);
                await AddFilesToVectorStoreAsync(vectorStoreId, fileIds, apiKey, progress);
            }
            
            // Use provided instructions or default
            var defaultInstructions = "You are a helpful assistant that can answer questions about uploaded documents. Use the file_search tool to find relevant information from the documents.";
            var assistantInstructions = !string.IsNullOrWhiteSpace(instructions) ? instructions : defaultInstructions;
            
            var assistantPayload = new
            {
                model = model ?? "gpt-4o",
                name = "Document Assistant",
                instructions = assistantInstructions,
                tools = new[] { new { type = "file_search" } },
                tool_resources = vectorStoreId != null ? new
                {
                    file_search = new
                    {
                        vector_store_ids = new[] { vectorStoreId }
                    }
                } : null
            };
            var assistantJson = JsonSerializer.Serialize(assistantPayload);
            
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/assistants")
            {
                Content = new StringContent(assistantJson, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("User-Agent", "Blazor-Bedrock/1.0");
            request.Headers.Add("OpenAI-Beta", "assistants=v2");

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("OpenAI API error creating assistant: Status {StatusCode}, Response: {ErrorContent}", 
                    response.StatusCode, errorContent);
                throw new HttpRequestException($"OpenAI API returned error {response.StatusCode}: {errorContent}");
            }
            
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("id").GetString() ?? throw new Exception("Failed to get assistant ID from OpenAI response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating OpenAI assistant");
            throw;
        }
    }

    public async Task<string> GetAssistantAsync(string assistantId, string apiKey)
    {
        try
        {
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.openai.com/v1/assistants/{assistantId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("User-Agent", "Blazor-Bedrock/1.0");
            request.Headers.Add("OpenAI-Beta", "assistants=v2");

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("OpenAI API error getting assistant: Status {StatusCode}, Response: {ErrorContent}", 
                    response.StatusCode, errorContent);
                throw new HttpRequestException($"OpenAI API returned error {response.StatusCode}: {errorContent}");
            }
            
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting OpenAI assistant");
            throw;
        }
    }

    public async Task<string> UpdateAssistantWithVectorStoreAsync(string assistantId, string vectorStoreId, string apiKey)
    {
        try
        {
            // OpenAI only allows 1 vector store per assistant, so we just set it directly
            var updatePayload = new
            {
                tool_resources = new
                {
                    file_search = new
                    {
                        vector_store_ids = new[] { vectorStoreId }
                    }
                }
            };
            var updateJson = JsonSerializer.Serialize(updatePayload);
            
            var request = new HttpRequestMessage(
                HttpMethod.Post, // OpenAI Assistants API uses POST for updates
                $"https://api.openai.com/v1/assistants/{assistantId}")
            {
                Content = new StringContent(updateJson, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("User-Agent", "Blazor-Bedrock/1.0");
            request.Headers.Add("OpenAI-Beta", "assistants=v2");

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("OpenAI API error updating assistant: Status {StatusCode}, Response: {ErrorContent}", 
                    response.StatusCode, errorContent);
                throw new HttpRequestException($"OpenAI API returned error {response.StatusCode}: {errorContent}");
            }
            
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("id").GetString() ?? throw new Exception("Failed to get assistant ID from OpenAI response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating OpenAI assistant with vector store");
            throw;
        }
    }

    public async Task<string> UpdateAssistantInstructionsAsync(string assistantId, string instructions, string apiKey)
    {
        try
        {
            var updatePayload = new
            {
                instructions = instructions
            };
            var updateJson = JsonSerializer.Serialize(updatePayload);
            
            var request = new HttpRequestMessage(
                HttpMethod.Post, // OpenAI Assistants API uses POST for updates
                $"https://api.openai.com/v1/assistants/{assistantId}")
            {
                Content = new StringContent(updateJson, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("User-Agent", "Blazor-Bedrock/1.0");
            request.Headers.Add("OpenAI-Beta", "assistants=v2");

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("OpenAI API error updating assistant instructions: Status {StatusCode}, Response: {ErrorContent}", 
                    response.StatusCode, errorContent);
                throw new HttpRequestException($"OpenAI API returned error {response.StatusCode}: {errorContent}");
            }
            
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("id").GetString() ?? throw new Exception("Failed to get assistant ID from OpenAI response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating OpenAI assistant instructions");
            throw;
        }
    }

    public async Task<string> AskQuestionAsync(string question, string threadId, string assistantId, List<string> fileIds, string apiKey)
    {
        try
        {
            // Create message - files should be in assistant's vector store, not attached to message
            // However, if files are provided and not in vector store, attach them to the message
            var messagePayload = new
            {
                role = "user",
                content = question
            };
            
            // Note: In v2, files should be in the assistant's vector store via tool_resources
            // Message attachments are optional and create thread-level vector stores
            // We'll rely on the assistant's vector store for file_search
            var messageJson = JsonSerializer.Serialize(messagePayload);
            
            var messageRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.openai.com/v1/threads/{threadId}/messages")
            {
                Content = new StringContent(messageJson, Encoding.UTF8, "application/json")
            };
            messageRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            messageRequest.Headers.Add("User-Agent", "Blazor-Bedrock/1.0");
            messageRequest.Headers.Add("OpenAI-Beta", "assistants=v2");

            var messageResponse = await _httpClient.SendAsync(messageRequest);
            
            if (!messageResponse.IsSuccessStatusCode)
            {
                var errorContent = await messageResponse.Content.ReadAsStringAsync();
                _logger.LogError("OpenAI API error creating message: Status {StatusCode}, Response: {ErrorContent}", 
                    messageResponse.StatusCode, errorContent);
                throw new HttpRequestException($"OpenAI API returned error {messageResponse.StatusCode}: {errorContent}");
            }

            // Create run with assistant_id
            var runPayload = new
            {
                assistant_id = assistantId
            };
            var runJson = JsonSerializer.Serialize(runPayload);
            
            var runRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.openai.com/v1/threads/{threadId}/runs")
            {
                Content = new StringContent(runJson, Encoding.UTF8, "application/json")
            };
            runRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            runRequest.Headers.Add("User-Agent", "Blazor-Bedrock/1.0");
            runRequest.Headers.Add("OpenAI-Beta", "assistants=v2");

            var runResponse = await _httpClient.SendAsync(runRequest);
            
            if (!runResponse.IsSuccessStatusCode)
            {
                var errorContent = await runResponse.Content.ReadAsStringAsync();
                _logger.LogError("OpenAI API error creating run: Status {StatusCode}, Response: {ErrorContent}", 
                    runResponse.StatusCode, errorContent);
                throw new HttpRequestException($"OpenAI API returned error {runResponse.StatusCode}: {errorContent}");
            }
            
            var runDoc = JsonDocument.Parse(await runResponse.Content.ReadAsStringAsync());
            var runId = runDoc.RootElement.GetProperty("id").GetString() ?? throw new Exception("Failed to get run ID from OpenAI response");

            // Poll for completion
            string status;
            int maxAttempts = 60; // 60 seconds max wait
            int attempts = 0;
            
            do
            {
                await Task.Delay(1000);
                attempts++;
                
                var statusRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"https://api.openai.com/v1/threads/{threadId}/runs/{runId}");
                statusRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                statusRequest.Headers.Add("User-Agent", "Blazor-Bedrock/1.0");
                statusRequest.Headers.Add("OpenAI-Beta", "assistants=v2");

                var statusResponse = await _httpClient.SendAsync(statusRequest);
                
                if (!statusResponse.IsSuccessStatusCode)
                {
                    var errorContent = await statusResponse.Content.ReadAsStringAsync();
                    _logger.LogError("OpenAI API error checking run status: Status {StatusCode}, Response: {ErrorContent}", 
                        statusResponse.StatusCode, errorContent);
                    throw new HttpRequestException($"OpenAI API returned error {statusResponse.StatusCode}: {errorContent}");
                }
                
                var statusJson = await statusResponse.Content.ReadAsStringAsync();
                using var statusDoc = JsonDocument.Parse(statusJson);
                status = statusDoc.RootElement.GetProperty("status").GetString() ?? "unknown";
                
                if (status == "failed" || status == "cancelled" || status == "expired")
                {
                    var errorMessage = "Run failed";
                    if (statusDoc.RootElement.TryGetProperty("last_error", out var lastError))
                    {
                        if (lastError.TryGetProperty("message", out var errorMsg))
                        {
                            errorMessage = errorMsg.GetString() ?? errorMessage;
                        }
                    }
                    throw new Exception($"OpenAI run {status}: {errorMessage}");
                }
            } while (status != "completed" && attempts < maxAttempts);

            if (status != "completed")
            {
                throw new Exception($"OpenAI run did not complete in time. Status: {status}");
            }

            // Get messages
            var messagesRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.openai.com/v1/threads/{threadId}/messages");
            messagesRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            messagesRequest.Headers.Add("User-Agent", "Blazor-Bedrock/1.0");
            messagesRequest.Headers.Add("OpenAI-Beta", "assistants=v2");

            var messagesResponse = await _httpClient.SendAsync(messagesRequest);
            
            if (!messagesResponse.IsSuccessStatusCode)
            {
                var errorContent = await messagesResponse.Content.ReadAsStringAsync();
                _logger.LogError("OpenAI API error getting messages: Status {StatusCode}, Response: {ErrorContent}", 
                    messagesResponse.StatusCode, errorContent);
                throw new HttpRequestException($"OpenAI API returned error {messagesResponse.StatusCode}: {errorContent}");
            }
            
            var messagesJson = await messagesResponse.Content.ReadAsStringAsync();
            using var messagesDoc = JsonDocument.Parse(messagesJson);

            // Get the first (most recent) assistant message
            if (messagesDoc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in data.EnumerateArray())
                {
                    if (message.TryGetProperty("role", out var role) && 
                        role.GetString() == "assistant" &&
                        message.TryGetProperty("content", out var content) &&
                        content.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var contentItem in content.EnumerateArray())
                        {
                            if (contentItem.TryGetProperty("type", out var type) && 
                                type.GetString() == "text" &&
                                contentItem.TryGetProperty("text", out var text) &&
                                text.TryGetProperty("value", out var value))
                            {
                                return value.GetString() ?? "No response generated.";
                            }
                        }
                    }
                }
            }

            return "Unable to parse response from OpenAI API.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error asking question in OpenAI thread: {ThreadId}", threadId);
            throw;
        }
    }
}
