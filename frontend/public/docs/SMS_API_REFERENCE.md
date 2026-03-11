# Textzy SMS API Reference

## Overview
Textzy SMS API lets each tenant send DLT-compliant SMS through tenant-scoped credentials. Public API requests stay simple while Textzy handles provider routing, audit logging, compliance checks, and delivery reporting.

Base URL: `https://api.textzy.in`

Authentication:
- `tenantSlug`
- `user`
- `password` or `pswd`
- `apiKey` or `apikey`

Supported request formats:
- `GET /api/public/messages/send`
- `POST /api/public/messages/send`

## Public Send Endpoint

### GET
```http
GET https://api.textzy.in/api/public/messages/send?recipient=919999999999&msg=Your approved DLT message text&user=MONEYART&pswd=YOUR_PASSWORD&apikey=YOUR_API_KEY&channel=sms&sender=MNYART&PE_ID=1601100000000006533&Template_ID=1207171593687982329&tenantSlug=moneyart
```

### POST
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
  "idempotencyKey": "sms-20260311-0001"
}
```

## Request Fields
| Field | Required | Description |
|---|---|---|
| `recipient` | Yes | Mobile number with country code |
| `message` / `msg` | Yes | Exact approved SMS text |
| `tenantSlug` | Yes | Tenant slug that owns the credentials |
| `user` | Yes | Tenant API username |
| `password` / `pswd` | Yes | Tenant API password |
| `apiKey` / `apikey` | Yes | Tenant API key |
| `channel` | Yes | Must be `sms` |
| `sender` | DLT | Approved sender ID |
| `peId` / `PE_ID` | DLT | Approved entity ID |
| `templateId` / `Template_ID` | DLT | Approved template ID |
| `idempotencyKey` | No | Recommended deduplication key |

## Responses
### Accepted
```json
{
  "jobId": "5d64f8bf-4c1f-4e59-92a9-4a0f5a8c992e",
  "message": "Accepted"
}
```

### Error
```json
{
  "message": "Invalid authorization.",
  "code": "401"
}
```

## Delivery Statuses
- `accepted`
- `queued` / `sent`
- `delivered`
- `failed` / `rejected`

## Registry Endpoints
- `GET /api/sms/templates`
- `POST /api/sms/templates`
- `POST /api/sms/templates/import-approved-csv`
- `GET /api/sms/senders`
- `POST /api/sms/senders`

## Segment Rules
- English GSM: `1-160` chars = 1 SMS, multipart uses `153`
- Unicode / regional: `1-70` chars = 1 SMS, multipart uses `67`

## Implementation Checklist
- Generate tenant API credentials in Integrations
- Save sender IDs and approved templates
- Use idempotency keys for retry-safe sends
- Monitor delivery report and SMS ledger
- Enable IP whitelist for fixed-source traffic
