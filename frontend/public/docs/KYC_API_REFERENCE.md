# KYC API Reference (DigiLocker Plugin)

This reference documents Textzy's tenant-scoped KYC API and the DigiLocker callback/webhook model.

## Billing Model (Credits)

- Metric key: `digilockerKyc`
- Credits consumed per successful verification:
  - Platform setting: scope `digilocker`, key `creditsPerSuccess`
  - Default: `3`
- Credits are consumed only when the session is marked `verified`.

## Authentication (Customer Integrations)

Textzy supports API-key auth for KYC endpoints.

Required headers:

- `X-Tenant-Slug: <tenantSlug>`
- `X-API-Key: <tenantApiKey>`
- `X-API-Secret: <tenantApiSecret>`

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

