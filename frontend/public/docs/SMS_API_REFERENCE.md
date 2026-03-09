# Textzy SMS API Reference

Base URL:
- `https://textzy-backend-production.up.railway.app`

## 1. Public SMS API

Use this API when your ERP, CRM, website, or backend wants to send SMS directly through Textzy with tenant-specific API credentials.

Endpoints:
- `GET /api/public/messages/send`
- `POST /api/public/messages/send`

## 2. GET Example

```text
GET https://textzy-backend-production.up.railway.app/api/public/messages/send
  ?recipient=919999999999
  &msg=Your approved DLT message text
  &user=MONEYART
  &pswd=YOUR_PASSWORD
  &apikey=YOUR_API_KEY
  &channel=sms
  &sender=MNYART
  &PE_ID=1601100000000006533
  &Template_ID=1207171593687982329
  &tenantSlug=moneyart
```

## 3. POST Example

```json
{
  "recipient": "919999999999",
  "message": "Your approved DLT message text",
  "user": "MONEYART",
  "password": "YOUR_PASSWORD",
  "apiKey": "YOUR_API_KEY",
  "tenantSlug": "moneyart",
  "channel": "sms",
  "sender": "MNYART",
  "peId": "1601100000000006533",
  "templateId": "1207171593687982329",
  "idempotencyKey": "sms-20260308-0001"
}
```

## 4. Required Fields

- `recipient`: mobile number with country code
- `message` or `msg`: exact approved SMS text
- `tenantSlug`: target tenant slug
- `user`: tenant API username
- `password` or `pswd`: tenant API password
- `apiKey` or `apikey`: tenant API key
- `sender`: approved sender ID
- `peId` or `PE_ID`: approved entity ID
- `templateId` or `Template_ID`: approved template ID

## 5. Optional Fields

- `idempotencyKey`
- `channel`

## 6. Security Model

- every tenant has its own API username, password, and API key
- `tenantSlug` is mandatory
- credentials are validated against that tenant only
- optional IP whitelist can be applied per tenant
- HTTPS is required

## 7. Success Response

```json
{
  "jobId": "5d64f8bf-4c1f-4e59-92a9-4a0f5a8c992e",
  "message": "Accepted"
}
```

## 8. Error Response

```json
{
  "message": "Invalid authorization.",
  "code": "401"
}
```

Common codes:
- `400` request rejected
- `401` invalid authorization
- `403` access denied
- `422` missing DLT field
- `429` rate limit exceeded
- `503` gateway temporarily unavailable

## 9. DLT Rules

For India SMS traffic, the following must match approved DLT data:

- sender ID
- entity ID / `PE_ID`
- template ID / `Template_ID`
- final SMS text

## 10. Template Registry

Endpoints:
- `GET /api/sms/templates`
- `POST /api/sms/templates`
- `PUT /api/sms/templates/{id}`
- `DELETE /api/sms/templates/{id}`
- `POST /api/sms/templates/{id}/status`
- `POST /api/sms/templates/import-approved-csv`

## 11. Sender Registry

Endpoints:
- `GET /api/sms/senders`
- `GET /api/sms/senders/stats`
- `POST /api/sms/senders`
- `PUT /api/sms/senders/{id}`
- `DELETE /api/sms/senders/{id}`

## 12. Delivery Reporting

Tenant SMS reports and delivery ledgers show message progress such as:

- `accepted`
- `queued`
- `sent`
- `delivered`
- `failed`
- `rejected`

## 13. Segment Logic

- English GSM:
  - `1-160` characters = 1 SMS
  - multipart uses `153` characters per segment
- Unicode / regional:
  - `1-70` characters = 1 SMS
  - multipart uses `67` characters per segment
