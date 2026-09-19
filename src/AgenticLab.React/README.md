# React Flow Workspace

An optional client-rendered example beside the Blazor frontend. React owns presentation; the existing
AiService owns agents and execution. A small ASP.NET Core BFF forwards the same API contracts.

From the repository root, with Node 24 LTS and .NET 10 installed:

```sh
npm --prefix src/AgenticLab.React ci
dotnet run --project src/AgenticLab.AppHost -- --ReactFrontend:Enabled=true
```

Open the **react** resource in Aspire. Blazor's **web** resource remains available separately.

See [the frontend guide](../../docs/react-frontend.md) for standalone development, built-asset hosting,
the five API routes, test commands, scope and customization. Start with
[theme tokens](src/styles/tokens.css) for visual changes, or replace components while retaining
[useFlowRun](src/flow/useFlowRun.ts) and the [API client](src/api/client.ts).

Fonts and icons are bundled locally. Their original licenses are in [public/licenses](public/licenses).
No model credentials or runtime service addresses are bundled into the browser application.
