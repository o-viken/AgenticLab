# Gemini example

Opt-in, representative Gemini host prompt and branding, not Google's actual system prompt or a
provider integration. The configured Azure OpenAI deployment is unchanged; the model label is
illustrative. The `chat` mode reuses the core, tool-free `ChatAgent` through `SharedAgentNames`.
This module owns no agents or tools.

```sh
dotnet run --project src/AgenticLab.AppHost -- --Examples:gemini:Enabled=true
dotnet test src/AgenticLab.Examples.Gemini/Tests/AgenticLab.Examples.Gemini.Tests.csproj
```

The module ID and API host key remain `gemini`. Saved `Gemini` selections work when enabled and
fall back to Default when disabled. AiService registers the harness; Web loads branding metadata.
No custom panel, endpoint, MCP or A2A contribution is added. Tests require no credentials.
See [example modules](../../docs/examples.md) for registration and ownership conventions.