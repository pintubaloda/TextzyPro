# Textzy iOS Shell

This is a Capacitor iOS wrapper for the same inbox runtime used in Android/mobile shell.

## Setup
1. `cd mobile-ios`
2. `npm install`
3. `npm run sync`
4. `npm run open`

## Runtime URL
- `https://textzy-frontend-production.up.railway.app/?mobileShell=1&platform=ios`

## Required iOS capabilities
- Camera permission (QR scan)
- Push notifications (APNs/FCM bridge if enabled)
- Background modes (if you enable realtime push handlers)
