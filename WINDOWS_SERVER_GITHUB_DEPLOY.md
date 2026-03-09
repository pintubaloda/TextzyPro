# Windows Server GitHub Deployment

This deployment model uses:

- GitHub-hosted Windows runners to build backend and frontend artifacts
- a self-hosted GitHub Actions runner on your Windows Server to deploy those artifacts into IIS

It gives you GitHub-driven auto-deploy behavior without building on the production server.

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

The workflow in `.github/workflows/deploy-windows-artifacts.yml` now matches the default Windows self-hosted runner labels.

## 5. GitHub repository configuration

### Repository variables

Add these GitHub repository variables if you use them:

- `REACT_APP_FACEBOOK_APP_ID`
- `REACT_APP_WABA_EMBEDDED_CONFIG_ID`

Frontend API base and IIS target paths are already committed in the production workflow for the current server layout.

## 6. Backend environment configuration

Do not commit backend secrets into the repository. Keep them on the Windows Server in a local script:

- target path: `C:\Secure\textzy-backend-env.ps1`
- template: `scripts/backend-env.template.ps1`

Example server-local script contents:

```powershell
[System.Environment]::SetEnvironmentVariable("PGHOST", "YOUR_DB_HOST", "Machine")
[System.Environment]::SetEnvironmentVariable("PGPORT", "5432", "Machine")
[System.Environment]::SetEnvironmentVariable("PGDATABASE", "YOUR_DB_NAME", "Machine")
[System.Environment]::SetEnvironmentVariable("PGUSER", "YOUR_DB_USER", "Machine")
[System.Environment]::SetEnvironmentVariable("PGPASSWORD", "YOUR_DB_PASSWORD", "Machine")
[System.Environment]::SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production", "Machine")
[System.Environment]::SetEnvironmentVariable("AllowedOrigins", "https://textzy.in", "Machine")
[System.Environment]::SetEnvironmentVariable("Secrets__MasterKey", "YOUR_MASTER_KEY", "Machine")
```

The deploy script now invokes `C:\Secure\textzy-backend-env.ps1` automatically on every deploy before the backend site/app pool is started again.

Recommended production additions in that same server-local script:

- `Redis__ConnectionString=...`
- queue provider configuration
- push notification configuration
- SMTP or Resend outbound config if not fully DB-managed yet
- WhatsApp credentials

Lock down the `C:\Secure` folder so only administrators can read it.

## 7. Deployment flow

On push to `main`:

1. GitHub builds backend artifact on Windows runner
2. GitHub builds frontend artifact on Windows runner
3. Artifacts are uploaded inside the workflow
4. Windows self-hosted runner downloads both artifacts
5. `scripts/deploy-windows-artifact.ps1`:
   - stages a release copy
   - writes `app_offline.htm` for backend
   - mirrors frontend/backend into IIS target folders
   - reapplies backend environment from `C:\Secure\textzy-backend-env.ps1`
   - removes `app_offline.htm`
   - restarts configured IIS sites/app pools

## 8. First deployment checklist

1. Install IIS
2. Install ASP.NET Core Hosting Bundle
3. Create IIS sites and app pools
4. Install GitHub self-hosted runner
5. Confirm the runner is online with `self-hosted` and `Windows` labels
6. Set optional repository variables
7. Create `C:\Secure\textzy-backend-env.ps1` from `scripts/backend-env.template.ps1`
8. Ensure backend environment variables script contains your real server values
9. Push to `main`

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

