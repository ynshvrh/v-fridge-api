# V-Fridge API

ASP.NET Core Minimal API (.NET 10) for **V-Fridge** — the food management system with AI-chef suggestions.

This service replaces the in-process Next.js API routes and exposes a typed REST surface for products, chat, and authentication (JWT cookies + Google OAuth + email verification).

---

## Tech stack

* **Runtime:** .NET 10, ASP.NET Core Minimal API
* **ORM:** EF Core 10 (Npgsql) — **DB-first**, models scaffolded from the existing Neon Postgres schema
* **Auth:** Stateless JWT bearer + opaque refresh tokens (returned in JSON body, **no cookies** — public-API friendly), Google ID-token sign-in, email verification
* **Mail:** MailKit SMTP (Gmail-style auth)
* **AI:** OpenRouter (OpenAI-compatible chat completions, configurable model)
* **Docs:** Built-in OpenAPI at `/openapi/v1.json`
* **Health:** `/health` (DbContext check)

---

## Project layout

```
src/
└── VFridge.Api/
    ├── Configuration/       # Strongly-typed options (Jwt, Email, Cors, …)
    ├── Data/                # EF Core DbContext + entities (scaffolded)
    │   └── Entities/
    ├── Program.cs           # App composition root
    ├── appsettings.json     # Defaults (no secrets)
    └── Properties/
        └── launchSettings.json
```

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
| `OpenRouter:Model`                         | Default model id (e.g. `openai/gpt-4o-mini`)   |
| `Frontend:BaseUrl`                         | Used in email verification links + CORS origin |
| `Cors:AllowedOrigins`                      | Array of allowed front-end origins             |

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
| **Auth**                            |                                               |
| `POST /auth/signup`                 | Create account + send verification email      |
| `POST /auth/login`                  | Email + password → JWT pair                   |
| `POST /auth/refresh`                | Rotate refresh token → new JWT pair           |
| `POST /auth/logout`                 | Revoke refresh token                          |
| `GET  /auth/verify-email?token=`    | Confirm email (redirects to `Frontend:BaseUrl`) |
| `POST /auth/resend-verification`    | Resend confirmation email (silent on unknown email) |
| `POST /auth/google`                 | Sign in with a Google ID token                |
| `GET  /auth/me`                     | Current user (requires Bearer token)          |
| **Products** (Bearer required)      |                                               |
| `GET  /products`                    | List owned, ordered by expiry                 |
| `POST /products`                    | Create                                        |
| `PATCH /products/{id}`              | Partial update                                |
| `DELETE /products/{id}`             | Delete one                                    |
| `DELETE /products`                  | Delete all owned                              |
| **Chat** (Bearer required)          |                                               |
| `GET  /chat`                        | Last 24h, max 20 messages                     |
| `POST /chat`                        | Ask the AI chef (rate-limited 5/60s)          |
| `DELETE /chat`                      | Clear history                                 |

Listens on `http://localhost:5080` (see `Properties/launchSettings.json`).

---

## Database

The schema is owned by Drizzle in the [`V-Fridge`](https://github.com/ynshvrh/V-Fridge) Next.js repo. EF Core consumes the same Neon Postgres database — no migrations are produced here.

To re-scaffold after Drizzle changes:

```bash
cd src/VFridge.Api
dotnet ef dbcontext scaffold "<Npgsql connection string>" \
  Npgsql.EntityFrameworkCore.PostgreSQL \
  --output-dir Data/Entities --context-dir Data --context VFridgeDbContext --force --no-onconfiguring
```

---

## Branch / PR workflow

Each major chunk lands via its own branch + PR (no direct commits to `main`):

| Branch                              | Scope                                                  |
| ----------------------------------- | ------------------------------------------------------ |
| `feat/bootstrap-api`                | Project skeleton, EF Core, OpenAPI, health             |
| `feat/products-chat-endpoints`      | Products CRUD, Chat (OpenRouter)                       |
| `feat/auth`                         | Stateless JWT + Google ID-token + email verification   |
