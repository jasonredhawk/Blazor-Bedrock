using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Infrastructure.ExternalApis;
using Blazor_Bedrock.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pinecone;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Blazor_Bedrock.Services.RAG;

public interface IRagService
{
    /// <summary>
    /// Processes a document: chunks text, creates embeddings, and stores in Pinecone
    /// </summary>
    Task ProcessDocumentAsync(int documentId, string userId, int? tenantId);

    /// <summary>
    /// Asks a question using RAG: searches Pinecone for relevant chunks and answers with GPT-4o
    /// </summary>
    Task<string> AskQuestionAsync(string question, int documentId, string userId, int? tenantId, int topK = 5);

    /// <summary>
    /// Checks if a document has been processed (has embeddings in Pinecone)
    /// </summary>
    Task<bool> IsDocumentProcessedAsync(int documentId, string userId, int? tenantId);

    /// <summary>
    /// Deletes all embeddings for a document from Pinecone
    /// </summary>
    Task DeleteDocumentEmbeddingsAsync(int documentId, string userId, int? tenantId);
}

public class RagService : IRagService
{
    private readonly ApplicationDbContext _context;
    private readonly IDocumentProcessor _documentProcessor;
    private readonly IDatabaseSyncService _dbSync;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly HttpClient _httpClient;
    private readonly ILogger<RagService> _logger;
    private readonly IConfiguration _configuration;
    private const int ChunkSize = 1500; // Characters per chunk
    private const int ChunkOverlap = 200; // Overlap between chunks
    private const string EmbeddingModel = "text-embedding-3-small";
    private const int EmbeddingDimension = 1536; // Dimension for text-embedding-3-small

    public RagService(
        ApplicationDbContext context,
        IDocumentProcessor documentProcessor,
        IDatabaseSyncService dbSync,
        IDataProtectionProvider dataProtectionProvider,
        HttpClient httpClient,
        ILogger<RagService> logger,
        IConfiguration configuration)
    {
        _context = context;
        _documentProcessor = documentProcessor;
        _dbSync = dbSync;
        _dataProtectionProvider = dataProtectionProvider;
        _httpClient = httpClient;
        _logger = logger;
        _configuration = configuration;
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

    private PineconeClient GetPineconeClient()
    {
        var apiKey = _configuration["Pinecone:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("Pinecone API key is not configured. Please add 'Pinecone:ApiKey' to appsettings.json");
        }

        return new PineconeClient(apiKey);
    }

    private string GetIndexName(string userId, int tenantId)
    {
        // Create index name with tenant and user to isolate data
        var indexName = _configuration["Pinecone:IndexName"] ?? "blazor-bedrock";
        return $"{indexName}-tenant{tenantId}";
    }

    private async Task EnsureIndexExistsAsync(PineconeClient pinecone, string indexName)
    {
        try
        {
            // Try to use the index - if it doesn't exist, create it
            try
            {
                var testIndex = pinecone.Index(indexName);
                // If we get here, index exists (we can't test without querying, so just continue)
            }
            catch
            {
                // Index doesn't exist, create it
                _logger.LogInformation("Creating Pinecone index: {IndexName}", indexName);
                
                // Create serverless index
                await pinecone.CreateIndexAsync(new CreateIndexRequest
                {
                    Name = indexName,
                    Dimension = EmbeddingDimension,
                    Metric = MetricType.Cosine,
                    Spec = new ServerlessIndexSpec
                    {
                        Serverless = new ServerlessSpec
                        {
                            Cloud = ServerlessSpecCloud.Aws, // Change to Azure or GCP if needed
                            Region = _configuration["Pinecone:Region"] ?? "us-east-1"
                        }
                    }
                });

                // Wait for index to be ready
                await Task.Delay(5000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring index exists: {IndexName}", indexName);
            // If index creation fails but index might exist, continue
            _logger.LogWarning("Continuing despite error - index may already exist");
        }
    }

    private List<string> ChunkText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        var chunks = new List<string>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var currentChunk = new StringBuilder();

        foreach (var line in lines)
        {
            if (currentChunk.Length + line.Length > ChunkSize && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString());
                // Start new chunk with overlap
                var chunkText = currentChunk.ToString();
                var overlapText = chunkText.Length > ChunkOverlap 
                    ? chunkText.Substring(chunkText.Length - ChunkOverlap)
                    : chunkText;
                currentChunk = new StringBuilder(overlapText);
            }
            currentChunk.AppendLine(line);
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString());
        }

        return chunks.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
    }

    private async Task<float[]> CreateEmbeddingAsync(string text, string apiKey)
    {
        var embeddings = await CreateEmbeddingsBatchAsync(new[] { text }, apiKey);
        return embeddings[0];
    }

    private async Task<List<float[]>> CreateEmbeddingsBatchAsync(IList<string> texts, string apiKey)
    {
        const int maxRetries = 5;
        int retryCount = 0;
        
        while (retryCount < maxRetries)
        {
            try
            {
                var requestBody = new
                {
                    model = EmbeddingModel,
                    input = texts
                };

                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                
                // Handle rate limiting with exponential backoff
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    retryCount++;
                    var delay = (int)Math.Pow(2, retryCount) * 1000; // Exponential backoff: 2s, 4s, 8s, 16s, 32s
                    
                    // Check for Retry-After header
                    if (response.Headers.TryGetValues("Retry-After", out var retryAfterValues))
                    {
                        if (int.TryParse(retryAfterValues.FirstOrDefault(), out var retryAfterSeconds))
                        {
                            delay = retryAfterSeconds * 1000;
                        }
                    }
                    
                    _logger.LogWarning("Rate limited (429). Retrying after {Delay}ms (attempt {Attempt}/{MaxRetries})", delay, retryCount, maxRetries);
                    await Task.Delay(delay);
                    continue;
                }
                
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                var embeddings = new List<float[]>();
                
                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var embeddingElement in data.EnumerateArray())
                    {
                        if (embeddingElement.TryGetProperty("embedding", out var embedding))
                        {
                            var embeddingList = new List<float>();
                            foreach (var value in embedding.EnumerateArray())
                            {
                                embeddingList.Add((float)value.GetDouble());
                            }
                            embeddings.Add(embeddingList.ToArray());
                        }
                    }
                }

                if (embeddings.Count != texts.Count)
                {
                    throw new InvalidOperationException($"Expected {texts.Count} embeddings but got {embeddings.Count}");
                }

                return embeddings;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("429"))
            {
                retryCount++;
                if (retryCount >= maxRetries)
                {
                    _logger.LogError(ex, "Failed to create embeddings batch after {MaxRetries} retries due to rate limiting", maxRetries);
                    throw;
                }
                var delay = (int)Math.Pow(2, retryCount) * 1000;
                _logger.LogWarning("Rate limited. Retrying after {Delay}ms (attempt {Attempt}/{MaxRetries})", delay, retryCount, maxRetries);
                await Task.Delay(delay);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating embeddings batch");
                throw;
            }
        }
        
        throw new InvalidOperationException("Failed to create embeddings batch after maximum retries");
    }

    public async Task ProcessDocumentAsync(int documentId, string userId, int? tenantId)
    {
        if (tenantId == null)
        {
            throw new InvalidOperationException("Tenant ID is required.");
        }

        await _dbSync.ExecuteAsync(async () =>
        {
            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId && d.TenantId == tenantId.Value);

            if (document == null)
            {
                throw new FileNotFoundException("Document not found");
            }

            // Get text content
            string text;
            using (var stream = new MemoryStream(document.FileContent))
            {
                text = await _documentProcessor.ExtractTextAsync(stream, document.ContentType);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("Document text extraction returned empty content");
            }

            // Chunk the text
            var chunks = ChunkText(text);
            _logger.LogInformation("Document {DocumentId} split into {ChunkCount} chunks", documentId, chunks.Count);

            // Get API keys
            var openAiApiKey = await GetApiKeyAsync(userId, tenantId);
            var pinecone = GetPineconeClient();
            var indexName = GetIndexName(userId, tenantId.Value);

            // Ensure index exists
            await EnsureIndexExistsAsync(pinecone, indexName);
            var index = pinecone.Index(indexName);

            // Create embeddings in batches (OpenAI supports up to 2048 items per batch)
            var vectors = new List<Vector>();
            const int embeddingBatchSize = 50; // Reduced batch size to avoid rate limits
            const int delayBetweenBatches = 200; // Small delay between batches to avoid rate limits
            
            for (int batchStart = 0; batchStart < chunks.Count; batchStart += embeddingBatchSize)
            {
                var batchEnd = Math.Min(batchStart + embeddingBatchSize, chunks.Count);
                var chunkBatch = chunks.Skip(batchStart).Take(batchEnd - batchStart).ToList();
                
                _logger.LogInformation("Creating embeddings for batch {BatchNumber} ({Start}-{End} of {Total})", 
                    (batchStart / embeddingBatchSize) + 1, batchStart + 1, batchEnd, chunks.Count);
                
                // Create embeddings in batch
                var embeddings = await CreateEmbeddingsBatchAsync(chunkBatch, openAiApiKey);
                
                // Create vectors from embeddings
                for (int i = 0; i < chunkBatch.Count; i++)
                {
                    var chunkIndex = batchStart + i;
                    var chunk = chunkBatch[i];
                    var embedding = embeddings[i];

                    var vectorId = $"doc{documentId}_chunk{chunkIndex}";
                    var metadata = new Metadata
                    {
                        ["document_id"] = documentId,
                        ["chunk_index"] = chunkIndex,
                        ["text"] = chunk,
                        ["user_id"] = userId,
                        ["tenant_id"] = tenantId.Value,
                        ["filename"] = document.FileName
                    };

                    vectors.Add(new Vector
                    {
                        Id = vectorId,
                        Values = embedding,
                        Metadata = metadata
                    });
                }
                
                // Small delay between batches to avoid rate limits (except for the last batch)
                if (batchEnd < chunks.Count)
                {
                    await Task.Delay(delayBetweenBatches);
                }
            }

            // Upsert vectors to Pinecone in batches
            const int batchSize = 100;
            for (int i = 0; i < vectors.Count; i += batchSize)
            {
                var batch = vectors.Skip(i).Take(batchSize).ToList();
                await index.UpsertAsync(new UpsertRequest { Vectors = batch });
                _logger.LogInformation("Upserted batch {BatchNumber} of {TotalBatches} for document {DocumentId}", 
                    (i / batchSize) + 1, (vectors.Count + batchSize - 1) / batchSize, documentId);
            }

            _logger.LogInformation("Successfully processed document {DocumentId} with {ChunkCount} chunks", documentId, chunks.Count);
        });
    }

    public async Task<string> AskQuestionAsync(string question, int documentId, string userId, int? tenantId, int topK = 5)
    {
        if (tenantId == null)
        {
            throw new InvalidOperationException("Tenant ID is required.");
        }

        try
        {
            // Get API keys
            var openAiApiKey = await GetApiKeyAsync(userId, tenantId);
            var pinecone = GetPineconeClient();
            var indexName = GetIndexName(userId, tenantId.Value);

            // Create embedding for the question
            var questionEmbedding = await CreateEmbeddingAsync(question, openAiApiKey);

            // Query Pinecone for similar chunks
            var index = pinecone.Index(indexName);
            var queryResponse = await index.QueryAsync(new QueryRequest
            {
                Vector = questionEmbedding,
                TopK = (uint)topK,
                Filter = new Metadata
                {
                    ["document_id"] = documentId,
                    ["user_id"] = userId,
                    ["tenant_id"] = tenantId.Value
                },
                IncludeMetadata = true
            });

            // Extract relevant chunks
            var relevantChunks = new List<string>();
            if (queryResponse.Matches != null)
            {
                foreach (var match in queryResponse.Matches)
                {
                    if (match.Metadata != null && match.Metadata.TryGetValue("text", out var textObj))
                    {
                        relevantChunks.Add(textObj.ToString() ?? string.Empty);
                    }
                }
            }

            if (!relevantChunks.Any())
            {
                return "I couldn't find any relevant information in the document to answer your question.";
            }

            // Build context from chunks
            var context = string.Join("\n\n", relevantChunks);

            // Get document info
            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId && d.TenantId == tenantId.Value);

            var documentName = document?.FileName ?? "the document";

            // Create prompt with context
            var prompt = $@"You are a helpful assistant that answers questions based on the provided document context.

Document: {documentName}

Context from the document:
{context}

Question: {question}

Please answer the question based on the context provided above. If the context doesn't contain enough information to answer the question, say so. Be concise and accurate.";

            // Call GPT-4o with the prompt
            var requestBody = new
            {
                model = "gpt-4o",
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                max_tokens = 2000,
                temperature = 0.7
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", openAiApiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content))
                {
                    return content.GetString() ?? "No response generated.";
                }
            }

            throw new InvalidOperationException("Failed to extract response from OpenAI API");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error asking question with RAG");
            throw;
        }
    }

    public async Task<bool> IsDocumentProcessedAsync(int documentId, string userId, int? tenantId)
    {
        if (tenantId == null)
        {
            return false;
        }

        try
        {
            var pinecone = GetPineconeClient();
            var indexName = GetIndexName(userId, tenantId.Value);

            var index = pinecone.Index(indexName);
            
            // Query with filter to check if any vectors exist for this document
            var dummyVector = new float[EmbeddingDimension]; // Zero vector for query
            var queryResponse = await index.QueryAsync(new QueryRequest
            {
                Vector = dummyVector,
                TopK = 1,
                Filter = new Metadata
                {
                    ["document_id"] = documentId,
                    ["user_id"] = userId,
                    ["tenant_id"] = tenantId.Value
                },
                IncludeMetadata = false
            });

            return queryResponse.Matches != null && queryResponse.Matches.Any();
        }
        catch
        {
            return false;
        }
    }

    public async Task DeleteDocumentEmbeddingsAsync(int documentId, string userId, int? tenantId)
    {
        if (tenantId == null)
        {
            throw new InvalidOperationException("Tenant ID is required.");
        }

        try
        {
            var pinecone = GetPineconeClient();
            var indexName = GetIndexName(userId, tenantId.Value);

            var index = pinecone.Index(indexName);

            // Query to get all vector IDs for this document
            var dummyVector = new float[EmbeddingDimension];
            var queryResponse = await index.QueryAsync(new QueryRequest
            {
                Vector = dummyVector,
                TopK = 10000, // Large number to get all
                Filter = new Metadata
                {
                    ["document_id"] = documentId,
                    ["user_id"] = userId,
                    ["tenant_id"] = tenantId.Value
                },
                IncludeMetadata = false
            });

            if (queryResponse.Matches != null && queryResponse.Matches.Any())
            {
                var vectorIds = queryResponse.Matches.Select(m => m.Id).ToList();
                await index.DeleteAsync(new DeleteRequest { Ids = vectorIds });
                _logger.LogInformation("Deleted {Count} embeddings for document {DocumentId}", vectorIds.Count, documentId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting document embeddings");
            throw;
        }
    }
}
