# ChatGPT example

An opt-in ChatGPT-style agent host. This module owns the representative host prompt, `ChatGpt`
agent, branding and tests. It is not OpenAI's actual system prompt or a separate model provider.
The configured model provider is still used; the model label is illustrative. Direct OpenAI API
access is selected through [server configuration](../../README.md#configure-a-model-provider), not this host.

AppHost enables this module in Development. Elsewhere, enable it from the repository root:

```sh
dotnet run --project src/AgenticLab.AppHost -- --Examples:chatgpt:Enabled=true
```

Use `--Examples:chatgpt:Enabled=false` to disable the Development default.

The stable API host key is `chatgpt`; its `chat` mode selects `ChatGpt`. The agent requests only
`SearchWiki`, `GetWikiPage` and `Calculate` from the shared `IHostToolSource`. The executable host
owns their implementations and schemas. No file/terminal access, custom panel, endpoint or protocol
role is added. AiService and Web register this module independently; Web loads metadata only.
Saved `ChatGpt` selections remain compatible when enabled and fall back to Default when disabled.

Credential-free checks:

```sh
dotnet test src/AgenticLab.Examples.ChatGpt/Tests/AgenticLab.Examples.ChatGpt.Tests.csproj
```

See [example modules](../../docs/examples.md) for ownership and registration conventions.