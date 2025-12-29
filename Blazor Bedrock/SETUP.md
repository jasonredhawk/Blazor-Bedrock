# Blazor Bedrock Setup Guide

## Overview

This is a comprehensive Blazor Server bedrock application with the following features:

- **Multi-Tenant Architecture**: Complete tenant isolation with user-tenant many-to-many relationships
- **Authentication**: Username/password, Google OAuth, Facebook OAuth
- **User & Role Management**: Full CRUD with custom permissions system
- **ChatGPT Integration**: API key management, chat interface, document analysis
- **Stripe Integration**: Payment and subscription management
- **Migration Management**: SuperAdmin page for managing database migrations
- **Application Logger**: Browser-based logger with CTRL+` toggle
- **Feature Flags**: Database-driven module enablement
- **Modular Design**: All components are feature-flag gated and independent

## Prerequisites

1. .NET 9.0 SDK
2. MySQL Server (local or Cloud SQL)
3. (Optional) Google OAuth credentials
4. (Optional) Facebook OAuth credentials
5. (Optional) Stripe API keys
6. (Optional) OpenAI API key

## Setup Steps

### 1. Database Configuration

Update `appsettings.json` with your MySQL connection string:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=BlazorBedrock;User=root;Password=YOUR_PASSWORD;Port=3306;"
  }
}
```

### 2. Create Initial Migration

Run the following commands in the project directory:

```bash
dotnet ef migrations add InitialCreate
dotnet ef database update
```

The database will be created and seeded with:
- Default Admin user (admin@bedrock.local / Admin@123!)
- Default Admin role
- Initial feature flags
- Initial ChatGPT prompts

### 3. Configure External Services (Optional)

#### Google OAuth
1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create OAuth 2.0 credentials
3. Update `appsettings.json`:
```json
{
  "Authentication": {
    "Google": {
      "ClientId": "YOUR_CLIENT_ID",
      "ClientSecret": "YOUR_CLIENT_SECRET"
    }
  }
}
```
4. Enable the feature flag: `Auth_Google`

#### Facebook OAuth
1. Go to [Facebook Developers](https://developers.facebook.com/)
2. Create an app and get credentials
3. Update `appsettings.json`:
```json
{
  "Authentication": {
    "Facebook": {
      "AppId": "YOUR_APP_ID",
      "AppSecret": "YOUR_APP_SECRET"
    }
  }
}
```
4. Enable the feature flag: `Auth_Facebook`

#### Stripe
1. Get API keys from [Stripe Dashboard](https://dashboard.stripe.com/)
2. Update `appsettings.json`:
```json
{
  "Stripe": {
    "PublishableKey": "YOUR_PUBLISHABLE_KEY",
    "SecretKey": "YOUR_SECRET_KEY"
  }
}
```
3. Enable the feature flag: `Stripe_Enabled`

### 4. Run the Application

```bash
dotnet run
```

Navigate to `https://localhost:5001` (or the port shown in the console)

### 5. First Login

1. Login with the default admin account:
   - Email: `admin@bedrock.local`
   - Password: `Admin@123!`

2. Create your first tenant/organization
3. Configure your ChatGPT API key in Profile settings (if using ChatGPT features)

## Feature Flags

Feature flags are stored in the database and can be toggled to enable/disable modules:

- `Auth_Google` - Google Authentication
- `Auth_Facebook` - Facebook Authentication
- `ChatGpt_Enabled` - ChatGPT Integration
- `Stripe_Enabled` - Stripe Payments
- `Migrations_Enabled` - Migration Management
- `Logger_Enabled` - Application Logger

## Google Cloud Run Deployment

### 1. Build Docker Image

```bash
docker build -t gcr.io/YOUR_PROJECT_ID/blazor-bedrock .
```

### 2. Push to Container Registry

```bash
docker push gcr.io/YOUR_PROJECT_ID/blazor-bedrock
```

### 3. Deploy to Cloud Run

```bash
gcloud run deploy blazor-bedrock \
  --image gcr.io/YOUR_PROJECT_ID/blazor-bedrock \
  --platform managed \
  --region us-central1 \
  --allow-unauthenticated
```

### 4. Configure Cloud SQL

Update `appsettings.Production.json` with your Cloud SQL connection string format.

## Architecture Notes

### Multi-Tenancy
- All tenant-scoped data includes `TenantId`
- Tenant is selected via dropdown in the top menu
- Tenant context is maintained via session/cookies
- Data isolation is enforced at the service layer

### Permissions
- Permissions are fully customizable (not predefined)
- Permissions are assigned to roles
- Roles are assigned to users per tenant
- Users can have different roles in different tenants

### Modularity
- Each module is feature-flag gated
- Modules are independent with no cross-dependencies
- Use `FeatureGate` component to conditionally render module content

## Next Steps

1. Run the initial migration
2. Login and create your first tenant
3. Configure external services as needed
4. Customize permissions and roles for your use case
5. Deploy to Google Cloud Run when ready

## Support

For issues or questions, refer to the codebase documentation in each service/component file.

