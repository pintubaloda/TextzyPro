# Textzy Unified API Reference (SMS / WhatsApp / KYC)

This page is a single, simple reference for SMS, WhatsApp, and KYC.

Base URL: `https://api.textzy.in`

## Authentication (Public / SMS-style)
Use these fields in query params or JSON body:
- `tenantSlug`
- `user`
- `pswd` (or `password`)
- `apikey` (or `apiKey`)

## 1) Send SMS

Endpoint: `GET /api/public/messages/send` or `POST /api/public/messages/send`

### GET example
```http
GET https://api.textzy.in/api/public/messages/send?tenantSlug=moneyart&user=MONEYART&pswd=YOUR_PASSWORD&apikey=YOUR_API_KEY&channel=sms&recipient=919999999999&msg=Your%20approved%20DLT%20message&sender=MNYART&PE_ID=1601100000000006533&Template_ID=1207171593687982329
```

### POST example
```json
{
  "tenantSlug": "moneyart",
  "user": "MONEYART",
  "password": "YOUR_PASSWORD",
  "apiKey": "YOUR_API_KEY",
  "channel": "sms",
  "recipient": "919999999999",
  "message": "Your approved DLT message",
  "sender": "MNYART",
  "peId": "1601100000000006533",
  "templateId": "1207171593687982329",
  "idempotencyKey": "sms-20260317-0001"
}
```

### SMS response (accepted)
```json
{
  "jobId": "GUID",
  "message": "Accepted"
}
```

## 2) Send WhatsApp (Simple Text)

Endpoint: `GET /api/public/messages/send` or `POST /api/public/messages/send`

### GET example
```http
GET https://api.textzy.in/api/public/messages/send?tenantSlug=moneyart&user=MONEYART&pswd=YOUR_PASSWORD&apikey=YOUR_API_KEY&channel=whatsapp&recipient=919999999999&msg=Hello%20from%20Textzy
```

### POST example
```json
{
  "tenantSlug": "moneyart",
  "user": "MONEYART",
  "password": "YOUR_PASSWORD",
  "apiKey": "YOUR_API_KEY",
  "channel": "whatsapp",
  "recipient": "919999999999",
  "message": "Hello from Textzy WhatsApp API",
  "idempotencyKey": "wa-20260317-0001"
}
```

### WhatsApp response (accepted)
```json
{
  "jobId": "GUID",
  "message": "Accepted"
}
```

## 3) KYC - DigiLocker (Redirect Flow)

### Start session
Endpoint: `GET /api/public/kyc/sessions/start`

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

Redirect the user to `redirectUrl`.

### Get session result
Endpoint: `GET /api/public/kyc/sessions/{sessionId}`

```http
GET https://api.textzy.in/api/public/kyc/sessions/{sessionId}?tenantSlug=moneyart&user=MONEYART&pswd=YOUR_PASSWORD&apikey=YOUR_API_KEY
```

## 4) KYC - GST Verification (Instant)

### Start session (no redirect)
Endpoint: `GET /api/public/kyc/sessions/start`

```http
GET https://api.textzy.in/api/public/kyc/sessions/start?tenantSlug=moneyart&user=MONEYART&pswd=YOUR_PASSWORD&apikey=YOUR_API_KEY&provider=gst&docType=GST&gstNo=03DOXPM4071K1ZE&customerRef=test-001
```

GST verification completes immediately. Use the same session result API above.

### GST response (example)
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

## 5) Download KYC File

Endpoint: `GET /api/public/kyc/sessions/{sessionId}/file`

```http
GET https://api.textzy.in/api/public/kyc/sessions/{sessionId}/file?tenantSlug=moneyart&user=MONEYART&pswd=YOUR_PASSWORD&apikey=YOUR_API_KEY&uri=in.gov.pan-PANCR-XXXX
```

## Webhook (Optional)
If `webhookUrl` is passed when creating a KYC session, Textzy will POST the session result after verification.
