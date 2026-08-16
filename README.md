# V-Fridge API

ASP.NET Core Minimal API (.NET 10) for **V-Fridge** — food inventory management, smart meal planning, calorie and macro nutrition tracking, shopping list auto-replenishment, shared fridges, and AI Chef integration via REST and gRPC.

This service exposes a typed REST surface for web and mobile clients, backed by PostgreSQL via Entity Framework Core 10 and integrated with the Go-based V-Chef microservice.

---

## Tech Stack

* **Runtime:** .NET 10, ASP.NET Core Minimal API
* **Database & ORM:** PostgreSQL via Entity Framework Core 10 (Npgsql provider) with EF Core Migrations
* **Microservices Integration:** V-Chef Go microservice client (`IVChefClient`) supporting both HTTP REST (`VChefClient`) and gRPC (`VChefGrpcClient` via `chef.proto`) with internal token authentication and non-blocking warmup (`VChefWarmupService`)
* **Background Workers:** `DailyMaintenanceWorker` (daily 09:00 Europe/Kyiv cron for product expiry digests and unverified account cleanup) and `VChefWarmupService`
* **Authentication & Security:** Stateless JWT Bearer tokens + opaque refresh token rotation (returned in JSON body, no cookie dependence), Google OAuth ID token validation, email confirmation, rate limiting (Sliding Window)
* **Email Delivery:** SMTP (MailKit) or Resend API (HTTPS-based for cloud hosts blocking outbound SMTP)
* **AI Engine:** OpenRouter API (OpenAI-compatible chat completions with multi-model fallback) + V-Chef recipe generator
* **API Documentation:** Native .NET 10 OpenAPI document at `/openapi/v1.json`
* **Health Checks:** `/health` (DbContext & database connectivity check)
* **Testing:** xUnit, `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`), Testcontainers PostgreSQL

---

## Project Structure

```
src/
└── VFridge.Api/
    ├── Auth/                # Current user accessor (ICurrentUser, HttpContextCurrentUser)
    ├── Configuration/       # Strongly-typed configuration options (Jwt, Email, Cors, OpenRouter, Google, Frontend)
    ├── Contracts/           # DTOs across all 9 modules (Auth, Products, Fridges, Shopping, MealPlan, Nutrition, Analytics, Chat, VChef)
    ├── Data/                # EF Core DbContext, entity definitions, and model snapshot
    │   └── Entities/        # User, Product, Fridge, ShoppingItem, ConsumptionLog, MealPlan, SavedRecipe, NutritionLog, etc.
    ├── Endpoints/           # Minimal API route modules
    │   ├── AnalyticsEndpoints.cs
    │   ├── AuthEndpoints.cs
    │   ├── ChatEndpoints.cs
    │   ├── FridgeEndpoints.cs
    │   ├── MealPlanEndpoints.cs
    │   ├── NutritionEndpoints.cs
    │   ├── ProductsEndpoints.cs
    │   ├── SavedRecipeEndpoints.cs
    │   └── ShoppingEndpoints.cs
    ├── Infrastructure/      # PostgreSQL connection string normalizer
    ├── Migrations/          # EF Core Migrations (InitialCreate, etc.)
    ├── Protos/              # Protocol Buffers schema (chef.proto) for gRPC communication
    ├── Services/            # Business services (Auth, AI chat, meal planner, email sender, VChef clients, daily worker)
    ├── Program.cs           # Application composition root, DI, middleware, and route registration
    ├── appsettings.json     # Configuration schema and defaults (no secrets)
    └── Properties/
        └── launchSettings.json
tests/
└── VFridge.Api.Tests/       # Unit and integration test suites
```

---

## V-Chef Microservice Integration

`v-fridge-api` integrates with the external **V-Chef** microservice to offload and accelerate AI recipe generation and meal planning workflows.

### Integration Modes

The API supports two communication protocols with V-Chef, switchable via configuration (`VChef:UseGrpc`):

1. **HTTP REST (`VChefClient`):**
   * Configured via `VChef:BaseUrl` (default: `https://v-chef.onrender.com`).
   * Sends structured JSON requests (`POST /api/v1/recipes/generate`) with inventory and dietary preferences.
   * Includes the `X-Internal-Token` security header for service-to-service authentication.

2. **gRPC (`VChefGrpcClient`):**
   * Configured via `VChef:GrpcUrl` (default: `http://localhost:50051`).
   * Uses strongly-typed Protocol Buffers generated from `Protos/chef.proto`.
   * Attaches internal token authentication via gRPC call metadata interceptor.

### Warmup Service (`VChefWarmupService`)

A hosted `BackgroundService` that triggers a non-blocking health check (`GET /health`) against V-Chef on application startup. This pre-warms free-tier cloud instances (e.g. Render) before incoming user traffic arrives.

---

## Configuration

All configuration is mapped from `appsettings.json`, environment variables, or `.env` files (loaded automatically at startup via `DotNetEnv`).

### Environment Variables & Keys

| Key | Environment Variable | Purpose | Default |
| --- | --- | --- | --- |
| `ConnectionStrings:Default` | `DATABASE_URL` / `ConnectionStrings__Default` | PostgreSQL connection string or libpq URI | Required |
| `Jwt:Secret` | `Jwt__Secret` | Signing key for HMAC-SHA256 (>= 32 chars) | Required in Prod |
| `Jwt:Issuer` | `Jwt__Issuer` | JWT issuer claim | `v-fridge-api` |
| `Jwt:Audience` | `Jwt__Audience` | JWT audience claim | `v-fridge-app` |
| `Email:Provider` | `Email__Provider` | Email provider: `smtp` or `resend` | `smtp` |
| `Email:SmtpHost` | `Email__SmtpHost` | SMTP server host | `""` |
| `Email:SmtpPort` | `Email__SmtpPort` | SMTP port (e.g. 587) | `587` |
| `Email:Username` | `Email__Username` | SMTP username | `""` |
| `Email:Password` | `Email__Password` | SMTP password / App password | `""` |
| `Email:ResendApiKey` | `Email__ResendApiKey` | API key when provider is `resend` | `""` |
| `Email:FromAddress` | `Email__FromAddress` | Sender email address | `""` |
| `Google:ClientId` | `Google__ClientId` | Google OAuth Client ID | `""` |
| `Google:ClientSecret` | `Google__ClientSecret` | Google OAuth Client Secret | `""` |
| `OpenRouter:ApiKey` | `OpenRouter__ApiKey` | API key for OpenRouter AI completions | `""` |
| `OpenRouter:Models` | `OpenRouter__Models` | Ordered model fallback pool | `["google/gemma-4-31b-it:free", ...]` |
| `Cors:AllowedOrigins` | `Cors__AllowedOrigins` | Allowed origins array for CORS | `["http://localhost:3000", ...]` |
| `Cors:AllowAnyOrigin` | `Cors__AllowAnyOrigin` | Allow wildcard origin (disables credentials) | `false` |
| `Frontend:BaseUrl` | `Frontend__BaseUrl` | Base URL for email verification redirects | `http://localhost:3000` |
| `VChef:BaseUrl` | `VChef__BaseUrl` | Base URL for V-Chef REST API | `https://v-chef.onrender.com` |
| `VChef:GrpcUrl` | `VChef__GrpcUrl` | Address for V-Chef gRPC endpoint | `http://localhost:50051` |
| `VChef:UseGrpc` | `VChef__UseGrpc` | Toggle gRPC client instead of REST | `false` |
| `VChef:InternalToken` | `VCHEF_INTERNAL_TOKEN` / `VChef__InternalToken` | Shared-secret token for V-Chef API | `""` |

---

## API Endpoints

The API listens on `http://localhost:5080` by default.

### 1. Authentication (`/auth`)

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `POST` | `/auth/signup` | Public | Register new user account; sends verification email |
| `POST` | `/auth/login` | Public | Authenticate with email & password; returns access & refresh tokens |
| `POST` | `/auth/refresh` | Public | Exchange valid refresh token for new access & refresh token pair |
| `POST` | `/auth/logout` | Public | Revoke refresh token |
| `GET` | `/auth/verify-email?token=` | Public | Verify email via browser link; redirects to frontend SPA |
| `POST` | `/auth/verify-email` | Public | Verify email via JSON API body |
| `POST` | `/auth/resend-verification` | Public | Resend verification email |
| `POST` | `/auth/google` | Public | Sign in / register with Google OAuth ID token |
| `GET` | `/auth/me` | Bearer | Get current authenticated user profile & preferences |
| `PATCH` | `/auth/me` | Bearer | Update user display name and profile settings |
| `PATCH` | `/auth/me/preferences` | Bearer | Update user cuisine, language, and dietary preferences |
| `POST` | `/auth/me/avatar` | Bearer | Upload user profile avatar image |

### 2. Shared Fridges (`/fridges`)

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/fridges` | Bearer | List all fridges owned by or shared with the caller |
| `POST` | `/fridges` | Bearer | Create a new shared fridge |
| `PATCH` | `/fridges/{id}` | Bearer | Rename a fridge (owner only) |
| `DELETE` | `/fridges/{id}` | Bearer | Delete a fridge and its contents (owner only) |
| `DELETE` | `/fridges/{id}/members/me` | Bearer | Leave a shared fridge |
| `POST` | `/fridges/{id}/invites` | Bearer | Invite another user by email (generates 7-day token) |
| `POST` | `/fridges/accept` | Bearer | Accept an invitation token to join a fridge |

### 3. Products Inventory (`/products`)

Supports multi-fridge scoping via the `X-Fridge-Id` header (defaults to user's primary fridge).

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/products` | Bearer | List active products sorted by expiry date |
| `POST` | `/products` | Bearer | Add a product with category, quantity, unit, and expiry date |
| `PATCH` | `/products/{id}` | Bearer | Update product details, category, or remaining quantity |
| `DELETE` | `/products/{id}` | Bearer | Remove a product (records consumption or waste log) |
| `DELETE` | `/products` | Bearer | Clear all products from current fridge |

### 4. Shopping List (`/shopping`)

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/shopping` | Bearer | List items on shopping list for the active fridge |
| `POST` | `/shopping` | Bearer | Add an item to the shopping list |
| `PATCH` | `/shopping/{id}` | Bearer | Update item details or toggle checked state |
| `DELETE` | `/shopping/{id}` | Bearer | Delete item from shopping list |
| `POST` | `/shopping/{id}/purchase` | Bearer | Move purchased item from shopping list into fridge inventory |

### 5. Meal Planner (`/meal-plan`)

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/meal-plan` | Bearer | Retrieve the active weekly meal plan |
| `POST` | `/meal-plan` | Bearer | Generate a new weekly meal plan using AI and current inventory |
| `POST` | `/meal-plan/regenerate-day` | Bearer | Regenerate meals for a specific single day of the week |
| `POST` | `/meal-plan/regenerate-meal` | Bearer | Regenerate a single specific meal |
| `POST` | `/meal-plan/recipe` | Bearer | Retrieve full cooking instructions for a planned meal |
| `POST` | `/meal-plan/import-gaps` | Bearer | Bulk-import missing ingredients into the shopping list |

### 6. Saved Recipes (`/saved-recipes`)

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/saved-recipes` | Bearer | List bookmarked recipes with nutrition metadata |
| `POST` | `/saved-recipes` | Bearer | Save a recipe to user favorites |
| `DELETE` | `/saved-recipes/{id}` | Bearer | Remove a recipe from saved favorites |

### 7. Nutrition Tracker (`/nutrition`)

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/nutrition/daily` | Bearer | Retrieve daily logs and progress towards calorie/macro targets |
| `POST` | `/nutrition/log` | Bearer | Log a consumed meal entry |
| `PUT` | `/nutrition/log/{id}` | Bearer | Update an existing nutrition log entry |
| `DELETE` | `/nutrition/log/{id}` | Bearer | Delete a nutrition log entry |
| `POST` | `/nutrition/targets` | Bearer | Update daily calorie, protein, fat, and carbs targets |

### 8. Analytics (`/analytics`)

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/analytics` | Bearer | Aggregated summary of consumed vs wasted items and weekly trends |

### 9. AI Chef Chat (`/chat`)

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/chat` | Bearer | Retrieve chat history for the last 24 hours (up to 20 messages) |
| `POST` | `/chat` | Bearer | Send a message to AI Chef (rate-limited: 5 requests / 60 seconds) |
| `DELETE` | `/chat` | Bearer | Clear chat message history |

### 10. Metadata & Health

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/` | Public | Service metadata and version |
| `GET` | `/health` | Public | Health check validating database connectivity |
| `GET` | `/openapi/v1.json` | Public | OpenAPI 3 document (.NET 10 built-in) |

---

## Error Handling & Envelope

All non-validation errors return a standard JSON envelope:

```json
{
  "code": "EMAIL_NOT_VERIFIED",
  "error": "Email is not verified yet. Check your inbox or request a new email."
}
```

### Common Error Codes

| Code | Status | Meaning |
| --- | --- | --- |
| `EMAIL_EXISTS` | 409 Conflict | Email address is already registered |
| `EMAIL_NOT_VERIFIED` | 403 Forbidden | Account exists but email is unconfirmed |
| `BAD_CREDENTIALS` | 401 Unauthorized | Incorrect email or password |
| `REFRESH_INVALID` | 401 Unauthorized | Refresh token invalid, expired, or revoked |
| `TOKEN_EXPIRED` | 400 Bad Request | Verification or invite token has expired |
| `TOKEN_NOT_FOUND` | 404 Not Found | Verification or invite token does not exist |
| `PRODUCT_NOT_FOUND` | 404 Not Found | Product does not exist or caller lacks access |
| `FRIDGE_NOT_FOUND` | 404 Not Found | Fridge does not exist or caller lacks membership |
| `FORBIDDEN` | 403 Forbidden | Caller lacks required owner/member permissions |
| `RATE_LIMITED` | 429 Too Many Requests | Rate limit exceeded for chat (5/60s) or auth (10/60s) |

---

## Database and Migrations

The database is managed with Entity Framework Core 10 migrations located in `src/VFridge.Api/Migrations/`.

### Automatic Application

On application startup, `Program.cs` automatically executes pending migrations using:

```csharp
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();
    await db.Database.MigrateAsync();
}
```

### Managing Migrations CLI

```bash
# Add a new migration
dotnet ef migrations add <MigrationName> --project src/VFridge.Api

# Update local database manually
dotnet ef database update --project src/VFridge.Api

# Generate SQL script for production deployment
dotnet ef migrations script --project src/VFridge.Api -o migration.sql
```

---

## Running Locally

### 1. Requirements

* .NET 10 SDK
* PostgreSQL 15+ (or Docker)

### 2. Configure Environment

Create a `.env` file in the repository root or use user-secrets:

```bash
cd src/VFridge.Api
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=vfridge;Username=postgres;Password=postgres"
dotnet user-secrets set "Jwt:Secret" "super-secret-key-at-least-32-characters-long"
dotnet user-secrets set "OpenRouter:ApiKey" "sk-or-v1-..."
dotnet user-secrets set "VChef:InternalToken" "internal-service-secret"
dotnet user-secrets set "VChef:BaseUrl" "http://localhost:8085"
```

### 3. Build & Run

```bash
dotnet restore
dotnet build
dotnet run --project src/VFridge.Api
```

---

## Testing

The solution includes comprehensive unit and integration tests using xUnit, WebApplicationFactory, and in-memory test doubles.

```bash
# Run all tests
dotnet test

# Run tests with detailed logger
dotnet test --logger "console;verbosity=detailed"
```
