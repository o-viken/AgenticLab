# GitHub Copilot example

Opt-in, representative GitHub Copilot host prompt and branding, not GitHub's actual system prompt
or a provider integration. The configured Azure OpenAI deployment is unchanged; the model label is
illustrative. This module owns no agents: `ask`, `plan` and `agent` reuse the core `Ask`, `Plan` and
`Coder` agents through `SharedAgentNames`, including their workspace confinement and tool controls.

AppHost enables this module in Development; `--Examples:copilot:Enabled=false` disables that default.
For other environments, enable it explicitly:

```sh
dotnet run --project src/AgenticLab.AppHost -- --Examples:copilot:Enabled=true
dotnet test src/AgenticLab.Examples.Copilot/Tests/AgenticLab.Examples.Copilot.Tests.csproj
```

The module ID and API host key remain `copilot`. Saved `Copilot` selections work when enabled and
fall back to Default when disabled. AiService registers the harness; Web loads branding metadata.
No custom panel, endpoint, tool, MCP or A2A contribution is added. Tests require no credentials.
See [example modules](../../docs/examples.md) for registration and ownership conventions.