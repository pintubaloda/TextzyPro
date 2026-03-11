# Textzy WhatsApp API Reference

## Overview
Textzy WhatsApp API supports two modes:
- Public tenant-scoped API for simple plain-text sends
- Authenticated tenant APIs for templates, media, inbox, and automation flows

Base URL: `https://api.textzy.in`

## Public WhatsApp Send
Use the public endpoint when an external system needs to send a plain WhatsApp text message.

### GET
```http
GET https://api.textzy.in/api/public/messages/send?recipient=919999999999&msg=Hello from Textzy WhatsApp API&user=MONEYART&pswd=YOUR_PASSWORD&apikey=YOUR_API_KEY&channel=whatsapp&tenantSlug=moneyart
```

### POST
```json
{
  "recipient": "919999999999",
  "message": "Hello from Textzy WhatsApp API",
  "user": "MONEYART",
  "password": "YOUR_PASSWORD",
  "apiKey": "YOUR_API_KEY",
  "tenantSlug": "moneyart",
  "channel": "whatsapp",
  "idempotencyKey": "wa-20260311-0001"
}
```

## Authenticated Tenant Messaging
Use authenticated tenant APIs when messages originate from Textzy web, mobile, desktop, inbox, or workflow execution.

### Session Message
```json
{
  "recipient": "919999999999",
  "channel": "WhatsApp",
  "body": "Hello from Textzy",
  "useTemplate": false
}
```

### Template Message
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

### Interactive Buttons
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

### Interactive Flow
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

## Request Fields
| Field | Used In | Description |
|---|---|---|
| `recipient` | Public and authenticated | WhatsApp number with country code |
| `message` / `msg` | Public | Plain text message body |
| `body` | Authenticated | Session or interactive message body |
| `tenantSlug` | Public | Tenant slug that owns the credentials |
| `user` / `password` / `apiKey` | Public | Tenant-scoped public API credentials |
| `templateName` | Authenticated | Approved template name |
| `templateLanguageCode` | Authenticated | Template language code |
| `interactiveType` | Authenticated | `button`, `list`, or `flow` |

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

## Media Endpoints
- `POST /api/messages/upload-whatsapp-media`
- `POST /api/messages/upload-whatsapp-asset`
- `GET /api/messages/media/{mediaId}`

## Template Endpoints
- `GET /api/templates`
- `POST /api/templates`
- `POST /api/templates/{id}/sync-meta`
- `GET /api/templates/project-list`

## Flow and Automation Endpoints
- `GET /api/automation/flows`
- `POST /api/automation/flows`
- `POST /api/automation/flows/{flowId}/simulate`
- `POST /api/automation/flows/{flowId}/run`
- `POST /api/automation/flows/{flowId}/send-flow`

## Webhook Endpoint
- `GET /api/waba/webhook`
- `POST /api/waba/webhook`

## Implementation Checklist
- Generate tenant public API credentials in Integrations
- Configure WABA and template approvals
- Use public API for simple text sends only
- Use authenticated APIs for templates, media, inbox, and flows
- Monitor webhook and inbox delivery flow in platform analytics
