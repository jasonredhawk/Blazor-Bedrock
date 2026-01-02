# Pinecone Setup Guide for Blazor Bedrock

## Overview

Your Blazor Bedrock application already has Pinecone integration for RAG (Retrieval Augmented Generation) functionality. This guide will help you get started with Pinecone.

## What is Pinecone?

Pinecone is a vector database that stores embeddings (vector representations) of your documents. When you upload a document, it's:
1. Split into chunks
2. Converted to embeddings using OpenAI's `text-embedding-3-small` model
3. Stored in Pinecone for fast similarity search

When users ask questions, the system:
1. Converts the question to an embedding
2. Searches Pinecone for similar document chunks
3. Uses those chunks as context to answer the question with GPT-4o

## Quick Start

### 1. Create a Pinecone Account

1. Go to [https://www.pinecone.io/](https://www.pinecone.io/)
2. Sign up for a free account (free tier includes 1 index)
3. Create a new project

### 2. Get Your API Key

1. In the Pinecone dashboard, go to your project
2. Navigate to "API Keys" section
3. Copy your API key

### 3. Configure Your Application

Update `appsettings.json` with your Pinecone credentials:

```json
{
  "Pinecone": {
    "ApiKey": "your-api-key-here",
    "IndexName": "blazor-bedrock",
    "Region": "us-east-1"
  }
}
```

**Configuration Options:**
- `ApiKey`: Your Pinecone API key (required)
- `IndexName`: Base name for indexes (default: "blazor-bedrock")
  - Actual indexes are created per tenant: `{IndexName}-tenant{tenantId}`
- `Region`: AWS region for serverless indexes (default: "us-east-1")
  - Options: `us-east-1`, `us-west-2`, `eu-west-1`, `ap-southeast-1`, etc.

### 4. Configure OpenAI API Key

The RAG service also requires an OpenAI API key for:
- Creating embeddings (`text-embedding-3-small`)
- Generating answers (`gpt-4o`)

1. Log into your Blazor Bedrock application
2. Go to Profile → Settings
3. Add your OpenAI API key
4. Make sure it's active for your current organization/tenant

## How It Works

### Document Processing

When you process a document:
1. The document text is extracted
2. Text is split into chunks (1500 characters with 200 character overlap)
3. Each chunk is converted to a 1536-dimensional embedding
4. Embeddings are stored in Pinecone with metadata:
   - `document_id`: The document ID
   - `chunk_index`: Position in the document
   - `text`: The actual chunk text
   - `user_id`: Owner of the document
   - `tenant_id`: Organization/tenant ID
   - `filename`: Original filename

### Asking Questions

When a user asks a question:
1. The question is converted to an embedding
2. Pinecone searches for the top 5 most similar chunks (configurable)
3. Relevant chunks are used as context
4. GPT-4o generates an answer based on the context

### Index Management

- **Automatic Creation**: Indexes are created automatically when first needed
- **Per-Tenant Isolation**: Each tenant gets its own index (`blazor-bedrock-tenant1`, `blazor-bedrock-tenant2`, etc.)
- **Serverless**: Uses Pinecone's serverless indexes (no infrastructure to manage)

## API Endpoints

Your application already has RAG endpoints configured:

### Process Document
```
POST /api/rag/process-document/{documentId}
```
Processes a document and stores embeddings in Pinecone.

### Ask Question
```
POST /api/rag/ask
Body: {
  "question": "Your question here",
  "documentId": 123,
  "topK": 5  // Optional, default: 5
}
```

### Check if Document is Processed
```
GET /api/rag/document/{documentId}/processed
```
Returns whether a document has been processed.

## Usage Example

1. **Upload a Document**: Upload a PDF, DOCX, or text file through the Documents page
2. **Process the Document**: Click "Process for RAG" (or use the API endpoint)
3. **Ask Questions**: Use the chat interface or API to ask questions about the document

## Troubleshooting

### "Pinecone API key is not configured"
- Make sure you've added the API key to `appsettings.json` under `Pinecone:ApiKey`

### "No API key found for the current organization"
- Configure your OpenAI API key in Profile → Settings for your current tenant

### Index Creation Fails
- Check your Pinecone account limits (free tier: 1 index)
- Verify your API key has proper permissions
- Check the region setting matches your Pinecone project region

### No Results Found
- Make sure the document has been processed first
- Verify the document ID matches
- Check that you're using the correct tenant/organization

## Cost Considerations

### Pinecone
- **Free Tier**: 1 index, 100K vectors, 1M queries/month
- **Paid Plans**: Start at $70/month for more capacity

### OpenAI
- **Embeddings**: `text-embedding-3-small` costs ~$0.02 per 1M tokens
- **Chat**: `gpt-4o` costs vary by usage

## Best Practices

1. **Process Documents Once**: Documents are processed and stored, so you only need to process them once
2. **Delete When Needed**: Use the delete endpoint to remove embeddings when documents are deleted
3. **Monitor Usage**: Keep an eye on your Pinecone and OpenAI usage
4. **Chunk Size**: The default 1500 characters works well for most documents, but you can adjust in `RagService.cs`

## Next Steps

1. ✅ Configure your Pinecone API key
2. ✅ Configure your OpenAI API key in the app
3. ✅ Upload and process a test document
4. ✅ Try asking questions about the document
5. ✅ Explore the RAG service code in `Services/RAG/RagService.cs`

## Additional Resources

- [Pinecone Documentation](https://docs.pinecone.io/)
- [Pinecone .NET SDK](https://github.com/pinecone-io/pinecone-dotnet-client)
- [OpenAI Embeddings Guide](https://platform.openai.com/docs/guides/embeddings)
