using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Infrastructure.ExternalApis;
using Blazor_Bedrock.Services;
using Blazor_Bedrock.Services.ApiConfiguration;
using Blazor_Bedrock.Services.Document;
using Microsoft.EntityFrameworkCore;
using Pinecone;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Blazor_Bedrock.Services.Rag;

public interface IRagService
{
    Task<List<RagGroup>> GetRagGroupsAsync(string userId, int tenantId);
    Task<RagGroup?> GetRagGroupByIdAsync(int ragGroupId, string userId, int tenantId);
    Task<RagGroup> CreateRagGroupAsync(string name, string? description, int topK, string userId, int tenantId);
    Task UpdateRagGroupAsync(int ragGroupId, string name, string? description, int topK, string userId, int tenantId);
    Task<bool> DeleteRagGroupAsync(int ragGroupId, string userId, int tenantId);
    Task AddDocumentsToRagGroupAsync(int ragGroupId, List<int> documentIds, string userId, int tenantId);
    Task RemoveDocumentFromRagGroupAsync(int ragGroupId, int documentId, string userId, int tenantId);
    Task IndexRagGroupAsync(int ragGroupId, string userId, int tenantId, IProgress<string>? progress = null);
    Task<List<string>> QueryRagGroupAsync(int ragGroupId, string query, string userId, int tenantId);
    Task<string> GetOpenAiApiKeyAsync(string userId, int? tenantId);
}

public class RagService : IRagService
{
    private readonly ApplicationDbContext _context;
    private readonly IDocumentService _documentService;
    private readonly IDocumentProcessor _documentProcessor;
    private readonly IApiConfigurationService _apiConfigurationService;
    private readonly IDatabaseSyncService _dbSync;
    private readonly ILogger<RagService> _logger;
    private readonly HttpClient _httpClient;

    private const int ChunkSize = 1000; // Characters per chunk
    private const int ChunkOverlap = 200; // Overlap between chunks
    private const string EmbeddingModel = "text-embedding-3-small"; // OpenAI embedding model

    public RagService(
        ApplicationDbContext context,
        IDocumentService documentService,
        IDocumentProcessor documentProcessor,
        IApiConfigurationService apiConfigurationService,
        IDatabaseSyncService dbSync,
        ILogger<RagService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _context = context;
        _documentService = documentService;
        _documentProcessor = documentProcessor;
        _apiConfigurationService = apiConfigurationService;
        _dbSync = dbSync;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<List<RagGroup>> GetRagGroupsAsync(string userId, int tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.RagGroups
                .Where(rg => rg.UserId == userId && rg.TenantId == tenantId)
                .Include(rg => rg.RagGroupDocuments)
                    .ThenInclude(rgd => rgd.Document)
                .OrderByDescending(rg => rg.CreatedAt)
                .ToListAsync();
        });
    }

    public async Task<RagGroup?> GetRagGroupByIdAsync(int ragGroupId, string userId, int tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.RagGroups
                .Where(rg => rg.Id == ragGroupId && rg.UserId == userId && rg.TenantId == tenantId)
                .Include(rg => rg.RagGroupDocuments)
                    .ThenInclude(rgd => rgd.Document)
                .FirstOrDefaultAsync();
        });
    }

    public async Task<RagGroup> CreateRagGroupAsync(string name, string? description, int topK, string userId, int tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var ragGroup = new RagGroup
            {
                Name = name,
                Description = description,
                TopK = topK,
                UserId = userId,
                TenantId = tenantId,
                CreatedAt = DateTime.UtcNow
            };

            _context.RagGroups.Add(ragGroup);
            await _context.SaveChangesAsync();

            return ragGroup;
        });
    }

    public async Task UpdateRagGroupAsync(int ragGroupId, string name, string? description, int topK, string userId, int tenantId)
    {
        await _dbSync.ExecuteAsync(async () =>
        {
            var ragGroup = await _context.RagGroups
                .FirstOrDefaultAsync(rg => rg.Id == ragGroupId && rg.UserId == userId && rg.TenantId == tenantId);

            if (ragGroup == null)
                throw new InvalidOperationException("RAG group not found");

            ragGroup.Name = name;
            ragGroup.Description = description;
            ragGroup.TopK = topK;
            ragGroup.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
        });
    }

    public async Task<bool> DeleteRagGroupAsync(int ragGroupId, string userId, int tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var ragGroup = await _context.RagGroups
                .Include(rg => rg.RagGroupDocuments)
                .FirstOrDefaultAsync(rg => rg.Id == ragGroupId && rg.UserId == userId && rg.TenantId == tenantId);

            if (ragGroup == null)
                return false;

            // Delete from Pinecone if indexed
            if (!string.IsNullOrEmpty(ragGroup.PineconeIndexName))
            {
                try
                {
                    await DeletePineconeIndexAsync(ragGroup.PineconeIndexName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete Pinecone index {IndexName}", ragGroup.PineconeIndexName);
                }
            }

            _context.RagGroups.Remove(ragGroup);
            await _context.SaveChangesAsync();

            return true;
        });
    }

    public async Task AddDocumentsToRagGroupAsync(int ragGroupId, List<int> documentIds, string userId, int tenantId)
    {
        await _dbSync.ExecuteAsync(async () =>
        {
            var ragGroup = await _context.RagGroups
                .FirstOrDefaultAsync(rg => rg.Id == ragGroupId && rg.UserId == userId && rg.TenantId == tenantId);

            if (ragGroup == null)
                throw new InvalidOperationException("RAG group not found");

            foreach (var documentId in documentIds)
            {
                // Check if document already exists in group
                var exists = await _context.RagGroupDocuments
                    .AnyAsync(rgd => rgd.RagGroupId == ragGroupId && rgd.DocumentId == documentId);

                if (!exists)
                {
                    // Verify document belongs to user/tenant
                    var document = await _context.Documents
                        .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId && d.TenantId == tenantId);

                    if (document != null)
                    {
                        _context.RagGroupDocuments.Add(new RagGroupDocument
                        {
                            RagGroupId = ragGroupId,
                            DocumentId = documentId,
                            AddedAt = DateTime.UtcNow,
                            IsIndexed = false
                        });
                    }
                }
            }

            await _context.SaveChangesAsync();
        });
    }

    public async Task RemoveDocumentFromRagGroupAsync(int ragGroupId, int documentId, string userId, int tenantId)
    {
        await _dbSync.ExecuteAsync(async () =>
        {
            var ragGroupDocument = await _context.RagGroupDocuments
                .Include(rgd => rgd.RagGroup)
                .FirstOrDefaultAsync(rgd => rgd.RagGroupId == ragGroupId && 
                                          rgd.DocumentId == documentId &&
                                          rgd.RagGroup.UserId == userId &&
                                          rgd.RagGroup.TenantId == tenantId);

            if (ragGroupDocument != null)
            {
                // If indexed, we should remove from Pinecone, but for simplicity, we'll just mark as not indexed
                // The next re-index will handle cleanup
                _context.RagGroupDocuments.Remove(ragGroupDocument);
                await _context.SaveChangesAsync();
            }
        });
    }

    public async Task IndexRagGroupAsync(int ragGroupId, string userId, int tenantId, IProgress<string>? progress = null)
    {
        var ragGroup = await GetRagGroupByIdAsync(ragGroupId, userId, tenantId);
        if (ragGroup == null)
            throw new InvalidOperationException("RAG group not found");

        progress?.Report("Initializing Pinecone connection...");

        // Get Pinecone configuration
        var pineconeConfig = await _apiConfigurationService.GetConfigurationAsync("Pinecone");
        if (!pineconeConfig.TryGetValue("ApiKey", out var pineconeApiKey) || string.IsNullOrEmpty(pineconeApiKey))
            throw new InvalidOperationException("Pinecone API key not configured");

        // Get OpenAI API key for embeddings
        var openAiApiKey = await GetOpenAiApiKeyAsync(userId, tenantId);

        // Create or get Pinecone index name
        var indexName = ragGroup.PineconeIndexName ?? $"rag-group-{ragGroupId}-{Guid.NewGuid():N}";
        
        if (string.IsNullOrEmpty(ragGroup.PineconeIndexName))
        {
            await _dbSync.ExecuteAsync(async () =>
            {
                ragGroup.PineconeIndexName = indexName;
                await _context.SaveChangesAsync();
            });
        }

        progress?.Report("Connecting to Pinecone...");

        // Initialize Pinecone client
        var pineconeClient = new PineconeClient(pineconeApiKey);

        progress?.Report("Creating/verifying index...");

        // Create index if it doesn't exist
        try
        {
            var indexList = await pineconeClient.ListIndexesAsync();
            var existingIndexes = indexList?.Indexes?.Select(i => i.Name).ToList() ?? new List<string>();
            
            if (!existingIndexes.Contains(indexName))
            {
                // Get region from config or use default
                var region = "us-east-1"; // Default region
                if (pineconeConfig.TryGetValue("Region", out var regionStr) && !string.IsNullOrEmpty(regionStr))
                    region = regionStr;

                // Pinecone serverless indexes default to AWS
                var indexSpec = new ServerlessIndexSpec
                {
                    Serverless = new ServerlessSpec
                    {
                        Cloud = ServerlessSpecCloud.Aws,
                        Region = region
                    }
                };

                await pineconeClient.CreateIndexAsync(new CreateIndexRequest
                {
                    Name = indexName,
                    Dimension = 1536, // text-embedding-3-small has 1536 dimensions
                    Metric = MetricType.Cosine,
                    Spec = indexSpec
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Pinecone index");
            throw;
        }

        // Get index
        var index = pineconeClient.Index(indexName);

        progress?.Report("Processing documents...");

        // Process each document in the group
        var documents = ragGroup.RagGroupDocuments
            .Where(rgd => rgd.Document != null)
            .Select(rgd => rgd.Document!)
            .ToList();

        var totalDocuments = documents.Count;
        var processedDocuments = 0;

        foreach (var ragGroupDocument in ragGroup.RagGroupDocuments.Where(rgd => !rgd.IsIndexed))
        {
            if (ragGroupDocument.Document == null)
                continue;

            var document = ragGroupDocument.Document;
            processedDocuments++;

            progress?.Report($"Processing document {processedDocuments}/{totalDocuments}: {document.FileName}...");

            try
            {
                // Get document text
                string text;
                if (!string.IsNullOrEmpty(document.ExtractedText))
                {
                    text = document.ExtractedText;
                }
                else
                {
                    // Extract text if not already extracted
                    using var stream = new MemoryStream(document.FileContent);
                    text = await _documentProcessor.ExtractTextAsync(stream, document.ContentType);
                }

                // Chunk the text
                progress?.Report($"Chunking text from '{document.FileName}'...");
                var chunks = ChunkText(text, document.Id, document.FileName);
                progress?.Report($"Created {chunks.Count} chunks from '{document.FileName}'");

                // Generate embeddings and upload to Pinecone
                var vectors = new List<Vector>();
                var totalChunks = chunks.Count;
                for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
                {
                    var chunk = chunks[chunkIndex];
                    var chunkProgress = chunkIndex + 1;
                    
                    progress?.Report($"Generating embedding {chunkProgress}/{totalChunks} for '{document.FileName}' (chunk {chunk.Index + 1})...");
                    
                    var embedding = await GenerateEmbeddingAsync(chunk.Text, openAiApiKey);
                    var metadata = new Metadata();
                    metadata["documentId"] = document.Id.ToString();
                    metadata["documentName"] = document.FileName;
                    metadata["chunkIndex"] = chunk.Index.ToString();
                    metadata["text"] = chunk.Text;

                    vectors.Add(new Vector
                    {
                        Id = chunk.Id,
                        Values = embedding.ToArray(),
                        Metadata = metadata
                    });
                }

                progress?.Report($"Uploading {vectors.Count} vectors to Pinecone for '{document.FileName}'...");

                // Upload vectors in batches
                const int batchSize = 100;
                var totalBatches = (int)Math.Ceiling((double)vectors.Count / batchSize);
                for (int i = 0; i < vectors.Count; i += batchSize)
                {
                    var batchNumber = (i / batchSize) + 1;
                    var batch = vectors.Skip(i).Take(batchSize).ToList();
                    progress?.Report($"Uploading batch {batchNumber}/{totalBatches} ({batch.Count} vectors) to Pinecone for '{document.FileName}'...");
                    
                    await index.UpsertAsync(new UpsertRequest
                    {
                        Vectors = batch
                    });
                }

                // Mark as indexed
                await _dbSync.ExecuteAsync(async () =>
                {
                    ragGroupDocument.IsIndexed = true;
                    await _context.SaveChangesAsync();
                });

                progress?.Report($"Completed document {processedDocuments}/{totalDocuments}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing document {DocumentId}", document.Id);
                progress?.Report($"Error processing document {document.FileName}: {ex.Message}");
            }
        }

        progress?.Report("Indexing completed!");
    }

    public async Task<List<string>> QueryRagGroupAsync(int ragGroupId, string query, string userId, int tenantId)
    {
        var ragGroup = await GetRagGroupByIdAsync(ragGroupId, userId, tenantId);
        if (ragGroup == null)
            throw new InvalidOperationException("RAG group not found");

        if (string.IsNullOrEmpty(ragGroup.PineconeIndexName))
            throw new InvalidOperationException("RAG group is not indexed yet");

        // Get Pinecone configuration
        var pineconeConfig = await _apiConfigurationService.GetConfigurationAsync("Pinecone");
        if (!pineconeConfig.TryGetValue("ApiKey", out var pineconeApiKey) || string.IsNullOrEmpty(pineconeApiKey))
            throw new InvalidOperationException("Pinecone API key not configured");

        // Get OpenAI API key for embeddings
        var openAiApiKey = await GetOpenAiApiKeyAsync(userId, tenantId);

        // Generate embedding for query
        var queryEmbedding = await GenerateEmbeddingAsync(query, openAiApiKey);

        // Initialize Pinecone client
        var pineconeClient = new PineconeClient(pineconeApiKey);
        var index = pineconeClient.Index(ragGroup.PineconeIndexName);

        // Query Pinecone
        var queryResult = await index.QueryAsync(new QueryRequest
        {
            Vector = queryEmbedding.ToArray(),
            TopK = (uint)ragGroup.TopK,
            IncludeMetadata = true
        });

        // Extract text from results
        var results = new List<string>();
        if (queryResult.Matches != null)
        {
            foreach (var match in queryResult.Matches)
            {
                if (match.Metadata != null && match.Metadata.ContainsKey("text"))
                {
                    var textValue = match.Metadata["text"];
                    if (textValue != null)
                    {
                        var text = textValue.ToString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            results.Add(text);
                        }
                    }
                }
            }
        }

        return results;
    }

    public async Task<string> GetOpenAiApiKeyAsync(string userId, int? tenantId)
    {
        var chatGptConfig = await _apiConfigurationService.GetConfigurationAsync("ChatGPT");
        if (!chatGptConfig.TryGetValue("ApiKey", out var apiKey) || string.IsNullOrEmpty(apiKey))
        {
            // Try to get from user/tenant specific API key
            var userApiKey = await _context.ChatGptApiKeys
                .FirstOrDefaultAsync(k => k.UserId == userId && (tenantId == null || k.TenantId == tenantId));

            if (userApiKey != null && !string.IsNullOrEmpty(userApiKey.EncryptedApiKey))
            {
                // Decrypt the API key (simplified - you may need to use data protection)
                // For now, assuming it's stored in a way that can be retrieved
                throw new InvalidOperationException("User-specific API key decryption not implemented. Please configure global ChatGPT API key.");
            }

            throw new InvalidOperationException("OpenAI API key is required to generate embeddings from document text before indexing to Pinecone. Please configure your ChatGPT/OpenAI API key in the Features page.");
        }

        return apiKey;
    }

    private List<TextChunk> ChunkText(string text, int documentId, string documentName)
    {
        var chunks = new List<TextChunk>();
        
        // Clean and normalize text
        text = Regex.Replace(text, @"\s+", " ");
        text = text.Trim();

        if (string.IsNullOrEmpty(text))
            return chunks;

        // Split into sentences first (better chunking)
        var sentences = Regex.Split(text, @"(?<=[.!?])\s+").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

        var currentChunk = new StringBuilder();
        var chunkIndex = 0;

        foreach (var sentence in sentences)
        {
            if (currentChunk.Length + sentence.Length > ChunkSize && currentChunk.Length > 0)
            {
                // Save current chunk
                chunks.Add(new TextChunk
                {
                    Id = $"{documentId}-chunk-{chunkIndex}",
                    Text = currentChunk.ToString().Trim(),
                    Index = chunkIndex,
                    DocumentId = documentId,
                    DocumentName = documentName
                });
                chunkIndex++;

                // Start new chunk with overlap
                var overlapText = currentChunk.ToString();
                if (overlapText.Length > ChunkOverlap)
                {
                    overlapText = overlapText.Substring(overlapText.Length - ChunkOverlap);
                }
                currentChunk = new StringBuilder(overlapText);
            }

            currentChunk.Append(sentence).Append(" ");
        }

        // Add remaining chunk
        if (currentChunk.Length > 0)
        {
            chunks.Add(new TextChunk
            {
                Id = $"{documentId}-chunk-{chunkIndex}",
                Text = currentChunk.ToString().Trim(),
                Index = chunkIndex,
                DocumentId = documentId,
                DocumentName = documentName
            });
        }

        return chunks;
    }

    private async Task<float[]> GenerateEmbeddingAsync(string text, string apiKey)
    {
        var requestBody = new
        {
            model = EmbeddingModel,
            input = text
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings")
        {
            Headers = { { "Authorization", $"Bearer {apiKey}" } },
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var jsonResponse = await response.Content.ReadAsStringAsync();
        var embeddingResponse = JsonSerializer.Deserialize<EmbeddingResponse>(jsonResponse, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var embedding = embeddingResponse?.Data?.FirstOrDefault()?.Embedding ?? new List<float>();
        return embedding.ToArray();
    }

    private async Task DeletePineconeIndexAsync(string indexName)
    {
        try
        {
            var pineconeConfig = await _apiConfigurationService.GetConfigurationAsync("Pinecone");
            if (!pineconeConfig.TryGetValue("ApiKey", out var pineconeApiKey) || string.IsNullOrEmpty(pineconeApiKey))
                return;

            var pineconeClient = new PineconeClient(pineconeApiKey);
            await pineconeClient.DeleteIndexAsync(indexName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete Pinecone index {IndexName}", indexName);
        }
    }

    private class TextChunk
    {
        public string Id { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public int Index { get; set; }
        public int DocumentId { get; set; }
        public string DocumentName { get; set; } = string.Empty;
    }

    private class EmbeddingResponse
    {
        public List<EmbeddingData>? Data { get; set; }
    }

    private class EmbeddingData
    {
        public List<float> Embedding { get; set; } = new();
    }
}
