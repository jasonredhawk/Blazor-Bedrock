using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Services;
using Microsoft.EntityFrameworkCore;

namespace Blazor_Bedrock.Services.ChatGpt;

public interface IQuestionService
{
    Task<List<ChatGptQuestionGroup>> GetAllGroupsAsync(int? tenantId);
    Task<ChatGptQuestionGroup?> GetGroupByIdAsync(int id, int? tenantId);
    Task<ChatGptQuestionGroup> CreateGroupAsync(ChatGptQuestionGroup group, int? tenantId);
    Task<bool> UpdateGroupAsync(int id, ChatGptQuestionGroup group, int? tenantId);
    Task<bool> DeleteGroupAsync(int id, int? tenantId);
    Task<bool> ReorderGroupsAsync(List<int> groupIds, int? tenantId);
    
    Task<List<ChatGptQuestion>> GetAllQuestionsAsync(int? tenantId, int? groupId = null);
    Task<ChatGptQuestion?> GetQuestionByIdAsync(int id, int? tenantId);
    Task<ChatGptQuestion> CreateQuestionAsync(ChatGptQuestion question, int? tenantId);
    Task<bool> UpdateQuestionAsync(int id, ChatGptQuestion question, int? tenantId);
    Task<bool> DeleteQuestionAsync(int id, int? tenantId);
    Task<bool> ReorderQuestionsAsync(List<int> questionIds, int? tenantId);
    
    Task<List<ChatGptQuestionResponse>> GetResponsesAsync(int? tenantId, int? questionId = null, int? documentId = null);
    Task<ChatGptQuestionResponse> SaveResponseAsync(ChatGptQuestionResponse response);
    Task<bool> DeleteResponseAsync(int responseId, int? tenantId);
}

public class QuestionService : IQuestionService
{
    private readonly ApplicationDbContext _context;
    private readonly IDatabaseSyncService _dbSync;

    public QuestionService(ApplicationDbContext context, IDatabaseSyncService dbSync)
    {
        _context = context;
        _dbSync = dbSync;
    }

    public async Task<List<ChatGptQuestionGroup>> GetAllGroupsAsync(int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.ChatGptQuestionGroups
                .Where(g => g.TenantId == null || g.TenantId == tenantId)
                .OrderBy(g => g.Order)
                .ThenBy(g => g.Name)
                .ToListAsync();
        });
    }

    public async Task<ChatGptQuestionGroup?> GetGroupByIdAsync(int id, int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.ChatGptQuestionGroups
                .FirstOrDefaultAsync(g => g.Id == id && (g.TenantId == null || g.TenantId == tenantId));
        });
    }

    public async Task<ChatGptQuestionGroup> CreateGroupAsync(ChatGptQuestionGroup group, int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            group.TenantId = tenantId;
            group.CreatedAt = DateTime.UtcNow;
            group.UpdatedAt = DateTime.UtcNow;
            
            // Set order to end if not specified
            if (group.Order == 0)
            {
                var maxOrder = await _context.ChatGptQuestionGroups
                    .Where(g => g.TenantId == tenantId)
                    .Select(g => (int?)g.Order)
                    .DefaultIfEmpty()
                    .MaxAsync();
                group.Order = (maxOrder ?? 0) + 1;
            }
            
            _context.ChatGptQuestionGroups.Add(group);
            await _context.SaveChangesAsync();
            return group;
        });
    }

    public async Task<bool> UpdateGroupAsync(int id, ChatGptQuestionGroup group, int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var existing = await _context.ChatGptQuestionGroups
                .FirstOrDefaultAsync(g => g.Id == id && (g.TenantId == null || g.TenantId == tenantId));
            
            if (existing == null)
                return false;

            existing.Name = group.Name;
            existing.Description = group.Description;
            existing.Order = group.Order;
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        });
    }

    public async Task<bool> DeleteGroupAsync(int id, int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var group = await _context.ChatGptQuestionGroups
                .Include(g => g.Questions)
                .FirstOrDefaultAsync(g => g.Id == id && (tenantId == null || g.TenantId == tenantId));
            
            if (group == null)
                return false;

            // Questions will be deleted automatically due to cascade delete
            _context.ChatGptQuestionGroups.Remove(group);
            await _context.SaveChangesAsync();
            return true;
        });
    }

    public async Task<bool> ReorderGroupsAsync(List<int> groupIds, int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var groups = await _context.ChatGptQuestionGroups
                .Where(g => g.TenantId == tenantId && groupIds.Contains(g.Id))
                .ToListAsync();
            
            for (int i = 0; i < groupIds.Count; i++)
            {
                var group = groups.FirstOrDefault(g => g.Id == groupIds[i]);
                if (group != null)
                {
                    group.Order = i + 1;
                    group.UpdatedAt = DateTime.UtcNow;
                }
            }
            
            await _context.SaveChangesAsync();
            return true;
        });
    }

    public async Task<List<ChatGptQuestion>> GetAllQuestionsAsync(int? tenantId, int? groupId = null)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var query = _context.ChatGptQuestions
                .Include(q => q.Group)
                .Where(q => q.TenantId == null || q.TenantId == tenantId);
            
            if (groupId.HasValue)
            {
                query = query.Where(q => q.GroupId == groupId.Value);
            }
            
            return await query
                .OrderBy(q => q.Group != null ? q.Group.Order : 0)
                .ThenBy(q => q.Order)
                .ThenBy(q => q.QuestionText)
                .ToListAsync();
        });
    }

    public async Task<ChatGptQuestion?> GetQuestionByIdAsync(int id, int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.ChatGptQuestions
                .FirstOrDefaultAsync(q => q.Id == id && (q.TenantId == null || q.TenantId == tenantId));
        });
    }

    public async Task<ChatGptQuestion> CreateQuestionAsync(ChatGptQuestion question, int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            question.TenantId = tenantId;
            question.CreatedAt = DateTime.UtcNow;
            question.UpdatedAt = DateTime.UtcNow;
            
            // Set order to end if not specified
            if (question.Order == 0)
            {
                var maxOrder = await _context.ChatGptQuestions
                    .Where(q => q.TenantId == tenantId && q.GroupId == question.GroupId)
                    .Select(q => (int?)q.Order)
                    .DefaultIfEmpty()
                    .MaxAsync();
                question.Order = (maxOrder ?? 0) + 1;
            }
            
            _context.ChatGptQuestions.Add(question);
            await _context.SaveChangesAsync();
            return question;
        });
    }

    public async Task<bool> UpdateQuestionAsync(int id, ChatGptQuestion question, int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var existing = await _context.ChatGptQuestions
                .FirstOrDefaultAsync(q => q.Id == id && (q.TenantId == null || q.TenantId == tenantId));
            
            if (existing == null)
                return false;

            existing.QuestionText = question.QuestionText;
            existing.Description = question.Description;
            existing.GroupId = question.GroupId;
            existing.Order = question.Order;
            existing.IsActive = question.IsActive;
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        });
    }

    public async Task<bool> DeleteQuestionAsync(int id, int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var question = await _context.ChatGptQuestions
                .FirstOrDefaultAsync(q => q.Id == id && (tenantId == null || q.TenantId == tenantId));
            
            if (question == null)
                return false;

            _context.ChatGptQuestions.Remove(question);
            await _context.SaveChangesAsync();
            return true;
        });
    }

    public async Task<bool> ReorderQuestionsAsync(List<int> questionIds, int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var questions = await _context.ChatGptQuestions
                .Where(q => q.TenantId == tenantId && questionIds.Contains(q.Id))
                .ToListAsync();
            
            for (int i = 0; i < questionIds.Count; i++)
            {
                var question = questions.FirstOrDefault(q => q.Id == questionIds[i]);
                if (question != null)
                {
                    question.Order = i + 1;
                    question.UpdatedAt = DateTime.UtcNow;
                }
            }
            
            await _context.SaveChangesAsync();
            return true;
        });
    }

    public async Task<List<ChatGptQuestionResponse>> GetResponsesAsync(int? tenantId, int? questionId = null, int? documentId = null)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var query = _context.ChatGptQuestionResponses
                .Include(r => r.Question)
                .Include(r => r.Document)
                .Include(r => r.Prompt)
                .Include(r => r.Conversation)
                .AsQueryable();
            
            if (tenantId.HasValue)
            {
                // Handle manual questions (QuestionId = null) and regular questions
                query = query.Where(r => 
                    r.QuestionId == null || // Manual questions
                    r.Question == null || // Safety check
                    r.Question.TenantId == tenantId || 
                    r.Question.TenantId == null);
            }
            
            if (questionId.HasValue)
            {
                query = query.Where(r => r.QuestionId == questionId.Value);
            }
            
            if (documentId.HasValue)
            {
                query = query.Where(r => r.DocumentId == documentId.Value);
            }
            
            return await query
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
        });
    }

    public async Task<ChatGptQuestionResponse> SaveResponseAsync(ChatGptQuestionResponse response)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            response.CreatedAt = DateTime.UtcNow;
            _context.ChatGptQuestionResponses.Add(response);
            await _context.SaveChangesAsync();
            return response;
        });
    }

    public async Task<bool> DeleteResponseAsync(int responseId, int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var response = await _context.ChatGptQuestionResponses
                .Include(r => r.Question)
                .FirstOrDefaultAsync(r => r.Id == responseId);
            
            if (response == null)
                return false;
            
            // Verify tenant access
            if (tenantId.HasValue && response.Question != null && 
                response.Question.TenantId.HasValue && response.Question.TenantId != tenantId)
            {
                return false;
            }
            
            _context.ChatGptQuestionResponses.Remove(response);
            await _context.SaveChangesAsync();
            return true;
        });
    }
}
