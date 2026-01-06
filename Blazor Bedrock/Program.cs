using Blazor_Bedrock.Components;
using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Services;
using Blazor_Bedrock.Services.Tenant;
using Blazor_Bedrock.Services.FeatureFlag;
using Blazor_Bedrock.Services.Auth;
using Blazor_Bedrock.Services.Navigation;
using Blazor_Bedrock.Services.Logger;
using Blazor_Bedrock.Services.UserManagement;
using Blazor_Bedrock.Services.RoleManagement;
using Blazor_Bedrock.Services.ChatGpt;
using Blazor_Bedrock.Services.Document;
using Blazor_Bedrock.Services.Chart;
using Blazor_Bedrock.Services.Migrations;
using Blazor_Bedrock.Services.Stripe;
using Blazor_Bedrock.Infrastructure.ExternalApis;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Blazor_Bedrock.Services.Notification;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Database Configuration
// Build connection string from Database configuration section, or fall back to ConnectionStrings:DefaultConnection
string connectionString;
var databaseConfig = builder.Configuration.GetSection("Database");
if (databaseConfig.Exists() && !string.IsNullOrEmpty(databaseConfig["DatabaseName"]))
{
    // Build connection string from Database configuration section
    var server = databaseConfig["Server"] ?? "localhost";
    var port = databaseConfig["Port"] ?? "3306";
    var databaseName = databaseConfig["DatabaseName"] ?? "BlazorBedrock";
    var user = databaseConfig["User"] ?? "root";
    var password = databaseConfig["Password"] ?? "";
    
    // Handle cloud SQL format (Unix socket path)
    if (server.StartsWith("/cloudsql/"))
    {
        connectionString = $"Server={server};Database={databaseName};Uid={user};Pwd={password};";
    }
    else
    {
        connectionString = $"Server={server};Database={databaseName};User={user};Password={password};Port={port};AllowUserVariables=True;AllowLoadLocalInfile=True;";
    }
}
else
{
    // Fall back to ConnectionStrings:DefaultConnection if Database section is not configured
    connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrEmpty(connectionString))
    {
        throw new InvalidOperationException("Database configuration not found. Please configure either the 'Database' section or 'ConnectionStrings:DefaultConnection' in appsettings.json");
    }
}

// Use a fixed ServerVersion instead of AutoDetect to avoid connection issues during startup
// AutoDetect requires a valid connection which can fail if credentials are incorrect
// Common MySQL versions: 8.0.0, 8.0.33, 8.0.36, 5.7.44
// Adjust this version to match your MySQL server version
// You can check your MySQL version with: SELECT VERSION();
var serverVersion = ServerVersion.Parse("8.0.36-mysql"); // Change this to match your MySQL version

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, serverVersion));

// Configure Authentication first (before Identity) to allow external providers
var authBuilder = builder.Services.AddAuthentication();

// External Authentication - Google
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
    });
}

// External Authentication - Facebook
var facebookAppId = builder.Configuration["Authentication:Facebook:AppId"];
var facebookAppSecret = builder.Configuration["Authentication:Facebook:AppSecret"];
if (!string.IsNullOrEmpty(facebookAppId) && !string.IsNullOrEmpty(facebookAppSecret))
{
    authBuilder.AddFacebook(options =>
    {
        options.AppId = facebookAppId;
        options.AppSecret = facebookAppSecret;
    });
}

// Identity Configuration (this will merge with existing authentication configuration)
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    // Password settings
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;
    
    // User settings
    options.User.RequireUniqueEmail = true;
    
    // Lockout settings
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
    
    // Sign-in settings
    options.SignIn.RequireConfirmedEmail = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Authentication Configuration
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/auth/login";
    options.LogoutPath = "/auth/logout";
    options.AccessDeniedPath = "/auth/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    // Cookie settings for proper authentication
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax; // Lax works better with redirects
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // Allow HTTP in development
    options.Cookie.Name = ".AspNetCore.Identity.Application";
});

// Notification Service
builder.Services.AddScoped<INotificationService, NotificationService>();

// Memory Cache
builder.Services.AddMemoryCache();

// Session Support
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// HTTP Context Accessor for tenant resolution
builder.Services.AddHttpContextAccessor();

// Database synchronization service (singleton to share semaphore across all requests)
builder.Services.AddSingleton<IDatabaseSyncService, DatabaseSyncService>();

// Application Services
builder.Services.AddScoped<IFeatureFlagService, FeatureFlagService>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<IIdentityService, IdentityService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IMenuService, MenuService>();
builder.Services.AddScoped<ISafeNavigationService, SafeNavigationService>();
builder.Services.AddScoped<Blazor_Bedrock.Services.ApiConfiguration.IApiConfigurationService, Blazor_Bedrock.Services.ApiConfiguration.ApiConfigurationService>();
builder.Services.AddSingleton<IApplicationLoggerService, ApplicationLoggerService>();

// Add custom logger provider to capture all logs
// Create with a temporary service provider, will be updated after app is built
var tempServiceProvider = builder.Services.BuildServiceProvider();
var loggerProvider = new ApplicationLoggerProvider(tempServiceProvider);
builder.Logging.AddProvider(loggerProvider);
tempServiceProvider.Dispose();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IDocumentProcessor, DocumentProcessor>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IChartService, ChartService>();
builder.Services.AddScoped<IMigrationService, MigrationService>();
builder.Services.AddScoped<IStripeService, StripeService>();
builder.Services.AddScoped<Blazor_Bedrock.Services.Subscription.ISubscriptionPlanService, Blazor_Bedrock.Services.Subscription.SubscriptionPlanService>();
builder.Services.AddScoped<Blazor_Bedrock.Services.Subscription.IOrganizationSubscriptionService, Blazor_Bedrock.Services.Subscription.OrganizationSubscriptionService>();
builder.Services.AddScoped<Blazor_Bedrock.Services.Subscription.ISubscriptionLimitationService, Blazor_Bedrock.Services.Subscription.SubscriptionLimitationService>();

// Data Protection for API key encryption
builder.Services.AddDataProtection();

// HttpClient for ChatGPT API calls (AddHttpClient registers the service automatically)
builder.Services.AddHttpClient<IChatGptService, ChatGptService>();
builder.Services.AddHttpClient<IOpenAIFileThreadService, OpenAIFileThreadService>();
builder.Services.AddScoped<IPromptService, PromptService>();
builder.Services.AddScoped<IQuestionService, QuestionService>();

var app = builder.Build();

// Update the logger provider to use the app's service provider
loggerProvider.UpdateServiceProvider(app.Services);

// Database Migration and Seeding
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        
        // Ensure database exists
        var logger = services.GetRequiredService<ILogger<Program>>();
        try
        {
            var canConnect = await context.Database.CanConnectAsync();
            if (!canConnect)
            {
                logger.LogInformation("Database does not exist. Creating database...");
                await context.Database.EnsureCreatedAsync();
                logger.LogInformation("Database created successfully.");
            }
            else
            {
                logger.LogInformation("Database connection verified.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking/creating database. This may be normal if the database server is not available.");
            // Continue - migrations will handle database creation if needed
        }
        
        // Apply migrations - this will create all tables and apply schema changes
        try
        {
            logger.LogInformation("Applying database migrations...");
            var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
            if (pendingMigrations.Any())
            {
                logger.LogInformation("Found {Count} pending migration(s): {Migrations}", 
                    pendingMigrations.Count(), 
                    string.Join(", ", pendingMigrations));
            }
            
            await context.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully.");
        }
        catch (MySqlConnector.MySqlException ex) when (ex.Message.Contains("already exists") || 
                                                       (ex.Message.Contains("Table") && ex.Message.Contains("already exists")))
        {
            // Table already exists - this can happen if migrations were partially applied
            // This is acceptable and we can continue
            logger.LogWarning(ex, "Some tables already exist. This may indicate a partially applied migration. Continuing...");
        }
        catch (Exception ex)
        {
            // For a fresh database, migration errors should be logged but we should still try to continue
            // The application may still work if core tables exist
            logger.LogError(ex, "An error occurred during database migration: {ErrorMessage}", ex.Message);
            logger.LogWarning("Attempting to continue application startup despite migration error. Some features may not work correctly.");
            // Don't rethrow - allow application to start, but log the error clearly
        }
        
        // Seed database
        var seeder = new DatabaseSeeder(context, userManager, roleManager);
        await seeder.SeedAsync();
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating or seeding the database.");
    }
}

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

// Session
app.UseSession();

// Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// External Auth Feature Flag Middleware (must be after auth but before tenant middleware)
app.UseMiddleware<Blazor_Bedrock.Services.Auth.ExternalAuthFeatureMiddleware>();

// Tenant Middleware
app.UseMiddleware<TenantMiddleware>();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Login endpoint for handling authentication via form POST (allows proper cookie setting)
app.MapPost("/auth/login-post", async (HttpContext context, IIdentityService identityService) =>
{
    var email = context.Request.Form["email"].ToString();
    var password = context.Request.Form["password"].ToString();
    var rememberMe = context.Request.Form["rememberMe"].ToString() == "true";
    
    if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
    {
        context.Response.Redirect("/auth/login?error=Email and password are required");
        return;
    }
    
    var result = await identityService.SignInAsync(email, password, rememberMe);
    
    if (result.Succeeded)
    {
        context.Response.Redirect("/");
    }
    else if (result.IsLockedOut)
    {
        context.Response.Redirect("/auth/login?error=Account is locked out");
    }
    else if (result.IsNotAllowed)
    {
        context.Response.Redirect("/auth/login?error=Account is not allowed");
    }
    else
    {
        context.Response.Redirect("/auth/login?error=Invalid email or password");
    }
}).AllowAnonymous();

// Logout endpoint for handling sign out (allows proper cookie clearing)
app.MapGet("/auth/logout-post", async (HttpContext context, IIdentityService identityService) =>
{
    await identityService.SignOutAsync();
    
    // Clear tenant session/cookies
    context.Session.Clear();
    if (context.Request.Cookies.ContainsKey("CurrentTenantId"))
    {
        context.Response.Cookies.Delete("CurrentTenantId");
    }
    
    context.Response.Redirect("/auth/login");
}).AllowAnonymous();

// External authentication endpoints - initiate OAuth flow
app.MapGet("/auth/external/google", async (HttpContext context) =>
{
    await context.ChallengeAsync("Google", new AuthenticationProperties
    {
        RedirectUri = "/signin-google"
    });
}).AllowAnonymous();

app.MapGet("/auth/external/facebook", async (HttpContext context) =>
{
    await context.ChallengeAsync("Facebook", new AuthenticationProperties
    {
        RedirectUri = "/signin-facebook"
    });
}).AllowAnonymous();

// External authentication callbacks
app.MapGet("/signin-google", async (HttpContext context, IIdentityService identityService, ILogger<Program> logger) =>
{
    var loginInfo = await identityService.GetExternalLoginInfoAsync();
    if (loginInfo == null)
    {
        logger.LogWarning("Failed to retrieve external login information for Google");
        context.Response.Redirect("/auth/login?error=Failed to retrieve Google authentication information");
        return;
    }

    // Try to sign in with external login
    var signInResult = await identityService.ExternalLoginSignInAsync(loginInfo.LoginProvider, loginInfo.ProviderKey, isPersistent: true);
    
    if (signInResult.Succeeded)
    {
        context.Response.Redirect("/");
        return;
    }

    // If sign-in failed, user doesn't exist yet - create them
    if (signInResult.IsNotAllowed)
    {
        context.Response.Redirect("/auth/login?error=Account is not allowed");
        return;
    }

    // Create new user from external login
    var createResult = await identityService.CreateExternalUserAsync(loginInfo);
    if (createResult.Succeeded)
    {
        context.Response.Redirect("/");
    }
    else
    {
        var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
        logger.LogError("Failed to create user from Google authentication: {Errors}", errors);
        context.Response.Redirect($"/auth/login?error={Uri.EscapeDataString(errors)}");
    }
}).AllowAnonymous();

app.MapGet("/signin-facebook", async (HttpContext context, IIdentityService identityService, ILogger<Program> logger) =>
{
    var loginInfo = await identityService.GetExternalLoginInfoAsync();
    if (loginInfo == null)
    {
        logger.LogWarning("Failed to retrieve external login information for Facebook");
        context.Response.Redirect("/auth/login?error=Failed to retrieve Facebook authentication information");
        return;
    }

    // Try to sign in with external login
    var signInResult = await identityService.ExternalLoginSignInAsync(loginInfo.LoginProvider, loginInfo.ProviderKey, isPersistent: true);
    
    if (signInResult.Succeeded)
    {
        context.Response.Redirect("/");
        return;
    }

    // If sign-in failed, user doesn't exist yet - create them
    if (signInResult.IsNotAllowed)
    {
        context.Response.Redirect("/auth/login?error=Account is not allowed");
        return;
    }

    // Create new user from external login
    var createResult = await identityService.CreateExternalUserAsync(loginInfo);
    if (createResult.Succeeded)
    {
        context.Response.Redirect("/");
    }
    else
    {
        var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
        logger.LogError("Failed to create user from Facebook authentication: {Errors}", errors);
        context.Response.Redirect($"/auth/login?error={Uri.EscapeDataString(errors)}");
    }
}).AllowAnonymous();

// Health check endpoint for Cloud Run
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Document file endpoint
app.MapGet("/api/documents/{id}/file", async (int id, HttpContext context, IDocumentService documentService, ITenantService tenantService) =>
{
    // Get user ID from HttpContext.User (available after authentication middleware)
    var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    var tenantId = tenantService.GetCurrentTenantId();

    if (string.IsNullOrEmpty(userId) || tenantId == null)
    {
        return Results.Unauthorized();
    }

    try
    {
        var (fileContent, contentType, fileName) = await documentService.GetDocumentFileWithMetadataAsync(id, userId, tenantId.Value);
        return Results.File(fileContent, contentType, fileName);
    }
    catch (FileNotFoundException)
    {
        return Results.NotFound();
    }
    catch
    {
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

// Export conversation to DOCX endpoint
app.MapGet("/api/conversations/{id}/export", async (int id, HttpContext context, IChatGptService chatGptService, ITenantService tenantService) =>
{
    // Get user ID from HttpContext.User (available after authentication middleware)
    var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    var tenantId = tenantService.GetCurrentTenantId();

    if (string.IsNullOrEmpty(userId))
    {
        return Results.Unauthorized();
    }

    try
    {
        var docxBytes = await chatGptService.ExportConversationToDocxAsync(id, userId, tenantId);
        var fileName = $"conversation_{id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.docx";
        return Results.File(docxBytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", fileName);
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error exporting conversation {ConversationId}", id);
        return Results.StatusCode(500);
    }
}).RequireAuthorization();

app.Run();
