using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Infrastructure.ExternalApis;
using Microsoft.EntityFrameworkCore;
using DocumentModel = Blazor_Bedrock.Data.Models.Document;

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
}

public class DocumentService : IDocumentService
{
    private readonly ApplicationDbContext _context;
    private readonly IDocumentProcessor _documentProcessor;

    public DocumentService(
        ApplicationDbContext context,
        IDocumentProcessor documentProcessor)
    {
        _context = context;
        _documentProcessor = documentProcessor;
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
    }

    public async Task<List<DocumentModel>> GetAllDocumentsAsync(string userId, int tenantId)
    {
        return await _context.Documents
            .Where(d => d.UserId == userId && d.TenantId == tenantId)
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync();
    }

    public async Task<DocumentModel?> GetDocumentByIdAsync(int id, string userId, int tenantId)
    {
        return await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId && d.TenantId == tenantId);
    }

    public async Task<bool> DeleteDocumentAsync(int id, string userId, int tenantId)
    {
        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId && d.TenantId == tenantId);
        
        if (document == null)
            return false;

        _context.Documents.Remove(document);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task UpdateDocumentMetadataAsync(int documentId, string userId, int tenantId, string? title, string? author)
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
    }

    public async Task<byte[]> GetDocumentFileAsync(int id, string userId, int tenantId)
    {
        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId && d.TenantId == tenantId);
        
        if (document == null)
            throw new FileNotFoundException("Document not found");

        return document.FileContent;
    }

    public async Task<(byte[] FileContent, string ContentType, string FileName)> GetDocumentFileWithMetadataAsync(int id, string userId, int tenantId)
    {
        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId && d.TenantId == tenantId);
        
        if (document == null)
            throw new FileNotFoundException("Document not found");

        return (document.FileContent, document.ContentType, document.FileName);
    }
}
