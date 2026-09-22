# Claude Code example

Opt-in, representative Claude Code host prompt and branding, not Anthropic's actual system prompt
or a provider integration. The configured Azure OpenAI deployment is unchanged; the model label is
illustrative. The `plan` and `agent` modes reuse the core `Plan` and `Coder` agents through
`SharedAgentNames`, including their workspace confinement and tool controls. This module owns no agents.

```sh
dotnet run --project src/AgenticLab.AppHost -- --Examples:claude-code:Enabled=true
dotnet test src/AgenticLab.Examples.ClaudeCode/Tests/AgenticLab.Examples.ClaudeCode.Tests.csproj
```

The module ID and API host key remain `claude-code`. Saved `ClaudeCode` selections work when enabled
and fall back to Default when disabled. AiService registers the harness; Web loads branding metadata.
No custom panel, endpoint, tool, MCP or A2A contribution is added. Tests require no credentials.
See [example modules](../../docs/examples.md) for registration and ownership conventions.