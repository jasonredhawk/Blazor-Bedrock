using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Infrastructure.ExternalApis;
using Blazor_Bedrock.Services;
using Microsoft.EntityFrameworkCore;
using DocumentModel = Blazor_Bedrock.Data.Models.Document;
using SheetData = Blazor_Bedrock.Infrastructure.ExternalApis.SheetData;

namespace Blazor_Bedrock.Services.Document;

public interface IDocumentService
{
    Task<DocumentModel> UploadDocumentAsync(Stream fileStream, string filename, string contentType, string userId, int tenantId, string? title = null, string? author = null);
    Task<List<DocumentModel>> GetAllDocumentsAsync(string userId, int tenantId);
    Task<DocumentModel?> GetDocumentByIdAsync(int id, string userId, int tenantId);
    Task<bool> DeleteDocumentAsync(int id, string userId, int tenantId);
    Task UpdateDocumentMetadataAsync(int documentId, string userId, int tenantId, string? title, string? author);
    Task<byte[]> GetDocumentFileAsync(int id, string userId, int tenantId);
    Task<(byte[] FileContent, string ContentType, string FileName)> GetDocumentFileWithMetadataAsync(int id, string userId, int tenantId);
    Task<List<SheetData>> GetStructuredDataAsync(int id, string userId, int tenantId, int maxRows = 100);
}

public class DocumentService : IDocumentService
{
    private readonly ApplicationDbContext _context;
    private readonly IDocumentProcessor _documentProcessor;
    private readonly IDatabaseSyncService _dbSync;

    public DocumentService(
        ApplicationDbContext context,
        IDocumentProcessor documentProcessor,
        IDatabaseSyncService dbSync)
    {
        _context = context;
        _documentProcessor = documentProcessor;
        _dbSync = dbSync;
    }

    public async Task<DocumentModel> UploadDocumentAsync(Stream fileStream, string filename, string contentType, string userId, int tenantId, string? title = null, string? author = null)
    {
        // Read file content into byte array
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream);
        var fileContent = memoryStream.ToArray();
        var fileSize = fileContent.Length;

        // Extract text from document
        string? extractedText = null;
        try
        {
            // Reset stream position for text extraction
            memoryStream.Position = 0;
            extractedText = await _documentProcessor.ExtractTextAsync(memoryStream, contentType);
        }
        catch (Exception ex)
        {
            // Log error but continue - we'll still save the document
            Console.WriteLine($"Error extracting text: {ex.Message}");
        }

        return await _dbSync.ExecuteAsync(async () =>
        {
            // Create document entity
            var document = new DocumentModel
            {
                UserId = userId,
                TenantId = tenantId,
                FileName = filename,
                Title = title,
                Author = author,
                ContentType = contentType,
                FileSize = fileSize,
                FileContent = fileContent,
                ExtractedText = extractedText,
                UploadedAt = DateTime.UtcNow
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            return document;
        });
    }

    public async Task<List<DocumentModel>> GetAllDocumentsAsync(string userId, int tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.Documents
                .Where(d => d.UserId == userId && d.TenantId == tenantId)
                .OrderByDescending(d => d.UploadedAt)
                .ToListAsync();
        });
    }

    public async Task<DocumentModel?> GetDocumentByIdAsync(int id, string userId, int tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.Documents
                .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId && d.TenantId == tenantId);
        });
    }

    public async Task<bool> DeleteDocumentAsync(int id, string userId, int tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId && d.TenantId == tenantId);
            
            if (document == null)
                return false;

            _context.Documents.Remove(document);
            await _context.SaveChangesAsync();
            return true;
        });
    }

    public async Task UpdateDocumentMetadataAsync(int documentId, string userId, int tenantId, string? title, string? author)
    {
        await _dbSync.ExecuteAsync(async () =>
        {
            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId && d.TenantId == tenantId);
            
            if (document == null)
                return;

            if (!string.IsNullOrWhiteSpace(title))
                document.Title = title;
            if (!string.IsNullOrWhiteSpace(author))
                document.Author = author;

            await _context.SaveChangesAsync();
        });
    }

    public async Task<byte[]> GetDocumentFileAsync(int id, string userId, int tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId && d.TenantId == tenantId);
            
            if (document == null)
                throw new FileNotFoundException("Document not found");

            return document.FileContent;
        });
    }

    public async Task<(byte[] FileContent, string ContentType, string FileName)> GetDocumentFileWithMetadataAsync(int id, string userId, int tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId && d.TenantId == tenantId);
            
            if (document == null)
                throw new FileNotFoundException("Document not found");

            return (document.FileContent, document.ContentType, document.FileName);
        });
    }

    public async Task<List<SheetData>> GetStructuredDataAsync(int id, string userId, int tenantId, int maxRows = 0)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId && d.TenantId == tenantId);
            
            if (document == null)
                throw new FileNotFoundException("Document not found");

            using var stream = new MemoryStream(document.FileContent);
            
            var contentType = document.ContentType?.ToLower() ?? "";
            if (contentType.Contains("spreadsheet") || contentType.Contains("excel") || 
                document.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                document.FileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
            {
                // maxRows = 0 means get all rows
                return await _documentProcessor.ExtractStructuredDataFromExcelAsync(stream, maxRows);
            }
            else if (contentType.Contains("csv") || document.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return await _documentProcessor.ExtractStructuredDataFromCsvAsync(stream, maxRows);
            }
            
            throw new NotSupportedException("Structured data extraction is only supported for Excel and CSV files");
        });
    }
}
