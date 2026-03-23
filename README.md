# Caso E - Code-First Orchestrator (.NET 8 + Foundry Agents SDK)

This repository implements the code-first counterpart to the workflow-first `CasoD`. The orchestration lives in C# code. The .NET application is the router, decides which branch to run, validates structured outputs, and produces the final user-facing answer.

Foundry is used here as the agent runtime and capability platform. It is not the orchestration layer for this case.

## Architecture

```text
User
  |
  v
App Orchestrator (.NET 8)
  |
  v
Intent router in code
  |- OrderAgent      -> MCP/APIM -> backend API
  |- RefundAgent     -> prompt agent
  |- ClarifierAgent  -> prompt agent
  `- Reject branch   -> app-controlled response
```

## Why This Case Exists

`CasoD` shows workflow-first routing where Foundry Workflow is the orchestration layer.

`CasoE` shows the opposite model:

- the routing logic is deterministic C# code
- Foundry prompt agents are specialist workers only
- the app validates JSON contracts between steps
- the app owns the final response shown to the user

This makes the tradeoff against workflow-first explicit.

## Comparison

| Dimension | Workflow-first (`CasoD`) | Code-first (`CasoE`) |
| --- | --- | --- |
| Orchestration location | Foundry Workflow | .NET application code |
| Control | Visual workflow graph and bindings | Full branch control in C# |
| Visibility | Workflow-centric runtime view | App-centric logs and code path |
| Testability | Validate through workflow deployment and branch runs | Validate router/helpers/output validators directly in code |
| Branching changes | Change workflow graph/YAML | Change router and branch methods |
| Governance effort | Workflow asset plus agent bindings | Application release plus agent reconciliation |

## App Responsibilities

The app:

- loads configuration
- validates Foundry project endpoint access
- validates the external `OrderAgentId`
- reconciles `refund-agent-casee` and `clarifier-agent-casee`
- classifies user intent in code
- invokes only the selected specialist
- validates structured JSON outputs with `System.Text.Json`
- builds the final user-facing answer in code

The app does not:

- use Foundry Workflow in the runtime path
- use a ManagerAgent
- use manually serialized agent tools
- claim that Foundry is doing the orchestration

## Project Structure

```text
Program.cs
CasoESettings.cs
Agents/
  AgentNames.cs
  RefundAgentFactory.cs
  ClarifierAgentFactory.cs
Models/
  RouteDecision.cs
  OrderResult.cs
  RefundResult.cs
  ClarifierResult.cs
  ResolvedAgentIdentity.cs
  ReconciliationResult.cs
Services/
  AgentValidationService.cs
  AgentReconciler.cs
  AgentRunner.cs
  IntentRouter.cs
  OrderIdExtractor.cs
  OutputValidators.cs
  ConsoleTrace.cs
appsettings.json
README.md
```

## Configuration

`appsettings.json` uses a dedicated `CasoE` section:

```json
{
  "CasoE": {
    "ProjectEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "ModelDeploymentName": "<deployment-name>",
    "OrderAgentId": "OrderAgent:5",
    "ResponsesTimeoutSeconds": 60,
    "ResponsesMaxBackoffSeconds": 8,
    "DefaultPrompt": "Where is order ORD-000123?"
  }
}
```

## Strict Contracts

`OrderAgent` must return:

```json
{
  "id": "string",
  "status": "Created|Confirmed|Packed|Shipped|Delivered|Cancelled|Unknown|NotFound",
  "requiresAction": true,
  "reason": "optional string"
}
```

`RefundAgent` must return:

```json
{
  "status": "accepted|needsMoreInfo|notAllowed|pending",
  "message": "short explanation",
  "orderId": "optional string",
  "refundReason": "optional string"
}
```

`ClarifierAgent` must return:

```json
{
  "question": "single clear clarification question"
}
```

Invalid JSON or missing required fields raise `InvalidOperationException` immediately. There is no free-form text fallback for `OrderAgent`.

## Routing Rules Used In Code

- `reject` first when destructive mass-order actions are requested, such as `delete`, `remove`, `erase`, `purge`, `wipe`, `destroy`, `drop`, or `cancel` against `all orders`, `every order`, or equivalent bulk targets
- `reject` also when those destructive verbs target an explicit order reference, including `order`, `orders`, `ORD-...`, or a numeric order id extracted by `OrderIdExtractor`
- `reject` for administrative requests outside the supported order-status and refund scope
- `refund` when the prompt includes `refund`, `reimbursement`, or `money back`
- `refund` for `return` only when `return` appears with refund/order context; status-oriented phrases like `return the order status` do not auto-route to `refund`
- `order` when the prompt is about order or shipment status and an order id is present
- `clarify` when the request is in-domain but ambiguous or missing a required order id
- `reject` for all other unsupported requests
- `OrderIdExtractor` matches `ORD[- ]?\d{3,}` first, then falls back to `\b\d{4,}\b`
- `RefundReason` is extracted best-effort from `because`, `since`, `due to`, or trailing `for`, while skipping obvious order-id-only and status/tracking captures

## Prompt Agents Reconciled By This Repo

### RefundAgent

```text
You are RefundAgent.
You handle refund requests safely.
Return exactly one JSON object and nothing else.
No markdown.
No prose outside JSON.
Output:
{"status":"accepted|needsMoreInfo|notAllowed|pending","message":"short explanation","orderId":"optional string","refundReason":"optional string"}

Rules:
- Do not invent approvals.
- If critical information is missing, use status="needsMoreInfo".
- Use status="notAllowed" for disallowed or unsupported refund requests.
- Use status="pending" when the request is valid but requires manual review or follow-up.
- Echo orderId and refundReason when known.
- Keep message short and user-safe.
```

### ClarifierAgent

```text
You are ClarifierAgent.
You receive the original user request, any detected orderId, and a short summary of missing information.
Return exactly one JSON object and nothing else:
{"question":"single clear clarification question"}
Ask only one concise question.
Do not mention tools, systems, workflows, MCP, backend, or internal routing.
```

## OrderAgent Prompt Template

`CasoE` invokes the external `OrderAgentId` with this strict structured prompt:

```text
Retrieve only structured order data for the requested order.
Use your configured MCP tool if applicable.
Return exactly one JSON object and nothing else.
No markdown.
No prose outside JSON.

Required fields:
- id
- status
- requiresAction

Optional field:
- reason

Allowed status values:
- Created
- Confirmed
- Packed
- Shipped
- Delivered
- Cancelled
- Unknown
- NotFound

If the order is not found, return:
{"id":"<requested-id>","status":"NotFound","requiresAction":false,"reason":"Order not found"}

User request:
{0}
```

## Build And Run

Build:

```powershell
dotnet build
```

Run with the configured default prompt:

```powershell
dotnet run
```

Run with an explicit prompt:

```powershell
dotnet run -- "I want a refund for order ORD-000123 because it arrived damaged."
```

## Sample Console Output

### Order

```text
[CONFIG] Configuration loaded
[CONFIG] Endpoint validated
[VALIDATION] OrderAgent validated: Name=OrderAgent, Version=5, Id=OrderAgent:5
[RECONCILE] refund-agent-casee => Created
[RECONCILE] clarifier-agent-casee => Created
[INPUT] Prompt selected: Where is order ORD-000123?
[ROUTER] Route selected: order
[ROUTER] Extracted OrderId: ORD-000123
[AGENT] Invoking OrderAgent
[VALIDATION] OrderAgent JSON valid
[FINAL] Response built successfully

Order ORD-000123 is currently shipped. No action is required right now.
```

### Refund

```text
[CONFIG] Configuration loaded
[CONFIG] Endpoint validated
[VALIDATION] OrderAgent validated: Name=OrderAgent, Version=5, Id=OrderAgent:5
[RECONCILE] refund-agent-casee => Unchanged
[RECONCILE] clarifier-agent-casee => Unchanged
[INPUT] Prompt selected: I want a refund for order ORD-000123 because it arrived damaged.
[ROUTER] Route selected: refund
[ROUTER] Extracted OrderId: ORD-000123
[ROUTER] Extracted RefundReason: it arrived damaged
[AGENT] Invoking RefundAgent
[VALIDATION] RefundAgent JSON valid
[FINAL] Response built successfully

The refund request for order ORD-000123 is pending review. Manual review is required for damaged-item refunds.
```

### Clarify

```text
[CONFIG] Configuration loaded
[CONFIG] Endpoint validated
[VALIDATION] OrderAgent validated: Name=OrderAgent, Version=5, Id=OrderAgent:5
[RECONCILE] refund-agent-casee => Unchanged
[RECONCILE] clarifier-agent-casee => Unchanged
[INPUT] Prompt selected: Can you help with my order?
[ROUTER] Route selected: clarify
[AGENT] Invoking ClarifierAgent
[VALIDATION] ClarifierAgent JSON valid
[FINAL] Response built successfully

What is the order ID you want me to check?
```

### Reject

```text
[CONFIG] Configuration loaded
[CONFIG] Endpoint validated
[VALIDATION] OrderAgent validated: Name=OrderAgent, Version=5, Id=OrderAgent:5
[RECONCILE] refund-agent-casee => Unchanged
[RECONCILE] clarifier-agent-casee => Unchanged
[INPUT] Prompt selected: Delete all orders.
[ROUTER] Route selected: reject
[FINAL] Response built successfully

I can help with order status and refund questions, but I can't delete orders or perform destructive account actions.
```

## Relationship To CasoD

- `CasoD` is workflow-first and keeps the authoritative routing flow in Foundry Workflow
- `CasoE` is code-first and keeps the authoritative routing flow in `Program.cs` plus local services
- both reuse the external `OrderAgent`
- only `CasoE` makes the .NET app responsible for end-to-end routing and final response composition
