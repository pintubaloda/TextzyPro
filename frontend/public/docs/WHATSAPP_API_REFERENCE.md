# Textzy WhatsApp API Reference

Base URL:
- `https://textzy-backend-production.up.railway.app`

## 1. Public WhatsApp API

Use this API when an external system wants to send a plain WhatsApp text message through Textzy with tenant-specific API credentials.

Endpoints:
- `GET /api/public/messages/send`
- `POST /api/public/messages/send`

Channel value:
- `whatsapp`

## 2. GET Example

```text
GET https://textzy-backend-production.up.railway.app/api/public/messages/send
  ?recipient=919999999999
  &msg=Hello from Textzy WhatsApp API
  &user=MONEYART
  &pswd=YOUR_PASSWORD
  &apikey=YOUR_API_KEY
  &channel=whatsapp
  &tenantSlug=moneyart
```

## 3. POST Example

```json
{
  "recipient": "919999999999",
  "message": "Hello from Textzy WhatsApp API",
  "user": "MONEYART",
  "password": "YOUR_PASSWORD",
  "apiKey": "YOUR_API_KEY",
  "tenantSlug": "moneyart",
  "channel": "whatsapp",
  "idempotencyKey": "wa-20260308-0001"
}
```

## 4. Required Fields

- `recipient`: mobile number with country code
- `message` or `msg`: plain WhatsApp message text
- `tenantSlug`: target tenant slug
- `user`: tenant API username
- `password` or `pswd`: tenant API password
- `apiKey` or `apikey`: tenant API key
- `channel`: `whatsapp`

## 5. Optional Fields

- `idempotencyKey`

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
- `429` rate limit exceeded
- `503` gateway temporarily unavailable

## 9. Authenticated Tenant WhatsApp API

Use the authenticated tenant APIs when messages originate from Textzy web, mobile, desktop, inbox, or workflow execution.

Primary endpoint:
- `POST /api/messages/send`

### Session message example

```json
{
  "recipient": "919999999999",
  "channel": "WhatsApp",
  "body": "Hello from Textzy",
  "useTemplate": false
}
```

### Template message example

```json
{
  "recipient": "919999999999",
  "channel": "WhatsApp",
  "useTemplate": true,
  "templateName": "order_update",
  "templateLanguageCode": "en",
  "templateParameters": ["John", "#1234"]
}
```

### Interactive button example

```json
{
  "recipient": "919999999999",
  "channel": "WhatsApp",
  "isInteractive": true,
  "interactiveType": "button",
  "body": "How can we help you?",
  "interactiveButtons": ["Support", "Sales", "Accounts"]
}
```

### Interactive flow example

```json
{
  "recipient": "919999999999",
  "channel": "WhatsApp",
  "isInteractive": true,
  "interactiveType": "flow",
  "body": "Open the flow below.",
  "interactiveFlowId": "FLOW_ID",
  "interactiveFlowCta": "Open",
  "interactiveFlowAction": "navigate",
  "interactiveFlowScreen": "start",
  "interactiveFlowDataJson": "{}",
  "interactiveFlowMessageVersion": 3
}
```

## 10. Media APIs

Endpoints:
- `POST /api/messages/upload-whatsapp-media`
- `POST /api/messages/upload-whatsapp-asset`
- `GET /api/messages/media/{mediaId}`

## 11. Template APIs

Endpoints:
- `GET /api/templates`
- `POST /api/templates`
- `PUT /api/templates/{id}`
- `DELETE /api/templates/{id}`
- `GET /api/templates/{id}/presets`
- `GET /api/templates/project-list`

## 12. Webhook APIs

Endpoints:
- `GET /api/waba/webhook`
- `POST /api/waba/webhook`

## 13. Flow and Automation APIs

Endpoints:
- `GET /api/automation/flows`
- `POST /api/automation/flows`
- `GET /api/automation/flows/{flowId}`
- `PUT /api/automation/flows/{flowId}`
- `GET /api/automation/flows/{flowId}/versions`
- `POST /api/automation/flows/{flowId}/versions/{versionId}/publish`
- `POST /api/automation/flows/{flowId}/simulate`
- `POST /api/automation/flows/{flowId}/run`
- `POST /api/automation/flows/{flowId}/send-flow`
- `POST /api/automation/flows/{flowId}/data-exchange`

## 14. Operational Notes

- public WhatsApp API examples above are for plain text sends
- richer features such as templates, interactive buttons, flows, media, and inbox actions use authenticated tenant APIs
- session messaging is subject to WhatsApp conversation-window rules
- approved templates should be used when a session message is not allowed
