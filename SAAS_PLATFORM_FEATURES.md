# Textzy SaaS Platform Features (Inventory)

This is a code-derived snapshot of what the Textzy SaaS platform currently supports, and what is typically still needed to become a complete self-serve SaaS.

## Available Today (Backend + UI)

### Public Website
- Landing + pricing (plans from `GET /api/public/plans`), contact section.
- Legal/trust pages: About, Contact, Privacy, Refund, Cookies, Terms, DPDP, Messaging compliance, Security, Subprocessors, Acceptable Use, DPA, Trust Center.

### Authentication And Onboarding
- User sessions with CSRF and session refresh/logout.
- Login with optional email verification flow (email action link -> OTP) and optional 2FA (authenticator).
- Step-up authentication for sensitive actions (TOTP-based).
- Self-serve registration with plan selection (Starter trial flow).
- Forgot password email reset flow (request -> emailed link -> reset form -> update password).
- Team invitation accept flow.
- Project (workspace) selection and switching.
- Device pairing (mobile) via QR + device management endpoints.

### Multi-Tenancy And Roles
- Tenants (workspaces) + memberships.
- Role catalog (`owner`, `admin`, `manager`, `support`, `marketing`, `finance`, `super_admin`) and permission overrides.
- Owner-group concept for cross-tenant ownership controls.

### Messaging (WhatsApp + SMS)
- WhatsApp Cloud integration service layer (Meta Graph API).
- Inbox: conversations + messages + assignment/transfer/labels/notes + typing + SLA.
- Real-time inbox hub (`/hubs/inbox`).
- Outbound message queue + worker (provider supports memory/redis/rabbitmq/sqs based on config).
- Webhook queue + worker for WABA webhooks (same provider model).
- Dead-letter visibility endpoints for outbound/webhooks.
- Broadcast queue + worker.

### SMS Control Center (India / DLT)
- SMS sender IDs, templates, flows, inputs.
- Compliance status + delivery events + billing ledger style views.
- Tata webhook endpoints (send + inbound + DLR handling paths in `SmsWebhookController`).
- SMS gateway reporting endpoints.

### Templates And Automation
- WhatsApp template management + lifecycle actions (submit/approve/reject/disable/version).
- Automation flow builder APIs (catalogs, versions, publish/unpublish, validation, metrics).
- Workflow execution engine + resume/delay worker.

### Analytics
- Tenant analytics overview APIs.
- Webhook analytics APIs.

### Billing And Purchases
- Billing plans + subscriptions + invoices.
- Usage metering + limit guards.
- Razorpay integration endpoints and payment webhooks.
- Invoice rendering and attachment services (PDF rendering exists in backend services).

### Support
- Tenant support tickets + replies + reopen flows.
- Platform support desk views (platform owner).

### Platform Owner (Super Admin) Capabilities
- Platform owner dashboard and admin workspace.
- Customer/tenant management: list customers, see usage/subscriptions/invoices/members/activity, assign plans (with trial days), effective owner assignment, customer feature toggles, company settings.
- Platform settings: WABA master, SMTP/email settings, SMS gateway, integration catalog, billing plans, request logs, branding.
- Platform queue health + security report + purchase report + request logs + backup endpoints.

## Background Workers (Always-On Jobs)

Registered hosted services (see `backend-dotnet/Program.cs`):
- `BroadcastWorker`
- `OutboundMessageWorker`
- `WabaWebhookWorker`
- `WabaOnboardingHealthWorker`
- `SecurityMonitoringWorker`
- `TemplateStatusSyncWorker`
- `WorkflowDelayResumeWorker`
- `BillingLifecycleWorker`

## What Is Typically Still Needed (To Call It “Complete SaaS”)

### Subscription Lifecycle (Revenue Critical)
- Automated trial-to-paid conversion flow (upgrade CTA, payment capture, proration policy).
- Automated enforcement when `RenewAtUtc` passes: grace period, suspend/lock messaging, re-activate on payment.
- Customer self-serve plan changes (upgrade/downgrade) with clear effective dates.
- Email notifications: trial ending, payment failed, invoice issued, renewal upcoming.

### Email Deliverability And Operations
- Bounce/complaint handling, suppression list, and delivery health dashboard.
- Required DNS (SPF/DKIM/DMARC) documentation and validation checks.
- Admin test-email button and clearer error surfaces when SMTP is not configured.

### Security And Enterprise Readiness
- SSO (SAML/OIDC) and SCIM provisioning (Enterprise).
- Audit log export and long-term retention controls.
- Admin impersonation (if needed) with strict auditing and step-up auth.

### Data Governance And Compliance
- GDPR-style exports/deletions (contacts, conversations, messages).
- Tenant backup/restore workflow and disaster recovery runbooks.

### Product Ops
- Central observability: traces, structured logs, dashboards, and alerting (queue lag, webhook lag, error rates).
- Rate limiting and abuse protection on public endpoints (register/login/forgot-password).
- Status page and incident tooling.

## Notes / Known Implementation Details
- Email sending uses platform settings with scope `smtp` (supports `smtp` and `resend` providers). If those settings are empty/misconfigured, email flows will not deliver.
- Plans shown on landing/registration come from `GET /api/public/plans`. The registration page uses plan limits to show “What you’ll get”.

