# KYC API Reference (DigiLocker Plugin)

This reference documents Textzy's tenant-scoped KYC API and the DigiLocker callback/webhook model.

## Billing Model (Credits)

- Default metric key: `digilockerKyc` (shown in UI as **KYC credits**)
- Credits consumed per successful verification:
  - Preferred: Platform Integration Catalog per-plugin `creditsPerSuccess` (for `digilocker-kyc`)
  - Fallback: platform setting `digilocker.creditsPerSuccess` (default `3`)
- Credits are consumed only when the session is marked `verified`.

## Authentication (Customer Integrations)

Textzy supports API-key auth for KYC endpoints.

Required headers:

- `X-Tenant-Slug: <tenantSlug>`
- `X-API-Key: <tenantApiKey>`
- `X-API-Secret: <tenantApiSecret>`

## Document Types (docTypes)

`docTypes` is a **business-level** list that your app uses to ask for specific document categories (for example PAN, Aadhaar, Driving License). Textzy passes these as a requester parameter to DigiLocker (configured via platform setting `digilocker.docTypeParamName`, default `req_doctype`).

Examples (common values you can use):

- `PAN`
- `AADHAAR`
- `DL`
- `RC`
- `PASSPORT`

Notes:
- DigiLocker availability depends on what the end-user has in DigiLocker and what your requester is allowed to access.
- `scope` is different: it is the OAuth scope (typically `files.issueddocs`) configured at platform level, not per request.

## Create KYC Session

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

Webhook behavior:
- If `webhookUrl` is omitted/empty, Textzy uses the tenant default `kycWebhookUrl` (configure once in **Integrations → Public API Access**).

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

Your app must redirect the end-user to `redirectUrl` to complete consent/login.

## Read Session Status + Result

`GET /api/kyc/sessions/{sessionId}`

Response fields:

- `status`: `created | redirected | verified | failed | expired`
- `requestedDocTypes`
- `result`: full DigiLocker payload snapshot (stored encrypted server-side)

## DigiLocker Callback

`GET /api/public/kyc/digilocker/callback?sessionId=...&code=...&state=...`

This endpoint is called by DigiLocker after the user completes consent/login. Textzy:

1. exchanges `code` for an access token (OAuth2 token endpoint)
2. fetches issued docs (configured `apiBaseUrl + issuedDocsPath`)
3. sets session status `verified` or `failed`
4. consumes DigiLocker credits on success
5. POSTs a webhook (if configured)
6. redirects end-user to `successRedirectUrl` / `failureRedirectUrl` (if configured)

## Platform OAuth Endpoints (Authorize URL / Token URL)

`authorizeUrl` and `tokenUrl` are **OAuth2 endpoints** for your DigiLocker requester app. They are platform-level because the platform owns the DigiLocker client credentials.

- You do not set them per API call.
- In Platform Settings → DigiLocker Master Config, you can leave these fields blank to use the default DigiLocker endpoints (recommended unless DigiLocker gives you different URLs for your environment).

## Webhook Payload

If `webhookUrl` is set on session creation, Textzy POSTs JSON:

```json
{
  "sessionId": "GUID",
  "tenantId": "GUID",
  "provider": "digilocker",
  "status": "verified",
  "ok": true,
  "customerRef": "user-123",
  "requestedDocTypes": ["pan", "dl"],
  "failureReason": "",
  "completedAtUtc": "2026-03-16T00:00:00Z",
  "result": {
    "provider": "digilocker",
    "fetchedAtUtc": "2026-03-16T00:00:00Z",
    "requestedDocTypes": ["pan", "dl"],
    "documentTypes": ["PAN", "DL"],
    "oauth": {
      "tokenExchangeStatus": 200,
      "token": { "token_type": "Bearer", "expires_in": 3600 }
    },
    "issuedDocsStatus": 200,
    "issuedDocs": { }
  }
}
```

Notes:
- access tokens are never included.
- If you want Textzy to download/store document files, we can extend the plugin once you confirm DigiLocker download endpoints for your requester setup.
