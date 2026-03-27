# CasoE - Foundry Bootstrap / Logical IaC

This repository is a bootstrap-only .NET 8 console application for Azure AI Foundry.

Its job is to prepare and validate agent bindings in a Foundry project, not to execute business prompts. The app validates project access, validates the external `OrderAgentId`, reconciles the internal `refund-agent-casee` and `clarifier-agent-casee` agents, prints a reconciliation summary, and exits.

## What This Repo Does

- Loads `CasoE` configuration from `appsettings.json`
- Validates the Foundry project endpoint format
- Validates access to the target Foundry project
- Validates that `ModelDeploymentName` exists and is accessible in the project
- Validates that external `OrderAgentId` exists and can be referenced
- Reconciles `refund-agent-casee`
- Reconciles `clarifier-agent-casee`
- Prints bindings, ids, versions, and reconciliation status

## Agent Ownership

- `OrderAgent` is external to this repo. This repo only validates that the configured `OrderAgentId` exists and can be bound.
- `RefundAgent` is defined in this repo and reconciled into Foundry as `refund-agent-casee`.
- `ClarifierAgent` is defined in this repo and reconciled into Foundry as `clarifier-agent-casee`.

## Out Of Scope

- runtime consumption
- code-first routing
- prompt execution
- API exposure

This repo does not consume agents, does not route prompts, does not invoke specialists, does not validate runtime JSON outputs, and does not build final user-facing business responses. Runtime consumption belongs in another repo or service.

## Configuration

`appsettings.json` uses the `CasoE` section:

```json
{
  "CasoE": {
    "ProjectEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "ModelDeploymentName": "<deployment-name>",
    "OrderAgentId": "OrderAgent:5"
  }
}
```

## Project Structure

```text
Program.cs
CasoESettings.cs
Agents/
  AgentNames.cs
  RefundAgentFactory.cs
  ClarifierAgentFactory.cs
Models/
  ResolvedAgentIdentity.cs
  ReconciliationResult.cs
Services/
  AgentValidationService.cs
  AgentReconciler.cs
  ConsoleTrace.cs
appsettings.json
README.md
```

## Build And Run

Build:

```powershell
dotnet build
```

Run the bootstrap:

```powershell
dotnet run
```

The application does not accept business prompts or runtime arguments.

## Expected Console Output

Example:

```text
[CONFIG] Endpoint validated
[VALIDATION] Project access validated
[VALIDATION] Model deployment validated: gpt-4.1
[VALIDATION] OrderAgent validated: Name=OrderAgent, Version=5, Id=OrderAgent:5
[RECONCILE] refund-agent-casee => unchanged
[RECONCILE] clarifier-agent-casee => updated
[SUMMARY] Endpoint: https://<resource>.services.ai.azure.com/api/projects/<project>
[SUMMARY] Deployment: gpt-4.1
[SUMMARY] OrderAgent => validated | Name=OrderAgent | Version=5 | Id=OrderAgent:5
[SUMMARY] RefundAgent => unchanged | Name=refund-agent-casee | Version=3 | Id=<refund-agent-id>
[SUMMARY] ClarifierAgent => updated | Name=clarifier-agent-casee | Version=4 | Id=<clarifier-agent-id>
[SUMMARY] Bindings => OrderAgent=OrderAgent:5; RefundAgent=<refund-agent-id>; ClarifierAgent=<clarifier-agent-id>
[SUMMARY] Foundry bootstrap completed
```

## Notes

- The project stays on .NET 8.
- No API surface is introduced.
- No Foundry Workflow, ManagerAgent, or `agent` tools are introduced here.
- The authoritative runtime flow should live in a separate consumer application or service.
