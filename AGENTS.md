# TheSeries

A .NET 10 [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) sample: an AI "WikiAssistant" agent backed by Azure OpenAI that answers questions using Wikipedia as a tool.

See [README.md](README.md) for a user-facing overview, prerequisites, and the `POST /chat` API.

## Architecture

Four projects, orchestrated by Aspire (solution: [TheSeries.slnx](TheSeries.slnx)):

| Project | Role |
|---------|------|
| [src/TheSeries.AppHost](src/TheSeries.AppHost/AppHost.cs) | Aspire orchestrator. Wires up resources, injects Azure OpenAI config, sets service references. |
| [src/TheSeries.AiService](src/TheSeries.AiService/Program.cs) | ASP.NET Core minimal-API service exposing `POST /chat`. Hosts the agent. |
| [src/TheSeries.Console](src/TheSeries.Console/Program.cs) | Interactive console client that calls the AI service via service discovery. |
| [src/TheSeries.ServiceDefaults](src/TheSeries.ServiceDefaults/Extensions.cs) | Shared OpenTelemetry, health checks, resilience, and service discovery. Referenced by every service. |

Key flow: Console → `POST /chat` on AiService → `ChatClientAgent` (Azure OpenAI) → calls `WikiTool` functions → answers. The agent and its setup live in [AgentService.cs](src/TheSeries.AiService/AgentService.cs); the Wikipedia tool in [WikiTool.cs](src/TheSeries.AiService/WikiTool.cs).

## Build and Run

- Build: `dotnet build TheSeries.slnx`
- Run everything (launches the Aspire dashboard): `dotnet run --project src/TheSeries.AppHost`
- The Console is registered with `WithExplicitStart()`, so start it manually from the Aspire dashboard. It needs an attached terminal for stdin.

## Configuration

Azure OpenAI settings are read from the **AppHost user-secrets** and injected into the AI service as environment variables. Set them on the AppHost project:

```
dotnet user-secrets set "AzureOpenAI:Endpoint" "<url>" --project src/TheSeries.AppHost
dotnet user-secrets set "AzureOpenAI:Deployment" "<deployment>" --project src/TheSeries.AppHost
dotnet user-secrets set "AzureOpenAI:ApiKey" "<key>" --project src/TheSeries.AppHost
```

Missing config throws at agent creation (`AgentService.Required`). Never commit secrets.

## Conventions

- Target framework `net10.0`; `Nullable` and `ImplicitUsings` enabled across all projects.
- Use top-level statements in `Program.cs` and minimal APIs (no controllers).
- DTOs are `internal sealed record` types declared at the bottom of the file that uses them.
- Agent capabilities are plain methods annotated with `[Description]` (on the method and each parameter) and exposed via `AIFunctionFactory.Create(...)` in `WikiTool.AsTools()`. Add new tools the same way.
- Services reach each other by Aspire resource name (e.g. `https+http://aiservice`) through service discovery, not hardcoded URLs.
- The agent is stateless and registered as a singleton.

## Documentation

- **Keep documentation present and up to date.** Whenever you change behavior, structure, or conventions, update the relevant docs in the same change so they never drift from the code.
- Update **this [AGENTS.md](AGENTS.md)** when you add/remove a project, change build/run commands, alter the architecture or dependency flow, or introduce a new convention.
- Add or update **XML doc comments** (`/// <summary>`) on public types and members — if no existing comment examples exist, add add replect example in here
- When adding a new endpoint, service, or cross-host component, document its purpose and any non-obvious behavior (e.g. that correct answers are never exposed).
- If you introduce project-level docs (README, `docs/**`), link to them from this file rather than duplicating content here.