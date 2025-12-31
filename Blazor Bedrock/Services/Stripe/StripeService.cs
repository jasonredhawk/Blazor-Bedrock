using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;

namespace Blazor_Bedrock.Services.Stripe;

public interface IStripeService
{
    Task<List<StripeSubscription>> GetTenantSubscriptionsAsync(int tenantId);
    Task<StripeSubscription?> GetSubscriptionAsync(int subscriptionId);
    Task<string> CreateCheckoutSessionAsync(int tenantId, string stripePriceId, int subscriptionPlanId, string successUrl, string cancelUrl);
}

public class StripeService : IStripeService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;

    public StripeService(ApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
        
        var secretKey = _configuration["Stripe:SecretKey"];
        if (!string.IsNullOrEmpty(secretKey))
        {
            StripeConfiguration.ApiKey = secretKey;
        }
    }

    public async Task<List<StripeSubscription>> GetTenantSubscriptionsAsync(int tenantId)
    {
        return await _context.StripeSubscriptions
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<StripeSubscription?> GetSubscriptionAsync(int subscriptionId)
    {
        return await _context.StripeSubscriptions.FindAsync(subscriptionId);
    }

    public async Task<string> CreateCheckoutSessionAsync(int tenantId, string stripePriceId, int subscriptionPlanId, string successUrl, string cancelUrl)
    {
        var options = new SessionCreateOptions
        {
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    Price = stripePriceId,
                    Quantity = 1,
                },
            },
            Mode = "subscription",
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            Metadata = new Dictionary<string, string>
            {
                { "tenantId", tenantId.ToString() },
                { "subscriptionPlanId", subscriptionPlanId.ToString() }
            }
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options);
        return session.Url;
    }
}

