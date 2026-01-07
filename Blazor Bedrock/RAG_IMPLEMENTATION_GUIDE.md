# RAG (Retrieval-Augmented Generation) Implementation Guide

This guide documents the complete implementation of RAG functionality using Pinecone.io for vector storage and OpenAI embeddings. This system allows users to create "Knowledge Bases" (RAG groups) that index documents and enable intelligent document search within ChatGPT conversations.

## Table of Contents

1. [Overview](#overview)
2. [Database Schema](#database-schema)
3. [Dependencies](#dependencies)
4. [Implementation Steps](#implementation-steps)
5. [Configuration](#configuration)
6. [Usage](#usage)

---

## Overview

The RAG system consists of:

- **Knowledge Bases (RAG Groups)**: Collections of documents that can be indexed to Pinecone
- **Document Indexing**: Converts documents to vectors using OpenAI embeddings and stores them in Pinecone
- **Query Integration**: Retrieves relevant document chunks when querying ChatGPT with a Knowledge Base selected
- **Top-K Configuration**: Configurable number of relevant chunks to retrieve per query

### Architecture Flow

1. **Indexing Phase**:
   - Documents are chunked into smaller pieces (1000 chars with 200 char overlap)
   - Each chunk is converted to a vector using OpenAI's `text-embedding-3-small` model (1536 dimensions)
   - Vectors are stored in Pinecone with metadata (document ID, name, chunk index, text)

2. **Query Phase**:
   - User question is converted to a vector
   - Pinecone finds top-K most similar document chunks
   - Chunks are sent to ChatGPT as context
   - ChatGPT uses context to provide accurate answers

---

## Database Schema

### SQL Scripts for Database Setup

```sql
-- Table: RagGroups
CREATE TABLE `RagGroups` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `Name` VARCHAR(200) NOT NULL,
    `Description` VARCHAR(1000) NULL,
    `UserId` VARCHAR(255) NOT NULL,
    `TenantId` INT NOT NULL,
    `TopK` INT NOT NULL DEFAULT 5,
    `CreatedAt` DATETIME(6) NOT NULL,
    `UpdatedAt` DATETIME(6) NULL,
    `PineconeIndexName` VARCHAR(200) NULL,
    PRIMARY KEY (`Id`),
    INDEX `IX_RagGroups_TenantId_UserId` (`TenantId`, `UserId`),
    INDEX `IX_RagGroups_PineconeIndexName` (`PineconeIndexName`),
    CONSTRAINT `FK_RagGroups_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_RagGroups_Tenants_TenantId` FOREIGN KEY (`TenantId`) REFERENCES `Tenants` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Table: RagGroupDocuments
CREATE TABLE `RagGroupDocuments` (
    `Id` INT NOT NULL AUTO_INCREMENT,
    `RagGroupId` INT NOT NULL,
    `DocumentId` INT NOT NULL,
    `AddedAt` DATETIME(6) NOT NULL,
    `IsIndexed` TINYINT(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`),
    UNIQUE INDEX `IX_RagGroupDocuments_RagGroupId_DocumentId` (`RagGroupId`, `DocumentId`),
    CONSTRAINT `FK_RagGroupDocuments_RagGroups_RagGroupId` FOREIGN KEY (`RagGroupId`) REFERENCES `RagGroups` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_RagGroupDocuments_Documents_DocumentId` FOREIGN KEY (`DocumentId`) REFERENCES `Documents` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Feature Flag for RAG
INSERT INTO `FeatureFlags` (`Name`, `Description`, `IsEnabled`, `CreatedAt`, `UpdatedAt`)
VALUES ('RAG_Enabled', 'Enable Knowledge Base (RAG) with Pinecone integration', 0, NOW(), NULL)
ON DUPLICATE KEY UPDATE `Description` = 'Enable Knowledge Base (RAG) with Pinecone integration';
```

### Entity Framework Models

#### RagGroup.cs
```csharp
namespace Blazor_Bedrock.Data.Models;

public class RagGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int TenantId { get; set; }
    public int TopK { get; set; } = 5; // Default top-K value for retrieval
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? PineconeIndexName { get; set; } // Store the Pinecone index name for this group

    // Navigation properties
    public virtual ApplicationUser User { get; set; } = null!;
    public virtual Tenant Tenant { get; set; } = null!;
    public virtual ICollection<RagGroupDocument> RagGroupDocuments { get; set; } = new List<RagGroupDocument>();
}
```

#### RagGroupDocument.cs
```csharp
namespace Blazor_Bedrock.Data.Models;

public class RagGroupDocument
{
    public int Id { get; set; }
    public int RagGroupId { get; set; }
    public int DocumentId { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public bool IsIndexed { get; set; } = false; // Track if document is indexed in Pinecone

    // Navigation properties
    public virtual RagGroup RagGroup { get; set; } = null!;
    public virtual Document Document { get; set; } = null!;
}
```

### Entity Framework Configurations

#### RagGroupConfiguration.cs
```csharp
using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class RagGroupConfiguration : IEntityTypeConfiguration<RagGroup>
{
    public void Configure(EntityTypeBuilder<RagGroup> builder)
    {
        builder.HasKey(rg => rg.Id);
        builder.Property(rg => rg.Name).IsRequired().HasMaxLength(200);
        builder.Property(rg => rg.Description).HasMaxLength(1000);
        builder.Property(rg => rg.TopK).IsRequired().HasDefaultValue(5);
        builder.Property(rg => rg.PineconeIndexName).HasMaxLength(200);
        
        builder.HasOne(rg => rg.User)
            .WithMany()
            .HasForeignKey(rg => rg.UserId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasOne(rg => rg.Tenant)
            .WithMany()
            .HasForeignKey(rg => rg.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasIndex(rg => new { rg.TenantId, rg.UserId });
        builder.HasIndex(rg => rg.PineconeIndexName);
    }
}
```

#### RagGroupDocumentConfiguration.cs
```csharp
using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class RagGroupDocumentConfiguration : IEntityTypeConfiguration<RagGroupDocument>
{
    public void Configure(EntityTypeBuilder<RagGroupDocument> builder)
    {
        builder.HasKey(rgd => rgd.Id);
        
        builder.HasOne(rgd => rgd.RagGroup)
            .WithMany(rg => rg.RagGroupDocuments)
            .HasForeignKey(rgd => rgd.RagGroupId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasOne(rgd => rgd.Document)
            .WithMany()
            .HasForeignKey(rgd => rgd.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
            
        // Ensure a document can only be added once to a group
        builder.HasIndex(rgd => new { rgd.RagGroupId, rgd.DocumentId }).IsUnique();
    }
}
```

### Update ApplicationDbContext.cs

Add to `ApplicationDbContext`:

```csharp
public DbSet<RagGroup> RagGroups { get; set; }
public DbSet<RagGroupDocument> RagGroupDocuments { get; set; }
```

In `OnModelCreating`:

```csharp
builder.ApplyConfiguration(new RagGroupConfiguration());
builder.ApplyConfiguration(new RagGroupDocumentConfiguration());
```

---

## Dependencies

### NuGet Packages

```xml
<PackageReference Include="Pinecone.Client" Version="4.0.2" />
```

The system also requires:
- OpenAI API access (for embeddings) - uses existing ChatGPT integration
- Pinecone.io account and API key

---

## Implementation Steps

### Step 1: Add Database Models and Configurations

1. Create `Data/Models/RagGroup.cs`
2. Create `Data/Models/RagGroupDocument.cs`
3. Create `Data/Configurations/RagGroupConfiguration.cs`
4. Create `Data/Configurations/RagGroupDocumentConfiguration.cs`
5. Update `ApplicationDbContext.cs` to include new DbSets and configurations

### Step 2: Create RAG Service

Create `Services/Rag/RagService.cs` with the following key methods:

**Interface (IRagService)**:
- `GetRagGroupsAsync(string userId, int tenantId)` - Get all RAG groups for user/tenant
- `GetRagGroupByIdAsync(int ragGroupId, string userId, int tenantId)` - Get specific group
- `CreateRagGroupAsync(...)` - Create new knowledge base
- `UpdateRagGroupAsync(...)` - Update group properties
- `DeleteRagGroupAsync(...)` - Delete group and Pinecone index
- `AddDocumentsToRagGroupAsync(...)` - Add documents to group
- `RemoveDocumentFromRagGroupAsync(...)` - Remove document from group
- `IndexRagGroupAsync(...)` - Index documents to Pinecone
- `QueryRagGroupAsync(...)` - Query Pinecone for relevant chunks
- `GetOpenAiApiKeyAsync(...)` - Get OpenAI API key for embeddings

**Key Implementation Details**:

1. **Chunking Logic**:
   - Chunk size: 1000 characters
   - Overlap: 200 characters
   - Splits on sentence boundaries for better chunking

2. **Embedding Generation**:
   - Uses OpenAI's `text-embedding-3-small` model
   - 1536 dimensions
   - Calls OpenAI embeddings API: `POST https://api.openai.com/v1/embeddings`

3. **Pinecone Integration**:
   - Creates serverless indexes (AWS by default)
   - Index dimension: 1536
   - Metric: Cosine similarity
   - Batch uploads (100 vectors per batch)

4. **Vector Metadata**:
   - `documentId`: Original document ID
   - `documentName`: File name
   - `chunkIndex`: Chunk position in document
   - `text`: Original chunk text

### Step 3: Register Services

In `Program.cs`:

```csharp
using Blazor_Bedrock.Services.Rag;

// Add to service registration
builder.Services.AddScoped<IRagService, RagService>();
builder.Services.AddHttpClient(); // For RAG service HTTP calls
```

### Step 4: Create Knowledge Base UI Page

Create `Components/Pages/Documents/KnowledgeBase.razor`:

**Key Features**:
- List all knowledge bases
- Create/Edit knowledge base (name, description, top-K)
- Manage documents (add/remove from group)
- Index to Pinecone with progress tracking
- Delete knowledge base

**Key UI Components**:
- Modal for create/edit
- Modal for managing documents (with search, select all/deselect all)
- Progress modal for indexing
- Two progress bars (overall + embedding progress)

### Step 5: Add Menu Integration

Update `Services/Navigation/MenuService.cs`:

```csharp
// In GetMenuItemsAsync, within Documents menu section:
var ragEnabled = await _featureFlagService.IsEnabledAsync("RAG_Enabled");
if (ragEnabled)
{
    documentsMenu.Children.Add(new MenuItem
    {
        Title = "Knowledge Base",
        Href = "/documents/knowledge-base",
        Icon = "bi bi-book"
    });
}
```

### Step 6: Add Feature Configuration

Update `Components/Pages/MasterAdmin/Features.razor`:

Add Pinecone configuration panel with:
- API Key (encrypted)
- Region (e.g., us-east-1)

Save configuration using `ApiConfigurationService` with service name "Pinecone".

### Step 7: Integrate with ChatGPT Chat

Update `Components/Pages/ChatGpt/Chat.razor`:

**Add RAG Group Dropdown**:
```razor
@if (_ragEnabled)
{
    <div class="d-flex align-items-center mt-2">
        <select class="form-select form-select-sm" value="@_selectedRagGroupId" @onchange="OnRagGroupChanged">
            <option value="">No Knowledge Base</option>
            @foreach (var ragGroup in _availableRagGroups)
            {
                <option value="@ragGroup.Id">@ragGroup.Name</option>
            }
        </select>
    </div>
}
```

**Query RAG Before Sending Message**:
```csharp
// Query RAG group if selected
string? ragContext = null;
if (_selectedRagGroupId.HasValue && _ragEnabled && !string.IsNullOrWhiteSpace(_messageText))
{
    var ragResults = await RagService.QueryRagGroupAsync(_selectedRagGroupId.Value, _messageText, userId, tenantId.Value);
    if (ragResults.Any())
    {
        ragContext = string.Join("\n\n", ragResults);
    }
}

// Store original message for display
var originalMessageText = _messageText;

// Create message text for ChatGPT (with RAG context if available)
var userMessageTextForChat = originalMessageText;
if (!string.IsNullOrEmpty(ragContext))
{
    userMessageTextForChat = $"Context from Knowledge Base:\n{ragContext}\n\nUser Question: {originalMessageText}";
}

// Save original message to database (for display)
var userMessage = new ChatGptMessage
{
    ConversationId = _currentConversationId.Value,
    Role = "user",
    Content = originalMessageText  // Save without RAG context for display
};

// Send to ChatGPT with RAG context
conversationMessages.Add(new ChatMessage
{
    Role = "user",
    Content = userMessageTextForChat  // Include RAG context for ChatGPT
});
```

### Step 8: Add Feature Flag

In `Data/DatabaseSeeder.cs`:

```csharp
new FeatureFlag { 
    Name = "RAG_Enabled", 
    Description = "Enable Knowledge Base (RAG) with Pinecone integration", 
    IsEnabled = false 
}
```

---

## Configuration

### 1. Enable RAG Feature

Navigate to `/masteradmin/features` and enable "Knowledge Base (RAG)".

### 2. Configure Pinecone

In the Features page, under "Knowledge Base (RAG)" panel:
- **API Key**: Your Pinecone API key from https://app.pinecone.io
- **Region**: Your Pinecone region (e.g., `us-east-1`)

### 3. Configure OpenAI (for Embeddings)

Ensure ChatGPT API key is configured in Features page (used for generating embeddings).

---

## Usage

### Creating a Knowledge Base

1. Navigate to `/documents/knowledge-base`
2. Click "Create Knowledge Base"
3. Enter:
   - **Name**: Descriptive name for the knowledge base
   - **Description**: Optional description
   - **Top-K**: Number of relevant chunks to retrieve (1-100, default: 5)
4. Click "Save"

### Adding Documents

1. Click "Manage Documents" (file icon) on a knowledge base
2. Use search to filter documents if needed
3. Check documents to add, or use "Select All" for visible documents
4. Documents are added immediately

### Indexing to Pinecone

1. Ensure documents are added to the knowledge base
2. Click "Index to Pinecone" (cloud upload icon)
3. Monitor progress:
   - Overall progress bar (all documents)
   - Embedding progress bar (current document)
   - Status messages showing current operation
4. Wait for completion

### Using in ChatGPT

1. Navigate to `/chatgpt/chat`
2. Select a Knowledge Base from the dropdown (if RAG is enabled)
3. Ask questions - ChatGPT will use relevant document chunks from the knowledge base
4. Your question displays normally; RAG context is sent behind the scenes

---

## Key Implementation Files

### Models
- `Data/Models/RagGroup.cs`
- `Data/Models/RagGroupDocument.cs`

### Configurations
- `Data/Configurations/RagGroupConfiguration.cs`
- `Data/Configurations/RagGroupDocumentConfiguration.cs`

### Services
- `Services/Rag/RagService.cs` - Core RAG logic
- `Services/Rag/IRagService.cs` - Service interface

### UI Components
- `Components/Pages/Documents/KnowledgeBase.razor` - Knowledge base management page
- `Components/Pages/ChatGpt/Chat.razor` - ChatGPT integration (modified)
- `Components/Pages/MasterAdmin/Features.razor` - Pinecone configuration (modified)

### Navigation
- `Services/Navigation/MenuService.cs` - Menu integration (modified)

### Database
- `Data/ApplicationDbContext.cs` - DbContext updates (modified)
- `Data/DatabaseSeeder.cs` - Feature flag seeding (modified)

---

## Technical Details

### Chunking Strategy

- **Chunk Size**: 1000 characters
- **Overlap**: 200 characters
- **Method**: Sentence-based splitting for better semantic boundaries
- **Chunk ID Format**: `{documentId}-chunk-{index}`

### Embedding Model

- **Model**: `text-embedding-3-small`
- **Dimensions**: 1536
- **API Endpoint**: `POST https://api.openai.com/v1/embeddings`
- **Input Format**: Plain text chunk

### Pinecone Configuration

- **Index Type**: Serverless
- **Cloud Provider**: AWS (default)
- **Metric**: Cosine similarity
- **Dimension**: 1536
- **Batch Size**: 100 vectors per upload

### Query Process

1. User question → OpenAI embedding API
2. Query Pinecone with embedding vector
3. Retrieve top-K matches (based on group's TopK setting)
4. Extract text from metadata
5. Combine chunks and send to ChatGPT as context

---

## Error Handling

### Common Issues

1. **"OpenAI API key not configured"**
   - Ensure ChatGPT API key is set in Features page
   - Required for generating embeddings

2. **"Pinecone API key not configured"**
   - Configure Pinecone API key and region in Features page

3. **"RAG group is not indexed yet"**
   - Click "Index to Pinecone" to index documents first

4. **Indexing fails**
   - Check Pinecone API key validity
   - Verify region is correct
   - Ensure OpenAI API key is valid
   - Check document text extraction is working

---

## Security Considerations

1. **API Keys**: Stored encrypted using `ApiConfigurationService` with data protection
2. **Tenant Isolation**: All RAG groups are tenant-scoped
3. **User Isolation**: Users can only access their own knowledge bases
4. **Document Access**: Only documents owned by user/tenant can be added

---

## Performance Considerations

1. **Batch Processing**: Vectors uploaded in batches of 100
2. **Progress Tracking**: Real-time progress updates during indexing
3. **Caching**: Consider caching frequently queried knowledge bases
4. **Index Management**: Old indexes should be cleaned up when groups are deleted

---

## Future Enhancements

Potential improvements:
- Support for multiple embedding models
- Custom chunking strategies
- Index re-indexing/updating
- Document versioning
- Query result caching
- Analytics on knowledge base usage

---

## Notes

- The system requires both OpenAI and Pinecone API keys
- Indexing can take time for large documents (progress is shown)
- Top-K value affects query quality vs. token usage
- Documents must have extracted text before indexing
- Pinecone indexes are created automatically on first indexing

---

## Support

For issues or questions:
1. Check error messages in the UI
2. Review logs for detailed error information
3. Verify API keys are correctly configured
4. Ensure feature flag is enabled
