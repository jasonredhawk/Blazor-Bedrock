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
using Microsoft.AspNetCore.DataProtection;
using System.IO;

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

// Data Protection for API key encryption (needed for reading encrypted credentials from database)
// Persist keys to ensure OAuth state remains valid across app restarts
// Use a consistent location so keys persist across app restarts
var dataProtectionKeysPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
    "BlazorBedrock", 
    "DataProtection-Keys");

// Ensure the directory exists
Directory.CreateDirectory(dataProtectionKeysPath);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    // Allow keys to be used for 90 days to handle key rotation
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90))
    // Set application name to ensure keys are scoped to this app
    .SetApplicationName("BlazorBedrock");

// Configure Authentication first (before Identity) to allow external providers
var authBuilder = builder.Services.AddAuthentication();

// External Authentication - Google
// Load credentials from appsettings.json first (fallback), then from database (configured via Features page)
string googleClientId = string.Empty;
string googleClientSecret = string.Empty;

// First, try to load from appsettings.json (for development/testing)
var googleConfig = builder.Configuration.GetSection("Authentication:Google");
if (googleConfig.Exists())
{
    googleClientId = googleConfig["ClientId"] ?? string.Empty;
    googleClientSecret = googleConfig["ClientSecret"] ?? string.Empty;
    if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
    {
        Console.WriteLine("✓ Google credentials loaded from appsettings.json.");
    }
}

// Then, check database for credentials (requires temporary service provider)
// Database credentials will override appsettings.json if both are present
try
{
    // Build temporary service provider to access database
    var tempServices = builder.Services.BuildServiceProvider();
    using (var scope = tempServices.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dataProtectionProvider = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var logger = scope.ServiceProvider.GetService<ILogger<Program>>();
        
        // Check if database is available (synchronous check to avoid async issues in service configuration)
        try
        {
            if (context.Database.CanConnect())
            {
                logger?.LogInformation("Database connection verified. Checking for Google credentials...");
                var config = context.ApiConfigurations
                    .FirstOrDefault(ac => ac.ServiceName == "GoogleAuth" && ac.IsActive);
                
                if (config != null)
                {
                    logger?.LogInformation("GoogleAuth configuration record found in database. Decrypting...");
                    try
                    {
                        var protector = dataProtectionProvider.CreateProtector("ApiConfiguration_GoogleAuth");
                        var decryptedJson = protector.Unprotect(config.EncryptedConfiguration);
                        var configuration = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(decryptedJson);
                    
                    if (configuration != null)
                    {
                        bool hasClientId = configuration.TryGetValue("ClientId", out var dbClientId) && !string.IsNullOrEmpty(dbClientId);
                        bool hasClientSecret = configuration.TryGetValue("ClientSecret", out var dbClientSecret) && !string.IsNullOrEmpty(dbClientSecret);
                        
                        if (hasClientId)
                        {
                            googleClientId = dbClientId;
                            logger?.LogInformation("✓ Google Client ID loaded from database during startup.");
                            Console.WriteLine("✓ Google Client ID loaded from database (overrides appsettings.json).");
                        }
                        else
                        {
                            logger?.LogWarning("GoogleAuth config found but ClientId is missing or empty.");
                        }
                        
                        if (hasClientSecret)
                        {
                            googleClientSecret = dbClientSecret;
                            logger?.LogInformation("✓ Google Client Secret loaded from database during startup.");
                            Console.WriteLine("✓ Google Client Secret loaded from database (overrides appsettings.json).");
                        }
                        else
                        {
                            logger?.LogWarning("GoogleAuth config found but ClientSecret is missing or empty.");
                        }
                        
                        if (!hasClientId || !hasClientSecret)
                        {
                            logger?.LogWarning("GoogleAuth configuration incomplete. ClientId: {HasId}, ClientSecret: {HasSecret}", hasClientId, hasClientSecret);
                        }
                    }
                    else
                    {
                        logger?.LogWarning("GoogleAuth config found but decrypted configuration is null.");
                    }
                    }
                    catch (System.Security.Cryptography.CryptographicException ex)
                    {
                        logger?.LogError(ex, "⚠️ CRITICAL: Cannot decrypt Google credentials!");
                        if (ex.Message.Contains("key") || ex.Message.Contains("not found"))
                        {
                            logger?.LogError("⚠️ This happens when data protection keys change (e.g., after adding key persistence).");
                            logger?.LogError("⚠️ SOLUTION: Re-save your Google credentials via Features page to re-encrypt with new keys.");
                            Console.WriteLine("═══════════════════════════════════════════════════════════════");
                            Console.WriteLine("⚠️  CRITICAL: Cannot decrypt Google credentials!");
                            Console.WriteLine("⚠️  The encryption keys have changed.");
                            Console.WriteLine("⚠️  SOLUTION: Go to Features page and re-save your Google credentials.");
                            Console.WriteLine("⚠️  OR: Add credentials to appsettings.json as a temporary workaround.");
                            Console.WriteLine("═══════════════════════════════════════════════════════════════");
                        }
                        else
                        {
                            logger?.LogError("⚠️ Decryption failed: {Message}", ex.Message);
                            Console.WriteLine($"⚠️  Cannot decrypt Google credentials: {ex.Message}");
                            Console.WriteLine("⚠️  SOLUTION: Re-save your Google credentials via Features page.");
                            Console.WriteLine("⚠️  OR: Add credentials to appsettings.json as a temporary workaround.");
                        }
                    }
                }
                else
                {
                    logger?.LogInformation("No Google authentication credentials found in database during startup.");
                    Console.WriteLine("⚠️ No Google credentials found in database during startup.");
                }
            }
            else
            {
                logger?.LogWarning("Database not available during startup. Google authentication credentials will be checked after migrations.");
                Console.WriteLine("⚠️ Database not available during startup. Will check after migrations.");
            }
        }
        catch (Exception dbEx)
        {
            logger?.LogError(dbEx, "Error checking database for Google credentials during startup: {Message}", dbEx.Message);
            Console.WriteLine($"⚠️ Error checking database: {dbEx.Message}");
        }
    }
    tempServices.Dispose();
}
catch (Exception ex)
{
    // Log but don't fail - database might not be available yet, credentials will be checked again after migrations
    var tempLogger = builder.Services.BuildServiceProvider().GetService<ILogger<Program>>();
    tempLogger?.LogWarning(ex, "Could not load Google credentials from database during startup. Will check again after migrations.");
    tempLogger?.LogWarning("If you just saved credentials via Features page, restart the application for them to take effect.");
}

if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        // Callback path - must match the redirect URI in Google Cloud Console
        options.CallbackPath = "/signin-google";
        // Save tokens for external authentication
        options.SaveTokens = true;
        // Configure cookie settings for OAuth state
        // MUST use None for SameSite to allow cross-site redirects (Google -> localhost)
        // Secure=true is REQUIRED when SameSite=None
        // Note: Some browsers on localhost may block these cookies - if so, try a different browser or use 127.0.0.1
        options.CorrelationCookie.SameSite = SameSiteMode.None;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always; // Must be Always for SameSite=None
        options.CorrelationCookie.HttpOnly = true;
        options.CorrelationCookie.Path = "/"; // Set path to root so cookie is available for all paths
        options.CorrelationCookie.MaxAge = TimeSpan.FromMinutes(10); // Set expiration time
        // Mark as essential - this helps some browsers allow it
        try
        {
            // IsEssential might not be available in all versions, so wrap in try-catch
            var cookieBuilder = options.CorrelationCookie;
            if (cookieBuilder.GetType().GetProperty("IsEssential") != null)
            {
                cookieBuilder.GetType().GetProperty("IsEssential")?.SetValue(cookieBuilder, true);
            }
        }
        catch { }
        // Don't set domain - let it default to the request domain (localhost)
    });
    Console.WriteLine("✓ Google authentication handler registered successfully.");
}
else
{
    Console.WriteLine("⚠️ Google authentication handler NOT registered - credentials not found during startup.");
    Console.WriteLine("   Options:");
    Console.WriteLine("   1. Add credentials to appsettings.json under 'Authentication:Google' section");
    Console.WriteLine("   2. Or configure credentials via Features page and restart the application");
    Console.WriteLine("   Example appsettings.json:");
    Console.WriteLine("   {");
    Console.WriteLine("     \"Authentication\": {");
    Console.WriteLine("       \"Google\": {");
    Console.WriteLine("         \"ClientId\": \"YOUR_CLIENT_ID\",");
    Console.WriteLine("         \"ClientSecret\": \"YOUR_CLIENT_SECRET\"");
    Console.WriteLine("       }");
    Console.WriteLine("     }");
    Console.WriteLine("   }");
}

// External Authentication - Facebook
// Load credentials from database (configured via Features page)
string facebookAppId = string.Empty;
string facebookAppSecret = string.Empty;

// Check database for credentials
try
{
    // Build temporary service provider to access database
    var tempServices = builder.Services.BuildServiceProvider();
    using (var scope = tempServices.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dataProtectionProvider = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        
        // Check if database is available (synchronous check to avoid async issues in service configuration)
        if (context.Database.CanConnect())
        {
            var config = context.ApiConfigurations
                .FirstOrDefault(ac => ac.ServiceName == "FacebookAuth" && ac.IsActive);
            
            if (config != null)
            {
                var protector = dataProtectionProvider.CreateProtector("ApiConfiguration_FacebookAuth");
                var decryptedJson = protector.Unprotect(config.EncryptedConfiguration);
                var configuration = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(decryptedJson);
                
                if (configuration != null)
                {
                    if (configuration.TryGetValue("AppId", out var dbAppId) && !string.IsNullOrEmpty(dbAppId))
                        facebookAppId = dbAppId;
                    if (configuration.TryGetValue("AppSecret", out var dbAppSecret) && !string.IsNullOrEmpty(dbAppSecret))
                        facebookAppSecret = dbAppSecret;
                }
            }
        }
    }
    tempServices.Dispose();
}
catch (Exception)
{
    // Silently fail - database might not be available yet, credentials will be checked again after migrations
    // This is expected if the database hasn't been created yet
}

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
    // Use None for SameSite to allow OAuth redirects (Google -> localhost)
    // Secure=true is required when SameSite=None (works with HTTPS)
    // Note: Some browsers on localhost may block these cookies - if so, try using 127.0.0.1 instead of localhost
    options.Cookie.SameSite = SameSiteMode.None;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // Always use Secure for SameSite=None
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
    // Use Lax for SameSite - works for top-level navigations (OAuth redirects)
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // Works with both HTTP and HTTPS
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
        
        // After migrations, check if Google/Facebook credentials exist but handlers weren't registered
        // This happens if credentials were saved via Features page after app started
        try
        {
            var dataProtectionProvider = services.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
            var authSchemeProvider = services.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>();
            
            // Check for Google credentials
            var googleConfigRecord = await context.ApiConfigurations
                .FirstOrDefaultAsync(ac => ac.ServiceName == "GoogleAuth" && ac.IsActive);
            
            if (googleConfigRecord != null)
            {
                try
                {
                    var protector = dataProtectionProvider.CreateProtector("ApiConfiguration_GoogleAuth");
                    var decryptedJson = protector.Unprotect(googleConfigRecord.EncryptedConfiguration);
                    var configuration = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(decryptedJson);
                    
                    if (configuration != null && 
                        configuration.TryGetValue("ClientId", out var dbGoogleClientId) && !string.IsNullOrEmpty(dbGoogleClientId) &&
                        configuration.TryGetValue("ClientSecret", out var dbGoogleClientSecret) && !string.IsNullOrEmpty(dbGoogleClientSecret))
                    {
                        var googleScheme = await authSchemeProvider.GetSchemeAsync("Google");
                        if (googleScheme == null)
                        {
                            logger.LogError("═══════════════════════════════════════════════════════════════");
                            logger.LogError("⚠️  CRITICAL: Google credentials found in database but handler NOT registered!");
                            logger.LogError("⚠️  Client ID: {ClientId}", dbGoogleClientId?.Substring(0, Math.Min(20, dbGoogleClientId?.Length ?? 0)) + "...");
                            logger.LogError("⚠️  ACTION REQUIRED: Restart the application NOW for Google authentication to work.");
                            logger.LogError("⚠️  This happens when credentials are saved via Features page after app started.");
                            logger.LogError("═══════════════════════════════════════════════════════════════");
                            Console.WriteLine("═══════════════════════════════════════════════════════════════");
                            Console.WriteLine("⚠️  CRITICAL: Google credentials found but handler NOT registered!");
                            Console.WriteLine("⚠️  ACTION REQUIRED: Restart the application NOW!");
                            Console.WriteLine("═══════════════════════════════════════════════════════════════");
                        }
                        else
                        {
                            logger.LogInformation("✓ Google authentication is configured and available.");
                            Console.WriteLine("✓ Google authentication handler is registered and ready.");
                        }
                    }
                    else
                    {
                        logger.LogWarning("GoogleAuth config found but ClientId or ClientSecret is missing/empty.");
                    }
                }
                catch (System.Security.Cryptography.CryptographicException ex) when (ex.Message.Contains("key") && ex.Message.Contains("not found"))
                {
                    logger.LogError("⚠️ Cannot decrypt Google credentials - data protection key mismatch!");
                    logger.LogError("⚠️ SOLUTION: Re-save your Google credentials via Features page.");
                }
            }
            else
            {
                logger.LogInformation("No GoogleAuth configuration found in database.");
            }
            
            // Check for Facebook credentials
            var facebookConfig = await context.ApiConfigurations
                .FirstOrDefaultAsync(ac => ac.ServiceName == "FacebookAuth" && ac.IsActive);
            
            if (facebookConfig != null)
            {
                var protector = dataProtectionProvider.CreateProtector("ApiConfiguration_FacebookAuth");
                var decryptedJson = protector.Unprotect(facebookConfig.EncryptedConfiguration);
                var configuration = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(decryptedJson);
                
                if (configuration != null && 
                    configuration.TryGetValue("AppId", out var dbFacebookAppId) && !string.IsNullOrEmpty(dbFacebookAppId) &&
                    configuration.TryGetValue("AppSecret", out var dbFacebookAppSecret) && !string.IsNullOrEmpty(dbFacebookAppSecret))
                {
                    var facebookScheme = await authSchemeProvider.GetSchemeAsync("Facebook");
                    if (facebookScheme == null)
                    {
                        logger.LogError("⚠️ Facebook credentials found in database but Facebook authentication handler is NOT registered.");
                        logger.LogError("⚠️ ACTION REQUIRED: Restart the application for Facebook authentication to work.");
                        logger.LogError("⚠️ This happens when credentials are saved via Features page after the app has started.");
                    }
                    else
                    {
                        logger.LogInformation("✓ Facebook authentication is configured and available.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not verify authentication credentials from database after migrations.");
        }
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

// Diagnostic endpoint to check Google auth configuration
app.MapGet("/auth/debug/google", async (HttpContext context) =>
{
    var schemeProvider = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
    var scheme = await schemeProvider.GetSchemeAsync("Google");
    
    var response = new System.Text.StringBuilder();
    response.AppendLine("<h1>Google Authentication Debug</h1>");
    response.AppendLine($"<p><strong>Scheme Registered:</strong> {(scheme != null ? "✓ YES" : "✗ NO")}</p>");
    
    if (scheme != null)
    {
        response.AppendLine($"<p><strong>Scheme Name:</strong> {scheme.Name}</p>");
        response.AppendLine($"<p><strong>Handler Type:</strong> {scheme.HandlerType?.Name ?? "Unknown"}</p>");
    }
    
    // Check appsettings
    var googleConfig = context.RequestServices.GetRequiredService<IConfiguration>().GetSection("Authentication:Google");
    response.AppendLine($"<p><strong>AppSettings Config Exists:</strong> {googleConfig.Exists()}</p>");
    if (googleConfig.Exists())
    {
        var clientId = googleConfig["ClientId"];
        var clientSecret = googleConfig["ClientSecret"];
        response.AppendLine($"<p><strong>Client ID:</strong> {(string.IsNullOrEmpty(clientId) ? "✗ MISSING" : $"✓ Set ({clientId.Substring(0, Math.Min(20, clientId.Length))}...)")}</p>");
        response.AppendLine($"<p><strong>Client Secret:</strong> {(string.IsNullOrEmpty(clientSecret) ? "✗ MISSING" : "✓ Set")}</p>");
    }
    
    response.AppendLine("<hr>");
    response.AppendLine("<h2>Next Steps:</h2>");
    if (scheme == null)
    {
        response.AppendLine("<p style='color:red;'><strong>❌ Google authentication is NOT configured!</strong></p>");
        response.AppendLine("<ol>");
        response.AppendLine("<li>Add credentials to appsettings.json under 'Authentication:Google'</li>");
        response.AppendLine("<li>Restart the application</li>");
        response.AppendLine("<li>Verify in Google Cloud Console that redirect URI is: <code>https://localhost:7035/signin-google</code></li>");
        response.AppendLine("</ol>");
    }
    else
    {
        response.AppendLine("<p style='color:green;'><strong>✓ Google authentication IS configured!</strong></p>");
        response.AppendLine("<ol>");
        response.AppendLine("<li>Verify in Google Cloud Console that redirect URI is: <code>https://localhost:7035/signin-google</code></li>");
        response.AppendLine("<li>Make sure you're accessing the app via <strong>HTTPS</strong> (not HTTP)</li>");
        response.AppendLine("<li>Try clearing browser cookies and trying again</li>");
        response.AppendLine("<li>If cookies are blocked, try using <code>127.0.0.1</code> instead of <code>localhost</code></li>");
        response.AppendLine("</ol>");
    }
    
    context.Response.ContentType = "text/html";
    await context.Response.WriteAsync(response.ToString());
}).AllowAnonymous();

// External authentication endpoints - initiate OAuth flow
app.MapGet("/auth/external/google", async (HttpContext context) =>
{
    var schemeProvider = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
    var scheme = await schemeProvider.GetSchemeAsync("Google");
    
    if (scheme == null)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("Google authentication is not configured. Please configure Google credentials in the Features page and restart the application. <a href='/auth/debug/google'>Click here for diagnostic info</a>");
        return;
    }
    
    // Log before challenge to help debug
    var logger = context.RequestServices.GetService<ILogger<Program>>();
    logger?.LogInformation("Initiating Google OAuth challenge. Redirect URI: /signin-google");
    var requestUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
    logger?.LogInformation("Request URL: {Url}", requestUrl);
    
    await context.ChallengeAsync("Google", new AuthenticationProperties
    {
        RedirectUri = "/signin-google"
    });
}).AllowAnonymous();

app.MapGet("/auth/external/facebook", async (HttpContext context) =>
{
    var schemeProvider = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
    var scheme = await schemeProvider.GetSchemeAsync("Facebook");
    
    if (scheme == null)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("Facebook authentication is not configured. Please configure Facebook credentials in the Features page and restart the application.");
        return;
    }
    
    await context.ChallengeAsync("Facebook", new AuthenticationProperties
    {
        RedirectUri = "/signin-facebook"
    });
}).AllowAnonymous();

// External authentication callbacks
app.MapGet("/signin-google", async (HttpContext context, IIdentityService identityService, ILogger<Program> logger) =>
{
    // Debug: Log all cookies
    logger.LogInformation("=== Google Callback Debug ===");
    var requestUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
    logger.LogInformation("Request URL: {Url}", requestUrl);
    logger.LogInformation("Cookies received: {Count}", context.Request.Cookies.Count);
    foreach (var cookie in context.Request.Cookies)
    {
        logger.LogInformation("Cookie: {Name} = {Value}", cookie.Key, cookie.Value?.Substring(0, Math.Min(50, cookie.Value?.Length ?? 0)));
    }
    
    var loginInfo = await identityService.GetExternalLoginInfoAsync();
    if (loginInfo == null)
    {
        logger.LogWarning("Failed to retrieve external login information for Google");
        logger.LogWarning("This usually means the OAuth state cookie was lost during redirect");
        logger.LogWarning("Check browser console and DevTools → Application → Cookies to see if correlation cookie exists");
        context.Response.Redirect("/auth/login?error=Failed to retrieve Google authentication information. The OAuth state cookie may have been blocked by your browser.");
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
