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
using Blazor_Bedrock.Services.Migrations;
using Blazor_Bedrock.Services.Stripe;
using Blazor_Bedrock.Infrastructure.ExternalApis;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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

// Identity Configuration
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

// External Authentication - Google
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    builder.Services.AddAuthentication()
        .AddGoogle(options =>
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
    builder.Services.AddAuthentication()
        .AddFacebook(options =>
        {
            options.AppId = facebookAppId;
            options.AppSecret = facebookAppSecret;
        });
}

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
builder.Services.AddSingleton<IApplicationLoggerService, ApplicationLoggerService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IDocumentProcessor, DocumentProcessor>();
builder.Services.AddScoped<IMigrationService, MigrationService>();
builder.Services.AddScoped<IStripeService, StripeService>();

// Data Protection for API key encryption
builder.Services.AddDataProtection();

// HttpClient for ChatGPT API calls (AddHttpClient registers the service automatically)
builder.Services.AddHttpClient<IChatGptService, ChatGptService>();
builder.Services.AddScoped<IPromptService, PromptService>();

var app = builder.Build();

// Database Migration and Seeding
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        
        // Apply migrations
        context.Database.Migrate();
        
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

// Health check endpoint for Cloud Run
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
