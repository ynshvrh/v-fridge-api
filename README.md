# V-Fridge API

ASP.NET Core Minimal API (.NET 10) for **V-Fridge** — the food management system with AI-chef suggestions and V-Chef microservice integration.

This service replaces the legacy in-process Next.js API routes and exposes a typed REST surface for products, fridges, shopping list, meal planning, nutrition tracking, saved recipes, chat, and authentication (JWT bearer + Google OAuth + email verification).

---

## Tech stack

* **Runtime:** .NET 10, ASP.NET Core Minimal API
* **Microservices:** V-Chef Integration (`IVChefClient`, `VChefWarmupService`)
* **ORM:** EF Core 10 (Npgsql) — schema owned by this repo via raw SQL migrations (`Migrations/*.sql`)
* **Auth:** Stateless JWT bearer + opaque refresh tokens (returned in JSON body, **no cookies** — public-API friendly), Google ID-token sign-in, email verification
* **Mail:** MailKit SMTP / Resend API
* **AI:** OpenRouter (OpenAI-compatible chat completions) + V-Chef recipe generator service
* **Docs:** Built-in OpenAPI at `/openapi/v1.json`
* **Health:** `/health` (DbContext check)

---

## Project layout

```
src/
└── VFridge.Api/
    ├── Configuration/       # Strongly-typed options (Jwt, Email, Cors, OpenRouter, VChef, …)
    ├── Contracts/           # DTOs (Auth, Product, Fridge, Shopping, MealPlan, SavedRecipes, VChef, Analytics, …)
    ├── Data/                # EF Core DbContext + entities
    │   └── Entities/
    ├── Endpoints/           # Minimal API Endpoint modules (9 modules)
    │   ├── AnalyticsEndpoints.cs
    │   ├── AuthEndpoints.cs
    │   ├── ChatEndpoints.cs
    │   ├── FridgeEndpoints.cs
    │   ├── MealPlanEndpoints.cs
    │   ├── NutritionEndpoints.cs
    │   ├── ProductsEndpoints.cs
    │   ├── SavedRecipeEndpoints.cs
    │   └── ShoppingEndpoints.cs
    ├── Infrastructure/      # SqlMigrator runner
    ├── Migrations/          # Raw SQL schema migrations (000_*.sql to 015_*.sql)
    ├── Services/            # Business services, OpenRouter AI, VChefClient & Warmup, Email, Auth
    ├── Program.cs           # App composition root
    ├── appsettings.json     # Defaults (no secrets)
    └── Properties/
        └── launchSettings.json
```

---

## V-Chef Microservice Integration

`v-fridge-api` integrates with the external **V-Chef** microservice to offload and accelerate AI recipe generation and meal planning workflows:

* **`IVChefClient` / `VChefClient`**: Typed HTTP client configured via `VChef:BaseUrl` (default: `https://v-chef.onrender.com`). Responsible for sending structured requests (`POST /api/v1/recipes/generate`) with user inventory and dietary preferences using `VChefGenerateRecipeRequest` and parsing responses (`VChefRecipeResponse`).
* **`VChefWarmupService`**: A `BackgroundService` hosted inside `VFridge.Api`. Upon web host startup, it waits 1 second and executes a non-blocking background ping (`GET /health`) against V-Chef. This ensures cold-started microservice containers (e.g. Render free tier) are pre-warmed before incoming user requests.
* **DTO Contracts**: Defined in `Contracts/VChefDtos.cs` for clean microservice boundary isolation.

---

## Configuration

All settings live under sections in `appsettings.json` and can be overridden via:

1. `appsettings.Development.json` (committed, no secrets)
2. **User secrets** in Development (`dotnet user-secrets`)
3. **A `.env` file** in the repo root (gitignored, auto-loaded via `DotNetEnv`) — see `.env.example`. Recommended for local.
4. Environment variables (`Jwt__Secret`, `ConnectionStrings__Default`, …) — for production
5. The libpq URI form `DATABASE_URL=postgresql://…` is auto-normalised at startup

### Required keys

| Section / key                              | Notes                                          |
| ------------------------------------------ | ---------------------------------------------- |
| `ConnectionStrings:Default`                | Npgsql or `postgresql://…` URI                 |
| `Jwt:Secret`                               | ≥32 char random (HS256 signing key)            |
| `Email:SmtpHost` / `Username` / `Password` | MailKit SMTP (e.g. Gmail app password)         |
| `Google:ClientId` / `ClientSecret`         | Google OAuth credentials                       |
| `OpenRouter:ApiKey`                        | OpenRouter API key (https://openrouter.ai)     |
| `OpenRouter:Model`                         | Single model id; used when `Models` is empty (e.g. `google/gemini-2.5-flash`) |
| `OpenRouter:Models`                        | Ordered model pool tried best-first; falls through on 429/402/5xx/empty |
| `OpenRouter:MaxTokens`                     | Per-call generation cap (default 2048)         |
| `Frontend:BaseUrl`                         | Used in email verification links + CORS origin |
| `Cors:AllowedOrigins`                      | Array of allowed front-end origins             |
| `VChef:BaseUrl`                            | Base URL for V-Chef microservice (`https://v-chef.onrender.com`) |

### Quick local setup

```bash
cd src/VFridge.Api
dotnet user-secrets set "ConnectionStrings:Default" "Host=…;Database=…;Username=…;Password=…;SslMode=Require"
dotnet user-secrets set "Jwt:Secret" "$(openssl rand -base64 48)"
dotnet user-secrets set "OpenRouter:ApiKey" "sk-or-v1-…"
dotnet user-secrets set "Email:Username" "…"
dotnet user-secrets set "Email:Password" "…"
dotnet user-secrets set "Google:ClientId" "…"
dotnet user-secrets set "Google:ClientSecret" "…"
dotnet user-secrets set "VChef:BaseUrl" "https://v-chef.onrender.com"
```

---

## Run

```bash
dotnet restore
dotnet build
dotnet run --project src/VFridge.Api
```

Defaults:

| Endpoint                            | Purpose                                       |
| ----------------------------------- | --------------------------------------------- |
| `GET /`                             | Service metadata                              |
| `GET /health`                       | Liveness + DB check                           |
| `GET /openapi/v1.json`              | OpenAPI 3 document                            |
| **Auth** (Token optional/required)  |                                               |
| `POST /auth/signup`                 | Create account + send verification email      |
| `POST /auth/login`                  | Email + password → JWT pair                   |
| `POST /auth/refresh`                | Rotate refresh token → new JWT pair           |
| `POST /auth/logout`                 | Revoke refresh token                          |
| `GET  /auth/verify-email?token=`    | Confirm email (HTML redirect to SPA)          |
| `POST /auth/verify-email`           | Confirm email (JSON API response)             |
| `POST /auth/resend-verification`    | Resend confirmation email                     |
| `POST /auth/google`                 | Sign in with a Google ID token                |
| `GET  /auth/me`                     | Current user info & settings                  |
| `PATCH /auth/me`                    | Update display name / profile                 |
| `PATCH /auth/me/preferences`        | Update language, cuisine, dietary preferences |
| `POST /auth/me/avatar`              | Upload user profile avatar image              |
| **Fridges** (Bearer required)       |                                               |
| `GET  /fridges`                     | List user's shared fridges                    |
| `POST /fridges`                     | Create a new shared fridge                    |
| `PATCH /fridges/{id}`               | Rename a fridge                               |
| `DELETE /fridges/{id}`              | Delete a fridge (owner only)                  |
| `DELETE /fridges/{id}/members/me`   | Leave a shared fridge                         |
| `POST /fridges/{id}/invites`        | Invite a user to a shared fridge              |
| `POST /fridges/accept`              | Accept a fridge invitation                    |
| **Products** (Bearer required)      |                                               |
| `GET  /products`                    | List active fridge items, ordered by expiry   |
| `POST /products`                    | Add a new product                             |
| `PATCH /products/{id}`              | Update product quantity, expiry date, category|
| `DELETE /products/{id}`             | Delete one product (logs as consumed/wasted)  |
| `DELETE /products`                  | Clear active fridge products                  |
| **Shopping List** (Bearer required) |                                               |
| `GET  /shopping`                    | List shopping items                           |
| `POST /shopping`                    | Add item to shopping list                     |
| `PATCH /shopping/{id}`              | Toggle checked state or update details        |
| `DELETE /shopping/{id}`             | Delete item from shopping list                |
| `POST /shopping/{id}/purchase`      | Purchase item (moves it to fridge inventory)  |
| **Meal Planner** (Bearer required)  |                                               |
| `GET  /meal-plan`                   | Get the weekly meal plan                      |
| `POST /meal-plan`                   | Generate a new weekly meal plan (via AI)      |
| `POST /meal-plan/regenerate-day`    | Regenerate plan for a specific day            |
| `POST /meal-plan/regenerate-meal`   | Regenerate plan for a specific single meal    |
| `POST /meal-plan/recipe`            | Retrieve full AI recipe instructions          |
| `POST /meal-plan/import-gaps`       | Import missing ingredients to shopping list   |
| **Saved Recipes** (Bearer required) |                                               |
| `GET  /saved-recipes`               | List user's saved recipes                     |
| `POST /saved-recipes`               | Save a recipe to user's favorites             |
| `DELETE /saved-recipes/{id}`        | Delete a saved recipe                         |
| **Nutrition Tracker** (Bearer req)  |                                               |
| `GET  /nutrition/daily`             | Retrieve daily logs and target progress       |
| `POST /nutrition/log`               | Log a meal consumed                           |
| `PUT  /nutrition/log/{id}`          | Edit a logged meal entry                      |
| `DELETE /nutrition/log/{id}`        | Delete a logged meal entry                    |
| `POST /nutrition/targets`           | Set daily calorie and macro targets           |
| **Analytics** (Bearer required)     |                                               |
| `GET  /analytics`                   | Get inventory and waste summaries             |
| **Chat** (Bearer required)          |                                               |
| `GET  /chat`                        | Last 24h, max 20 messages                     |
| `POST /chat`                        | Ask the AI chef (rate-limited 5/60s)          |
| `DELETE /chat`                      | Clear chat history                            |

Listens on `http://localhost:5080` (see `Properties/launchSettings.json`).

---

## Quick walkthrough (curl)

A minimal end-to-end signup → verify → login → products flow. Replace `BASE`, `EMAIL`, `PASSWORD`, and the verification token where shown.

```bash
BASE=http://localhost:5080

# 1. Sign up. The server emails a verification link.
curl -sS -X POST "$BASE/auth/signup" \
  -H 'Content-Type: application/json' \
  -d '{"username":"yanosh","email":"you@example.com","password":"hunter22!"}'

# 2. Login without verifying → 403 EMAIL_NOT_VERIFIED.
curl -sS -i -X POST "$BASE/auth/login" \
  -H 'Content-Type: application/json' \
  -d '{"email":"you@example.com","password":"hunter22!"}'

# 3. SPA exchanges token via POST /auth/verify-email.
curl -sS -X POST "$BASE/auth/verify-email" \
  -H 'Content-Type: application/json' \
  -d "{\"token\":\"<raw-token-from-email>\"}"

# 4. Login now returns a TokenPair.
ACCESS=$(curl -sS -X POST "$BASE/auth/login" \
  -H 'Content-Type: application/json' \
  -d '{"email":"you@example.com","password":"hunter22!"}' \
  | jq -r .accessToken)

# 5. Create a product.
curl -sS -X POST "$BASE/products" \
  -H "Authorization: Bearer $ACCESS" \
  -H 'Content-Type: application/json' \
  -d '{"name":"Milk","quantity":1,"unit":"l","expiryDate":"2026-06-01"}'

# 6. List products.
curl -sS "$BASE/products" -H "Authorization: Bearer $ACCESS"

# 7. Ask the chef.
curl -sS -X POST "$BASE/chat" \
  -H "Authorization: Bearer $ACCESS" \
  -H 'Content-Type: application/json' \
  -d '{"content":"What can I cook with what I have?"}'
```

### Error envelope

Every non-validation error returns the same JSON shape:

```json
{ "code": "EMAIL_NOT_VERIFIED", "error": "Email is not verified yet. Check your inbox or request a new email." }
```

Branch your client on `code` (stable, machine-readable). `error` is the English fallback message.

| Code | Where | Meaning |
| --- | --- | --- |
| `EMAIL_EXISTS` | `POST /auth/signup` | Address already registered. |
| `EMAIL_NOT_VERIFIED` | `POST /auth/login` | Account exists but the email is unconfirmed. |
| `BAD_CREDENTIALS` | `POST /auth/login` | Wrong email or password. |
| `REFRESH_INVALID` | `POST /auth/refresh` | Token unknown / expired / revoked. |
| `TOKEN_MISSING` / `TOKEN_NOT_FOUND` / `TOKEN_USED` / `TOKEN_EXPIRED` | `POST /auth/verify-email` | Email verification failure modes. |
| `GOOGLE_TOKEN_INVALID` / `GOOGLE_EMAIL_UNVERIFIED` | `POST /auth/google` | Google ID-token rejected. |
| `PRODUCT_NOT_FOUND` | `PATCH/DELETE /products/{id}` | No row for that id owned by the caller. |
| `RATE_LIMITED` | `POST /chat` | 6th call within the 60 s window. |

Validation errors (e.g. wrong DTO shape) come back as RFC 7807 `ProblemDetails` with an `errors` dictionary.

---

## Database

The schema lives in this repo as plain `.sql` migrations under `src/VFridge.Api/Migrations/`:

| File                                  | Purpose                                                                 |
| ------------------------------------- | ----------------------------------------------------------------------- |
| `000_initial.sql`                     | Base tables (`users`, `products`, `chat`)                              |
| `001_auth.sql`                        | Auth additions: email verification, refresh tokens, Google OAuth logins |
| `002_categories.sql`                  | Categorized inventory tables                                            |
| `003_shopping_items.sql`              | Custom shopping list structures                                         |
| `004_consumption_log.sql`             | Log for product consumption and waste tracking                          |
| `005_shared_fridges.sql`              | Multi-fridge sharing, owners, invitations, and membership               |
| `006_username_display_name.sql`       | User display name and username extensions                               |
| `007_user_preferred_language.sql`     | User localization language settings                                     |
| `008_user_cuisine_preference.sql`     | Cuisine preference presets for recipe generations                       |
| `009_shopping_items_fridge_id.sql`    | Associating shopping items with specific shared fridges                 |
| `010_meal_plans.sql`                  | Storing generated weekly meals                                          |
| `011_user_dietary_profile.sql`        | Storing custom dietary restrictions (e.g. Vegan, Keto)                  |
| `012_calorie_tracker.sql`             | Calorie and daily macro targets tracker tables                          |
| `013_user_avatar.sql`                 | Profile avatar image support                                            |
| `014_saved_recipes.sql`               | Saved recipe bookmarks & nutrition metadata                             |
| `015_consumption_log_fridge_id.sql`   | Fridge association for consumption logs                                 |

`Infrastructure/SqlMigrator.cs` is a tiny additive-migration runner: on every startup it picks up each `NNN_*.sql`, hashes its filename, and applies it once per database (tracked in `schema_migrations`).

---

## Branch / PR workflow

Each major feature lands via its own branch + PR (no direct commits to `main`):

| Branch                              | Scope                                                  |
| ----------------------------------- | ------------------------------------------------------ |
| `feat/bootstrap-api`                | Project skeleton, EF Core, OpenAPI, health             |
| `feat/products-chat-endpoints`      | Products CRUD, Chat (OpenRouter)                       |
| `feat/auth`                         | Stateless JWT + Google ID-token + email verification   |
| `feat-shared-fridges`               | Shared fridges, member permissions, invitations         |
| `feat-shopping-list`                | Shopping list endpoints & auto-purchase sync           |
| `feat-meal-plan`                    | AI weekly planner & ingredient gap imports             |
| `feat-nutrition-base`               | Daily calorie & macro tracking                         |
| `feat-saved-recipes`                | Saved recipe bookmarks API                             |
| `feat-vchef-integration`            | V-Chef microservice integration client & warmup ping   |
| `feat-analytics`                    | Inventory consumption & waste analytics                |
