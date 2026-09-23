# Claude example

Opt-in, representative Claude host prompt and branding, not Anthropic's actual system prompt or a
provider integration. The configured model provider is unchanged; the model label is
illustrative. The `chat` mode reuses the core, tool-free `ChatAgent` through `SharedAgentNames`.
This module owns no agents or tools.

```sh
dotnet run --project src/AgenticLab.AppHost -- --Examples:claude:Enabled=true
dotnet test src/AgenticLab.Examples.Claude/Tests/AgenticLab.Examples.Claude.Tests.csproj
```

The module ID and API host key remain `claude`. Saved `Claude` selections work when enabled and
fall back to Default when disabled. AiService registers the harness; Web loads branding metadata.
No custom panel, endpoint, MCP or A2A contribution is added. Tests require no credentials.
See [example modules](../../docs/examples.md) for registration and ownership conventions.