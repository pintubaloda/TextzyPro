# KYC API (Simple)

This is the **simple, SMS-style** DigiLocker KYC API for testing.

Base URL: `https://api.textzy.in`

Auth fields (same as SMS):
- `tenantSlug`
- `user`
- `pswd` (or `password`)
- `apikey` (or `apiKey`)

## 1) Start KYC Session (GET)

```http
GET https://api.textzy.in/api/public/kyc/sessions/start?tenantSlug=moneyart&user=MONEYART&pswd=YOUR_PASSWORD&apikey=YOUR_API_KEY&provider=digilocker&docType=AADHAAR&customerRef=test-001&successRedirectUrl=https%3A%2F%2Fyourapp.com%2Fkyc%2Fsuccess&failureRedirectUrl=https%3A%2F%2Fyourapp.com%2Fkyc%2Ffailed
```

Response:
```json
{
  "sessionId": "GUID",
  "provider": "digilocker",
  "status": "created",
  "docType": "AADHAAR",
  "redirectUrl": "https://digilocker.../authorize?...",
  "state": "..."
}
```

Your app must redirect the user to `redirectUrl`.

### GST Verification (no redirect)

```http
GET https://api.textzy.in/api/public/kyc/sessions/start?tenantSlug=moneyart&user=MONEYART&pswd=YOUR_PASSWORD&apikey=YOUR_API_KEY&provider=gst&docType=GST&gstNo=03DOXPM4071K1ZE&customerRef=test-001
```

GST verification completes immediately. You can read the result using the session status API.

## 2) Get Session Status + Result (GET)

```http
GET https://api.textzy.in/api/public/kyc/sessions/{sessionId}?tenantSlug=moneyart&user=MONEYART&pswd=YOUR_PASSWORD&apikey=YOUR_API_KEY
```

Response (simple fields only):
```json
{
  "sessionId": "GUID",
  "provider": "digilocker",
  "status": "verified",
  "customerRef": "test-001",
  "docTypes": ["aadhaar"],
  "failureReason": "",
  "createdAtUtc": "2026-03-17T07:25:49Z",
  "updatedAtUtc": "2026-03-17T07:26:29Z",
  "completedAtUtc": "2026-03-17T07:26:29Z",
  "result": {
    "provider": "digilocker",
    "fetchedAtUtc": "2026-03-17T07:26:29Z",
    "requestedDocTypes": ["aadhaar"],
    "documentTypes": ["DRVLC","PANCR"],
    "user": {
      "digilockerId": "xxxx",
      "name": "Rakesh Kumar",
      "dob": "15-01-1986",
      "gender": "Male",
      "email": "bmrjjn@gmail.com",
      "mobile": "9460943374",
      "address": "..."
    },
    "panNo": "BEQPK9277N",
    "aadhaarNo": "123412341234",
    "dlNo": "RJ1820120000536",
    "documents": [
      {
        "uri": "textzy/aadhaar-report",
        "doctype": "AADHAAR_REPORT",
        "name": "Textzy Aadhaar Verification Report",
        "mime": "text/html; charset=utf-8",
        "sizeBytes": 12345,
        "downloadUrl": "https://api.textzy.in/api/public/kyc/sessions/{sessionId}/file?uri=textzy%2Faadhaar-report"
      }
    ]
  }
}
```

GST response example:
```json
{
  "sessionId": "GUID",
  "provider": "gst",
  "status": "verified",
  "customerRef": "test-001",
  "docTypes": ["gst"],
  "failureReason": "",
  "createdAtUtc": "2026-03-17T07:25:49Z",
  "updatedAtUtc": "2026-03-17T07:26:29Z",
  "completedAtUtc": "2026-03-17T07:26:29Z",
  "result": {
    "provider": "gst",
    "fetchedAtUtc": "2026-03-17T07:26:29Z",
    "gstNo": "03DOXPM4071K1ZE",
    "error": false,
    "message": "",
    "taxpayerInfo": { }
  }
}
```

## 3) Download a File from Textzy (GET)

```http
GET https://api.textzy.in/api/public/kyc/sessions/{sessionId}/file?tenantSlug=moneyart&user=MONEYART&pswd=YOUR_PASSWORD&apikey=YOUR_API_KEY&uri=in.gov.pan-PANCR-XXXX
```

This returns the file as PDF/HTML using your server credentials (no base64 in the response).

## Webhook (Optional)

If `webhookUrl` is passed when creating the session, Textzy will POST the same response after DigiLocker completes.

## Header‑Based API (If you want it)

Headers:
- `X-Tenant-Slug`
- `X-API-Key`
- `X-API-Secret`

Endpoints:
- `POST /api/kyc/sessions`
- `GET /api/kyc/sessions/{sessionId}`
