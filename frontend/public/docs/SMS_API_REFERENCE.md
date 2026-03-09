# Textzy SMS API Reference

Professional SMS integration reference for Textzy.

This document covers:
- simple public SMS API
- app-internal tenant SMS send
- DLT requirements
- sender and template registry
- Tata gateway behavior
- tenant delivery reporting
- operational notes

## 1. Base URL

- Backend API: `https://textzy-backend-production.up.railway.app`

Use the backend URL for all SMS API calls.

## 2. SMS Integration Models

### 2.1 Simple Public SMS API

Use when you need:
- URL-based integration
- server-to-server SMS send
- ERP/CRM/panel integration
- no login session or bearer token flow
- tenant-specific isolated credentials managed by platform owner

Endpoints:
- `GET /api/public/messages/send`
- `POST /api/public/messages/send`

### 2.2 Textzy App SMS API

Use when:
- the user is already signed in to Textzy
- SMS is triggered from Textzy web, mobile, desktop, workflow, OTP, or automation
- billing, audit, credits, and message history must stay inside Textzy

Endpoint:
- `POST /api/messages/send`

## 3. Simple Public SMS API

## 3.1 GET Request

```text
GET https://textzy-backend-production.up.railway.app/api/public/messages/send
  ?recipient=919999999999
  &msg=Your%20approved%20DLT%20message%20text
  &user=MONEYART
  &pswd=YOUR_PASSWORD
  &apikey=YOUR_API_KEY
  &channel=sms
  &sender=MNYART
  &PE_ID=1601100000000006533
  &Template_ID=1207171593687982329
  &tenantSlug=moneyart
```

## 3.2 POST Request

`POST /api/public/messages/send`

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

## 3.3 Required Fields

- `recipient`: mobile number with country code
- `message` or `msg`: exact SMS text
- `tenantSlug`: target tenant slug
- `user`: API username
- `password` or `pswd`: API password
- `apiKey` or `apikey`: API key
- `sender`: approved sender ID
- `peId` or `PE_ID`: approved DLT entity ID
- `templateId` or `Template_ID`: approved DLT template ID

## 3.4 Optional Fields

- `idempotencyKey`
- `channel` (defaults to SMS)

## 3.5 Tenant-Scoped Security Model

Public SMS credentials are not shared across the platform.

Each tenant can have its own:
- `publicApiEnabled`
- `apiUsername`
- `apiPassword`
- `apiKey`
- optional IP whitelist

Platform owner provisions these credentials per tenant from the admin console.

Important rules:
- `tenantSlug` is required
- credentials are validated against that tenant only
- changing `tenantSlug` does not let a caller reuse another tenant's credentials
- use HTTPS only
- if IP whitelist is configured, requests outside that list are rejected

## 3.6 Success Response

```json
{
  "jobId": "5d64f8bf-4c1f-4e59-92a9-4a0f5a8c992e",
  "message": "Accepted"
}
```

## 3.7 Public Error Response

```json
{
  "message": "Invalid authorization.",
  "code": "401"
}
```

```json
{
  "message": "Sender is required.",
  "code": "422"
}
```

Common codes:
- `400` request rejected
- `401` invalid authorization
- `403` access denied
- `422` missing DLT field
- `429` rate limit exceeded
- `503` gateway temporarily unavailable

## 4. Textzy App SMS Send

Endpoint:
- `POST /api/messages/send`

This is not the partner/public API.

It is the internal tenant API used by Textzy applications after login.

Typical flow:
1. user signs in to Textzy
2. user selects a project
3. Textzy checks SMS credits / plan rules
4. Textzy sends the request to Tata using centrally managed gateway credentials
5. delivery status is recorded in tenant reports and ledger

Example request from Textzy app:

```json
{
  "recipient": "919999999999",
  "channel": "Sms",
  "body": "Your OTP is 123456",
  "smsSenderId": "MNYART",
  "smsPeId": "1601100000000006533",
  "smsTemplateId": "1207171593687982329"
}
```

Requirements:
- authenticated tenant session
- selected project
- idempotency key header
- SMS credits or valid billing allowance
- cookie-based web/mobile/desktop session for first-party Textzy apps

What the tenant/user supplies in this flow:
- recipient
- message body
- sender ID
- PE ID
- template ID

What Textzy supplies internally:
- Tata gateway username
- Tata gateway password
- routing and audit metadata
- billing / ledger handling

## 5. DLT Requirements

For India DLT-compliant SMS, Textzy expects:
- sender ID approved by DLT
- PE ID / Entity ID approved by DLT
- template ID approved by DLT
- message content aligned with approved template

Field mapping:
- `sender` = Sender ID
- `PE_ID` = Entity ID
- `Template_ID` = approved DLT template ID

## 6. Tata Gateway Mapping

Textzy maps SMS send to Tata in this format:

```text
https://smsgw.tatatel.co.in:9095/campaignService/campaigns/qs
  ?recipient=<MobileNo>
  &dr=false
  &msg=<Message>
  &user=<Username>
  &pswd=<Password>
  &sender=<SenderAddress>
  &PE_ID=<PEID>
  &Template_ID=<TemplateID>
```

Textzy keeps Tata credentials at platform level and uses them internally.

The tenant/request payload passes only:
- recipient
- sender
- message
- PE_ID
- Template_ID

This means:
- customer integrations never need direct Tata credentials
- tenant users never see platform gateway secrets
- one tenant cannot reuse another tenant's API credentials

## 7. SMS Template Registry

Endpoints:
- `GET /api/sms/templates`
- `POST /api/sms/templates`
- `PUT /api/sms/templates/{id}`
- `DELETE /api/sms/templates/{id}`
- `POST /api/sms/templates/{id}/status`
- `POST /api/sms/templates/import-approved-csv`

Use this registry for:
- approved DLT templates
- sender mapping
- operator metadata
- lifecycle status

Suggested import columns:
- `tenantId`
- `entityId`
- `templateName`
- `templateId`
- `status`
- `templateContent`
- `header`
- `templateType`

## 8. SMS Sender Registry

Endpoints:
- `GET /api/sms/senders`
- `GET /api/sms/senders/stats`
- `POST /api/sms/senders`
- `PUT /api/sms/senders/{id}`
- `DELETE /api/sms/senders/{id}`

Used for:
- sender inventory
- entity mapping
- route type
- verification state

## 9. Delivery and Status Reporting

After a message is accepted:
- Textzy stores the provider job ID
- delivery callbacks update tenant SMS report
- ledger and status history are refreshed automatically

Tenant-visible statuses typically include:
- accepted
- queued
- sent
- delivered
- failed
- rejected

STOP / unsubscribe handling is managed by Textzy internally and is not part of the normal tenant integration surface.

## 10. SMS Segment Logic

Textzy uses standard segment calculation:

### English GSM
- `1-160` chars = 1 SMS
- `161-306` chars = 2 SMS
- multipart uses `153` chars per segment

### Unicode / Regional
- `1-70` chars = 1 SMS
- `71-134` chars = 2 SMS
- multipart uses `67` chars per segment

## 11. Production Checklist

- use backend URL only
- use HTTPS only
- protect API credentials
- validate sender, PE_ID, and Template_ID
- keep message text aligned with approved DLT template
- verify delivery status in Textzy reports after go-live

## 12. Best-Practice Recommendation

Use the public SMS API for outside systems such as:
- CRM
- ERP
- websites
- external admin panels

Use the Textzy app SMS API only from inside Textzy products.

For simple partner integrations:
- use `POST /api/public/messages/send`
- keep credentials server-side if possible
- keep `GET` only for legacy compatibility

For product-led or application-native integrations:
- use authenticated tenant APIs
- rely on billing, audit, and inbox workflow already built into Textzy
