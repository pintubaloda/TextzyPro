# Textzy - Multi-tenant WhatsApp + SMS SaaS

## Integration docs
- Simple API guide (SMS + WhatsApp + sandbox): `API_INTEGRATION_SIMPLE_GUIDE.md`
- Postman collection: `Textzy_API_Integration_Postman_Collection.json`
- Postman environment: `Textzy_API_Integration_Postman_Environment.json`

## Architecture status

Implemented now:
- Control-plane + tenant-plane DB split
- Dynamic tenant DB resolution per request
- Session auth with opaque token + refresh
- RBAC (`owner/admin/agent`)
- CRUD modules with optimistic UI updates

## Database setup (PostgreSQL)

### Start PostgreSQL
```bash
cd /Volumes/Rakesh/RBproject/Textzy
docker compose up -d postgres
```

### Backend run
```bash
cd /Volumes/Rakesh/RBproject/Textzy/backend-dotnet
dotnet restore
dotnet run
```

### Frontend run
```bash
cd /Volumes/Rakesh/RBproject/Textzy/frontend
npm ci
npm run dev
```

## How tenant DB routing works

- Request must include `X-Tenant-Slug`
- Tenant is looked up from control DB (`Tenants`)
- `TenancyContext` stores tenant DB connection string
- Domain controllers use `TenantDbContext` connected to that tenant DB

## Seeded credentials
- Email: `admin@textzy.local`
- Password: `ChangeMe@123`

## Current note
For local development, both demo tenants can point to same Postgres DB using tenant row `DataConnectionString`. For production, set unique connection strings per tenant for full DB-per-tenant isolation.

## Tenant provisioning API

Create a new tenant and dedicated tenant database:

```bash
curl -X POST http://localhost:5000/api/platform/tenants \
  -H 'Content-Type: application/json' \
  -H 'Authorization: Bearer <accessToken>' \
  -H 'X-Tenant-Slug: demo-retail' \
  -d '{"name":"Acme Commerce","slug":"acme-commerce"}'
```

What it does:
- Validates slug uniqueness in control DB
- Creates dedicated PostgreSQL database: `textzy_tenant_<slug>`
- Registers tenant in control plane with `DataConnectionString`
- Assigns requesting user as `owner` for the new tenant
- Initializes tenant DB schema/data seed

## Full Role Matrix + Permission Catalog

Roles:
- owner
- admin
- manager
- support
- marketing
- finance
- super_admin

Permission catalog:
- contacts.read / contacts.write
- campaigns.read / campaigns.write
- templates.read / templates.write
- automation.read / automation.write
- inbox.read / inbox.write
- billing.read / billing.write
- api.read / api.write
- platform.tenants.manage

APIs:
- `GET /api/auth/me` now returns `permissions`
- `GET /api/permissions/catalog` returns full roles + permissions catalog

Notes:
- API authorization is now permission-based (`HasPermission(...)`) instead of only coarse role checks.
- `super_admin` gets full catalog permissions.

## WhatsApp Cloud + Embedded Signup Integration

### Backend APIs added
- `GET /api/waba/status`
- `POST /api/waba/embedded-signup/exchange`
- `GET /api/waba/webhook` (Meta webhook verify)
- `POST /api/waba/webhook` (signature validation + inbound processing)

### 24-hour session rule
For `POST /api/messages/send` when `channel=WhatsApp`:
- If `useTemplate=false`, backend checks last inbound message window (`<= 24h`)
- If closed, API returns error: `24-hour WhatsApp session closed. Use template message.`
- If `useTemplate=true`, backend sends WhatsApp template message

### Backend config
Set in `/backend-dotnet/appsettings*.json` under `WhatsApp`:
- `AppId`
- `AppSecret`
- `VerifyToken`
- `GraphApiBase`
- `ApiVersion`
- `EmbeddedSignupConfigId`

### Frontend env (Vite)
Create frontend env values:
- `VITE_FACEBOOK_APP_ID`
- `VITE_WABA_EMBEDDED_CONFIG_ID`

WABA setup card now triggers embedded signup and code exchange via backend.

## Render Blueprint Deploy

Blueprint file:
- `/Volumes/Rakesh/RBproject/Textzy/render.yaml`

Services provisioned:
- `textzy-postgres` (managed Postgres)
- `textzy-backend` (.NET 8 web service via Docker)
- `textzy-frontend` (Vite static site)

Important Render env values (set `sync: false` values manually in Render):
- Backend:
  - `WhatsApp__AppId`
  - `WhatsApp__AppSecret`
  - `WhatsApp__VerifyToken`
  - `WhatsApp__EmbeddedSignupConfigId`
  - `AllowedOrigins` (frontend URL)
- Frontend:
  - `VITE_API_BASE` (backend public URL)
  - `VITE_FACEBOOK_APP_ID`
  - `VITE_WABA_EMBEDDED_CONFIG_ID`

Notes:
- Backend CORS is enabled from `AllowedOrigins` (comma-separated if multiple).
- SPA routing is handled by Render rewrite `/* -> /index.html` in blueprint.
