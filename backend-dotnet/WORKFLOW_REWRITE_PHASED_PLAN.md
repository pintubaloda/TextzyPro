# Workflow Rewrite (Safe Phased Cutover)

This plan migrates from the current webhook-coupled workflow runtime (`WabaWebhookWorker`) to a separated trigger/evaluation/execution architecture without breaking production.

## Current Production Runtime

- Inbound webhook processing and workflow execution live in:
  - `backend-dotnet/Services/WabaWebhookWorker.cs`
- Flow CRUD/versioning:
  - `backend-dotnet/Controllers/AutomationController.cs`
- Existing workflow tables:
  - `AutomationFlows`
  - `AutomationFlowVersions`
  - `AutomationNodes`
  - `AutomationRuns`
  - `AutomationApprovals`
  - `AutomationUsageCounters`

## Target Runtime

- Trigger evaluation service (isolated)
- Execution engine service (stateful node runtime)
- Optional middleware/worker orchestration
- Strong workflow execution observability

## Non-Negotiable Safety Rules

1. No big-bang switch.
2. Keep current runtime as fallback until parity is proven.
3. Feature-flag all new execution paths.
4. Any deploy must preserve webhook 200 response behavior.
5. Tenant and phone-number isolation cannot change.

## Phase 0 (Done Before Any Code Cutover)

### 0.1 Feature flags

Add config flags:

- `Workflow__EngineMode=legacy|shadow|new`
- `Workflow__ShadowLogOnly=true|false`
- `Workflow__EnableExecutionState=false|true`

Behavior:

- `legacy`: only current `WabaWebhookWorker` execution.
- `shadow`: legacy executes, new engine dry-runs/logs only.
- `new`: new engine executes, legacy disabled except fallback-on-error.

### 0.2 Rollback guarantee

Rollback must be one config change:

- set `Workflow__EngineMode=legacy`

No DB rollback required for runtime fallback.

## Phase 1 (Schema Extension, No Behavior Change)

Create additive tables only (no destructive changes):

- `WorkflowExecutionStates`
- `WorkflowExecutionLogs`
- `TriggerEvaluationAudit`
- `AgentAvailability`
- `ConversationQueue`
- `ScheduledMessages`
- `AgentActivityLog`

Rules:

- no drop/rename of existing tables
- no required columns added to existing hot tables
- all new tables nullable/default-safe

## Phase 2 (Shadow Trigger Evaluation)

Implement `TriggerEvaluationService` in parallel to current trigger logic.

Runtime:

- current trigger path remains source of truth
- new trigger evaluator runs in shadow mode
- write comparison rows to audit:
  - inbound id
  - matched flow (legacy)
  - matched flow (shadow)
  - mismatch reason

Exit criteria:

- >= 99% match parity over real traffic sample

## Phase 3 (Shadow Execution Engine)

Implement `WorkflowExecutionEngine` dry-run:

- parse same definition JSON as production
- execute nodes without sending external messages
- log expected actions to `WorkflowExecutionLogs`

Compare with legacy action logs:

- node path
- condition outcomes
- final branch selected

Exit criteria:

- no material divergence for live flows

## Phase 4 (Controlled Send Cutover)

Enable new engine for low-risk tenants only:

- whitelist by tenant id
- fall back to legacy on runtime exception

Required:

- idempotency preserved
- outbound enqueue semantics preserved
- `waba.workflow.*` logs retained for ops view

Exit criteria:

- zero critical delivery regressions over burn-in period

## Phase 5 (Primary Runtime Switch)

Set default mode:

- `Workflow__EngineMode=new`

Keep legacy code for one release cycle as emergency fallback.

After stability window:

- deprecate legacy execution blocks
- keep shared parser/evaluator utilities

## Immediate Next Work Items

1. Add workflow mode flags and runtime mode logging.
2. Add additive SQL migration file for Phase 1 tables.
3. Implement shadow trigger evaluator with parity audit rows.
4. Add platform diagnostics endpoint for mismatch rates.

## Operational Checks Per Deploy

1. Webhook response latency unchanged.
2. `waba.workflow.trigger_eval` continues to appear.
3. No increase in dead-letter or enqueue-failed rates.
4. Interactive reply flows continue to branch correctly.
5. Tenant isolation verified on `phone_number_id`.

