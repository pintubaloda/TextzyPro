# Textzy WhatsApp API Reference

Professional WhatsApp Business API reference for Textzy.

This document covers:
- tenant-authenticated WhatsApp send APIs
- template management
- media upload
- webhook processing
- flow builder and Meta flow APIs
- customer-facing request examples
- operational flow for production use

## 1. Base URL

- Backend API: `https://textzy-backend-production.up.railway.app`

Use the backend URL for all WhatsApp APIs.

## 2. Authentication Model

WhatsApp APIs are tenant-scoped operational APIs.

Use:
- authenticated Textzy session
- selected project / tenant
- CSRF token on unsafe browser requests
- cookie-based session transport
- optional login-time authenticator verification when 2FA is enabled

Core auth endpoints:
- `POST /api/auth/login`
- `POST /api/auth/two-factor/verify-login`
- `GET /api/auth/projects`
- `POST /api/auth/switch-project`
- `GET /api/auth/me`
- `POST /api/auth/refresh`
- `POST /api/auth/logout`

Important behavior:
- first-party Textzy web and mobile apps use httpOnly cookie sessions
- tenant context comes from the selected project
- external callers should not try to supply WhatsApp tenant context by arbitrary header mutation

## 3. Complete Working Flow

Use this sequence for normal WhatsApp operations inside Textzy:

1. Login  
   `POST /api/auth/login`

2. If 2FA is enabled, verify authenticator  
   `POST /api/auth/two-factor/verify-login`

3. Get available projects  
   `GET /api/auth/projects`

4. Select project / tenant  
   `POST /api/auth/switch-project`

5. Check readiness before messaging  
   `GET /api/waba/smoke/readiness`

6. Send one of:
   - session message
   - template message
   - interactive message
   - flow message

7. Monitor:
   - inbox updates
   - webhook status
   - analytics

## 4. Sample Login and Project Selection

### 4.1 Login

```json
{
  "email": "owner@company.com",
  "password": "StrongPassword"
}
```

### 4.2 Two-Factor Verify

```json
{
  "challengeToken": "LOGIN_CHALLENGE_TOKEN",
  "code": "123456"
}
```

### 4.3 Switch Project

```json
{
  "slug": "moneyart"
}
```

## 5. Send WhatsApp Session Message

Endpoint:
- `POST /api/messages/send`

Example:

```json
{
  "recipient": "919999999999",
  "channel": "WhatsApp",
  "body": "Hello from Textzy",
  "useTemplate": false
}
```

Behavior:
- works inside the 24-hour customer service window
- session messages are blocked outside active WhatsApp session window
- requires authenticated tenant context, not public API credentials

## 6. Send WhatsApp Template Message

Endpoint:
- `POST /api/messages/send`

Example:

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

Use this when:
- 24-hour session window is closed
- utility / authentication / marketing template must be sent
- business flow requires approved template delivery

Sample authentication template request:

```json
{
  "recipient": "919999999999",
  "channel": "WhatsApp",
  "useTemplate": true,
  "templateName": "login_otp",
  "templateLanguageCode": "en",
  "templateParameters": ["123456"]
}
```

## 7. Send Interactive WhatsApp Message

Endpoint:
- `POST /api/messages/send`

### 7.1 Button Example

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

### 7.2 Flow Example

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

## 8. Media Upload and Send

Endpoints:
- `POST /api/messages/upload-whatsapp-media`
- `POST /api/messages/upload-whatsapp-asset`
- `GET /api/messages/media/{mediaId}`

Use cases:
- send media in live conversation
- upload template header assets
- preview or download inbound media from inbox

## 9. WhatsApp Template Management

Endpoints:
- `GET /api/templates`
- `POST /api/templates`
- `PUT /api/templates/{id}`
- `DELETE /api/templates/{id}`
- `GET /api/templates/{id}/presets`
- `GET /api/templates/project-list`

Supported template properties include:
- name
- category
- language
- body
- header type
- header text or header media
- footer text
- buttons JSON

Supported categories:
- `MARKETING`
- `UTILITY`
- `AUTHENTICATION`

## 10. WhatsApp Webhook

Verification endpoint:
- `GET /api/waba/webhook`

Event receiver:
- `POST /api/waba/webhook`

Purpose:
- inbound message receive
- delivery and read receipts
- interactive reply capture
- automation trigger execution
- flow completion handling

## 11. WhatsApp Flow Builder and Automation API

## 11.1 Flow Catalog and Lifecycle

Endpoints:
- `GET /api/automation/catalogs/node-types`
- `GET /api/automation/limits`
- `GET /api/automation/flows`
- `POST /api/automation/flows`
- `GET /api/automation/flows/{flowId}`
- `PUT /api/automation/flows/{flowId}`
- `DELETE /api/automation/flows/{flowId}`
- `GET /api/automation/flows/{flowId}/versions`
- `POST /api/automation/flows/{flowId}/versions`
- `POST /api/automation/flows/{flowId}/versions/{versionId}/publish`
- `POST /api/automation/flows/{flowId}/unpublish`
- `POST /api/automation/flows/{flowId}/versions/{versionId}/rollback`

## 11.2 Validation, Simulation, and Runtime

Endpoints:
- `POST /api/automation/flows/validate-definition`
- `POST /api/automation/flows/{flowId}/versions/{versionId}/validate`
- `POST /api/automation/flows/{flowId}/simulate`
- `POST /api/automation/flows/{flowId}/run`
- `GET /api/automation/runs`
- `GET /api/automation/runs/{runId}`

## 11.3 Meta Flow Management

Endpoints:
- `GET /api/automation/meta/flows`
- `POST /api/automation/meta/flows`
- `GET /api/automation/meta/flows/{metaFlowId}`
- `PUT /api/automation/meta/flows/{metaFlowId}`
- `DELETE /api/automation/meta/flows/{metaFlowId}`
- `POST /api/automation/meta/flows/{metaFlowId}/publish`
- `POST /api/automation/flows/{flowId}/import-meta`

## 11.4 Flow Delivery and Dynamic Data

Endpoints:
- `POST /api/automation/flows/{flowId}/send-flow`
- `POST /api/automation/flows/{flowId}/data-exchange`
- `GET /api/automation/metrics/flows`
- `GET /api/automation/metrics/flows/events`
- `GET /api/automation/trigger-audit`
- `GET /api/automation/trigger-audit/summary`

Live customer automations should use published flow/runtime APIs only.

## 12. WhatsApp Diagnostics and Smoke Testing

### 12.1 Readiness
- `GET /api/waba/smoke/readiness`

Returns readiness details such as:
- WABA configuration
- onboarding state
- webhook verification state
- template sync state
- 24-hour runtime counters

### 12.2 Smoke Run
- `POST /api/waba/smoke/run`

Example:

```json
{
  "recipient": "919999999999",
  "sendSessionMessage": true,
  "sessionMessageText": "Smoke test from Textzy",
  "sendTemplateMessage": false
}
```

Useful for:
- session-open validation
- test session send
- test template send
- confirming tenant-specific WABA readiness

## 13. Analytics and Reporting

Tenant analytics endpoints relevant to WhatsApp:
- `GET /api/analytics/overview`
- `GET /api/analytics/webhook-status`

These are typically used by dashboard analytics pages to show:
- message volume
- delivery counts
- read counts
- channel distribution
- campaign performance
- conversation load
- flow activity

Sensitive write paths can require step-up authenticator verification.

## 14. Common Status Codes

- `200` success
- `201` created
- `204` no-content success
- `400` validation failure
- `401` authentication failed
- `403` permission denied
- `404` resource not found
- `409` conflict
- `422` semantic validation issue
- `428` step-up verification required
- `429` rate limit exceeded
- `502` provider/gateway problem
- `503` temporarily unavailable

## 15. Production Checklist

- use backend URL only
- use HTTPS only
- confirm WABA is connected and active
- confirm webhook is subscribed and verified
- confirm templates are approved before using them outside the 24-hour window
- use session send only inside open service window
- run readiness and smoke checks before go-live
- monitor webhook failures and message event drift
- confirm login, project switch, and 2FA work for operational users

## 16. Best-Practice Recommendation

For full WhatsApp operations, use Textzy authenticated tenant APIs rather than trying to expose WhatsApp send publicly.

Recommended flow:
1. login
2. if required, complete authenticator verification
3. select project
4. check readiness
5. send session, template, interactive, or flow message
6. monitor inbox, webhooks, and analytics
4. verify WABA readiness
5. sync or create templates
6. send session, template, interactive, or flow message
7. monitor webhooks and analytics
8. use flow APIs for richer automation and structured journey execution
