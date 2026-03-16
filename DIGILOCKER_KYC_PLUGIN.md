# DigiLocker KYC Plugin (Textzy)

This document describes how to configure and use Textzy's DigiLocker KYC plugin, including the public API that your customers can integrate with.

## Overview

Textzy implements DigiLocker KYC as a plugin-style provider:

1. Your customer calls Textzy to create a KYC session.
2. Your customer redirects the end-user to DigiLocker consent/login (OAuth2).
3. DigiLocker redirects back to Textzy callback.
4. Textzy fetches the user's DigiLocker documents (issued docs) and marks the session `verified` or `failed`.
5. Textzy charges 1 unit (`digilockerKyc`) on successful verification.

## Billing (Per-Use Charging)

Metric key: `digilockerKyc`

Credits per successful KYC:

- Config key: Platform Settings scope `digilocker`, `creditsPerSuccess`
- Default: `3`

You can sell DigiLocker KYC as a prepaid usage pack using Billing Plans:

- Create a Billing Plan with:
  - `pricingModel = usage_pack`
  - `usageUnitName = digilockerKyc`
  - `includedQuantity = <credits>`
  - price (INR) as you want

When a KYC session is verified, Textzy consumes 1 unit from:

1. Prepaid credit balance (if any)
2. Remaining subscription allowance (if your subscription plan includes a monthly limit for `digilockerKyc`)

## Platform Configuration (Credentials)

Store DigiLocker credentials in Platform Settings scope `digilocker`:

Endpoint: `PUT /api/platform/settings/digilocker`

Keys:

- `clientId`: DigiLocker OAuth client id
- `clientSecret`: DigiLocker OAuth client secret
- `redirectUri`: must match DigiLocker configured redirect URI
- `authorizeUrl`: default `https://digilocker.meripehchaan.gov.in/public/oauth2/1/authorize`
- `tokenUrl`: default `https://digilocker.meripehchaan.gov.in/public/oauth2/1/token`
- `apiBaseUrl`: default `https://digilocker.meripehchaan.gov.in/public/oauth2/1`
- `scope`: default `files.issueddocs`
- `docTypeParamName`: default `req_doctype` (optional; used to request specific document types)
- `issuedDocsPath`: default `/files/issued`
- `creditsPerSuccess`: how many credits to consume per successful KYC (default `3`)

All values are encrypted at rest in the control DB.

## Tenant API (Browser / Dashboard users)

Base path: `/api/kyc`

### Create session

`POST /api/kyc/sessions`

Body:
```json
{
  "provider": "digilocker",
  "customerRef": "user-123",
  "docTypes": ["PAN", "DL"],
  "successRedirectUrl": "https://yourapp.com/kyc/success",
  "failureRedirectUrl": "https://yourapp.com/kyc/failure",
  "webhookUrl": "https://yourapp.com/webhooks/textzy-kyc"
}
```

Response:
```json
{
  "sessionId": "GUID",
  "provider": "digilocker",
  "status": "created",
  "redirectUrl": "https://digilocker.../authorize?...",
  "state": "..."
}
```

### Get session

`GET /api/kyc/sessions/{sessionId}`

### List sessions

`GET /api/kyc/sessions?take=50`

## Customer API (API Key Authentication)

Your SaaS customers can use the same endpoints using API key headers:

Required headers:

- `X-Tenant-Slug: <tenantSlug>`
- `X-API-Key: <tenantApiKey>`
- `X-API-Secret: <tenantApiSecret>`

Example:
```bash
curl -X POST "https://api.textzy.in/api/kyc/sessions" \
  -H "Content-Type: application/json" \
  -H "X-Tenant-Slug: moneyart" \
  -H "X-API-Key: ***" \
  -H "X-API-Secret: ***" \
  -d '{"provider":"digilocker","customerRef":"u-1","docTypes":["PAN"]}'
```

## Public Callback

`GET /api/public/kyc/digilocker/callback?sessionId=...&code=...&state=...`

This endpoint is called by DigiLocker after the end-user completes consent/login.

Textzy will:

1. Validate `state` (fixed-time compare)
2. Exchange `code` for access token
3. Fetch issued documents (`apiBaseUrl + issuedDocsPath`)
4. Mark the session verified/failed
5. Consume `creditsPerSuccess` units of `digilockerKyc` on success (default `3`)
6. Call `webhookUrl` (if provided)
7. Redirect the end-user to `successRedirectUrl` or `failureRedirectUrl` (if provided), and append:
   - `sessionId`
   - `status`

## Notes / Limitations

- This implementation stores the provider result encrypted. It does not yet download and store document PDFs/XML; it stores the issued docs payload and doc type list. If you want full document downloads inside Textzy, we will extend the provider with provider-specific download endpoints once confirmed in your DigiLocker integration docs.
