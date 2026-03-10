# Textzy Windows Desktop Shell

## Run
1. `cd desktop-windows`
2. `npm install`
3. `npm start`

## Build Installer
1. `npm run dist`
2. Output is generated under `desktop-windows/dist/`

## Notes
- This app opens the dedicated desktop shell with platform flag:
  - `https://textzy-frontend-production.up.railway.app/?desktopShell=1&platform=windows`
- Node integration is disabled.
- Request host allow-list is enforced in `main.js`.
