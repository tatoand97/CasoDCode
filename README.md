# CasoDCode - Bootstrap / IaC Logico de Foundry

`CasoDCode` es un repositorio de bootstrap / IaC logico para Azure AI Foundry sobre .NET 8.

Su responsabilidad es preparar y validar bindings de agentes dentro de un proyecto Foundry. Este repo no consume agentes, no enruta prompts, no ejecuta flujo de negocio y no actua como runtime.

## Que hace este repo

- Carga `CasoESettings` desde `appsettings.json`
- Valida el formato de `ProjectEndpoint`
- Valida acceso al proyecto Foundry
- Valida que `ModelDeploymentName` exista y sea accesible
- Valida que el agente externo configurado en `OrderAgentId` exista y pueda referenciarse
- Reconcilia `refund-agent-casee`
- Reconcilia `clarifier-agent-casee`
- Imprime un resumen final con endpoint, deployment, ids, versiones y bindings
- Termina con codigo `0` si todo sale bien

## Modelo de ownership

- `OrderAgent` es externo a este repo. `CasoDCode` solo valida que `OrderAgentId` exista y pueda enlazarse.
- `RefundAgent` se define en este repo y se reconcilia en Foundry como `refund-agent-casee`.
- `ClarifierAgent` se define en este repo y se reconcilia en Foundry como `clarifier-agent-casee`.

## Este repo no es

- una API
- una consola de consumo
- un runtime de negocio
- un orquestador code-first

El consumo real de prompts y el flujo runtime deben vivir en otro repo o servicio.

## Out of scope

- runtime consumption
- code-first routing
- prompt execution
- API exposure

Tambien quedan fuera de alcance:

- invocacion runtime de `OrderAgent`
- invocacion runtime de `RefundAgent`
- invocacion runtime de `ClarifierAgent`
- construccion de respuestas finales al usuario
- validacion JSON de outputs runtime
- smoke tests funcionales de negocio

## Configuracion

El repo usa la seccion `CasoE` dentro de `appsettings.json`:

```json
{
  "CasoE": {
    "ProjectEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "ModelDeploymentName": "<deployment-name>",
    "OrderAgentId": "OrderAgent:5"
  }
}
```

## Estructura principal

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

Ejemplo:

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

## Notas

- El proyecto permanece en .NET 8.
- No se introducen Workflows.
- No se introduce `ManagerAgent`.
- No se introducen tools tipo `agent`.
- El runtime/consumo debe vivir fuera de este repositorio.
