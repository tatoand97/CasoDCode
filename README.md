# CasoDCode - Bootstrap / IaC Logico de Foundry

`CasoDCode` es un repositorio de bootstrap / IaC logico para Azure AI Foundry sobre .NET 8.

Su responsabilidad es validar configuracion, validar acceso al proyecto Foundry, validar la referencia externa a `OrderAgent`, reconciliar `refund-agent-casee`, reconciliar `clarifier-agent-casee`, imprimir un resumen de bindings y terminar. Este repo ya no es una PoC de consumo code-first.

## Que hace este repo

- Carga `CasoDCodeSettings` desde `appsettings.json`
- Valida `ProjectEndpoint`
- Valida el deployment configurado en `ModelDeploymentName`
- Valida acceso al proyecto Foundry
- Valida que `OrderAgentId` exista y pueda referenciarse
- Reconcilia `refund-agent-casee`
- Reconcilia `clarifier-agent-casee`
- Imprime ids, nombres, versiones y bindings para repos consumidores
- Termina con codigo `0` cuando el bootstrap finaliza correctamente

## Produces

- validated external OrderAgent reference
- reconciled RefundAgent
- reconciled ClarifierAgent
- binding summary for downstream consumer repos

## Ownership de agentes

- `OrderAgent` es externo a este repo. `CasoDCode` solo valida que el `OrderAgentId` configurado exista y pueda enlazarse.
- `RefundAgent` se define en este repo y se reconcilia en Foundry como `refund-agent-casee`.
- `ClarifierAgent` se define en este repo y se reconcilia en Foundry como `clarifier-agent-casee`.

## Este repo NO es

- una API
- una consola de consumo
- un runtime de negocio
- un orchestrator code-first

El consumo/runtime vive en otro repo o servicio.

## Out of scope

- runtime consumption
- code-first routing
- prompt execution
- response composition
- API exposure

Tambien quedan fuera de alcance la invocacion runtime de `OrderAgent`, `RefundAgent` o `ClarifierAgent`, la validacion de outputs JSON de negocio y cualquier respuesta final al usuario.

## Configuracion

`appsettings.json` conserva solo configuracion de bootstrap:

```json
{
  "CasoDCode": {
    "ProjectEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "ModelDeploymentName": "<deployment-name>",
    "OrderAgentId": "OrderAgent:5"
  }
}
```

## Estructura principal

```text
Program.cs
CasoDCodeSettings.cs
Agents/
  AgentNames.cs
  AgentInstructionLoader.cs
  RefundAgentFactory.cs
  ClarifierAgentFactory.cs
  Definitions/
    refund-agent-casee.instructions.txt
    clarifier-agent-casee.instructions.txt
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

## Build y ejecucion

Compilar:

```powershell
dotnet build
```

Ejecutar bootstrap:

```powershell
dotnet run
```

La aplicacion no acepta prompts de negocio ni argumentos de runtime.

## Salida esperada

```text
[CONFIG] Endpoint validated
[CONFIG] Deployment validated: gpt-4.1
[VALIDATION] Project access validated
[VALIDATION] OrderAgent validated: Name=OrderAgent, Version=5, Id=OrderAgent:5
[RECONCILE] refund-agent-casee => created|updated|unchanged
[RECONCILE] clarifier-agent-casee => created|updated|unchanged
[SUMMARY] Endpoint: https://<resource>.services.ai.azure.com/api/projects/<project>
[SUMMARY] Deployment: gpt-4.1
[SUMMARY] OrderAgent => validated | Name=OrderAgent | Version=5 | Id=OrderAgent:5
[SUMMARY] RefundAgent => created|updated|unchanged | Name=refund-agent-casee | Version=<version> | Id=<refund-agent-id>
[SUMMARY] ClarifierAgent => created|updated|unchanged | Name=clarifier-agent-casee | Version=<version> | Id=<clarifier-agent-id>
[SUMMARY] Bindings => OrderAgent=OrderAgent:5; RefundAgent=<refund-agent-id>; ClarifierAgent=<clarifier-agent-id>
[SUMMARY] Foundry bootstrap completed
```

## Notas

- El proyecto permanece en .NET 8.
- No se introducen Workflows.
- No se introduce `ManagerAgent`.
- No se introducen tools tipo `agent`.
- No existe routing ni consumo runtime en el path principal.
