# Windows Server GitHub Deployment

This deployment model uses:

- GitHub-hosted runners to build backend and frontend artifacts
- a self-hosted GitHub Actions runner on your Windows Server to deploy those artifacts into IIS

It gives you Railway-style auto-deploy behavior without building on the production server.

## 1. Target topology

- Frontend IIS site:
  - `https://app.yourdomain.com`
  - physical path example: `C:\inetpub\textzy-frontend`
- Backend IIS site:
  - `https://api.yourdomain.com`
  - physical path example: `C:\inetpub\textzy-backend`
- PostgreSQL:
  - same server or separate DB server
- Optional:
  - Redis for production queue/cache

## 2. Server requirements

- Windows Server 2022 preferred
- IIS installed
- ASP.NET Core Hosting Bundle for .NET 8 installed
- Web Deploy not required
- GitHub Actions self-hosted runner installed on the server
- WebAdministration PowerShell module available

## 3. IIS prerequisites

Create two IIS sites before first deploy.

### Frontend site

- Site name: `textzy-frontend`
- Path: `C:\inetpub\textzy-frontend`
- Host binding: `app.yourdomain.com`
- HTTPS enabled

### Backend site

- Site name: `textzy-backend`
- Path: `C:\inetpub\textzy-backend`
- Host binding: `api.yourdomain.com`
- HTTPS enabled
- App pool: `No Managed Code`

## 4. Install self-hosted runner

Install the runner on the Windows Server and add labels:

- `self-hosted`
- `windows`
- `textzy-deploy`

The workflow in `.github/workflows/deploy-windows-artifacts.yml` expects those labels.

## 5. GitHub repository configuration

### Repository variables

Add these GitHub repository variables:

- `REACT_APP_API_BASE`
- `REACT_APP_FACEBOOK_APP_ID`
- `REACT_APP_WABA_EMBEDDED_CONFIG_ID`

These are used during frontend build.

### Repository secrets

Add these GitHub repository secrets:

- `TEXTZY_FRONTEND_PATH`
  - example: `C:\inetpub\textzy-frontend`
- `TEXTZY_BACKEND_PATH`
  - example: `C:\inetpub\textzy-backend`
- `TEXTZY_FRONTEND_SITE_NAME`
  - example: `textzy-frontend`
- `TEXTZY_BACKEND_SITE_NAME`
  - example: `textzy-backend`
- `TEXTZY_FRONTEND_APP_POOL`
  - optional
- `TEXTZY_BACKEND_APP_POOL`
  - example: `textzy-backend`

## 6. Backend environment configuration

Set these on the Windows Server for the backend app pool or machine environment:

- `ASPNETCORE_ENVIRONMENT=Production`
- `ConnectionStrings__Default=Host=...;Port=5432;Database=...;Username=...;Password=...`
- `AllowedOrigins=https://app.yourdomain.com`
- `WhatsApp__AppId=...`
- `WhatsApp__AppSecret=...`
- `WhatsApp__VerifyToken=...`
- `WhatsApp__EmbeddedSignupConfigId=...`

Recommended production additions:

- `Redis__ConnectionString=...`
- queue provider configuration
- push notification configuration
- SMTP or Resend outbound config if not fully DB-managed yet

## 7. Deployment flow

On push to `main`:

1. GitHub builds backend artifact on Ubuntu runner
2. GitHub builds frontend artifact on Ubuntu runner
3. Artifacts are uploaded inside the workflow
4. Windows self-hosted runner downloads both artifacts
5. `scripts/deploy-windows-artifact.ps1`:
   - stages a release copy
   - writes `app_offline.htm` for backend
   - mirrors frontend/backend into IIS target folders
   - removes `app_offline.htm`
   - restarts configured IIS sites/app pools

## 8. First deployment checklist

1. Install IIS
2. Install ASP.NET Core Hosting Bundle
3. Create IIS sites and app pools
4. Install GitHub self-hosted runner
5. Add runner labels: `windows`, `textzy-deploy`
6. Set repository variables and secrets
7. Ensure backend environment variables are configured on server
8. Push to `main`

## 9. Rollback strategy

The deploy script stores staged copies under a sibling `releases` folder.

Recommended rollback:

1. stop/restart app pool
2. copy an earlier release back into the live IIS folder
3. restart site/app pool

If you want zero-risk rollback, extend the script later to switch IIS physical paths to versioned folders instead of mirroring live directories.

## 10. Operational notes

- Keep PostgreSQL and Redis private
- Do not expose backend directly without HTTPS
- Enable IIS WebSockets for SignalR
- Keep server clock synced
- Restrict RDP by IP if possible

## 11. Files added for this deployment model

- `.github/workflows/deploy-windows-artifacts.yml`
- `scripts/deploy-windows-artifact.ps1`

