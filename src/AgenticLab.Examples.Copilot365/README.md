# Copilot 365

An opt-in workplace Copilot example with chat, researcher and analyst agents. The module owns its
personas, host prompt, tools, synthetic Microsoft 365 data, branding, resource/risk metadata and tests. It uses
the existing chat API and Flow UI, with no custom panel, extra service or module-specific endpoint.
This is a representative teaching sample, not Microsoft's product implementation or system prompt.

## Enable

AppHost enables this module in Development. Use `--Examples:copilot365:Enabled=false` to disable it.
For other environments, enable it from the repository root with the normal [Azure OpenAI setup](../../README.md):

```sh
dotnet run --project src/AgenticLab.AppHost -- --Examples:copilot365:Enabled=true
```

Open **web** in Aspire and select **Copilot 365**, then **chat**, **researcher** or **analyst**.
When disabled, the module contributes no agents, host, tools or manifest. Existing saved host
selections fall back to Default when the module is disabled. For standalone processes,
set `Examples__copilot365__Enabled=true` on both AiService and Web: Web loads the manifest for resource
and risk presentation even though the module does not need a panel.

The module/configuration ID is **`copilot365`**. The API host key remains **`microsoft365`** to preserve
chat requests and saved selections. The agent names and `Agents:<name>:Deployment` model overrides
are unchanged. General Microsoft 365 Learn content remains available independently of the example.

## Agents And Tools

| Mode | API agent name | Tools |
| --- | --- | --- |
| chat | `M365Copilot` | `SearchEmail`, `SearchFiles`, `SearchChats`, `GetCalendar`, `FindPeople`, `SummarizeDocument`, `SendMail` |
| researcher | `M365Researcher` | `SearchEmail`, `SearchFiles`, `SearchChats`, `FindPeople`, `SummarizeDocument`, `SearchWiki`, `GetWikiPage` |
| analyst | `M365Analyst` | `SearchFiles`, `SummarizeDocument`, `Calculate` |

Try asking what blocks the Q3 launch, researching the SSO requirement, or calculating the underspend
on the FY26 budget. None of these agents needs a workspace or supports workspace skills.
Per-run disabled-tool settings still narrow the selected agent's tools; disabling `SendMail` makes
the chat mode read-only.

The researcher reuses the host's existing Wikipedia implementation, which makes public HTTPS calls.
The analyst reuses its in-process calculator. Both request only their exact function names through
`IHostToolSource`; an unavailable name is an error, never permission to add another tool. The production
module references only [Extensibility](../AgenticLab.Extensibility), not an executable host.

## Synthetic Data And Safety

[Microsoft365SampleData](Data/Microsoft365SampleData.cs) contains a fixed, illustrative tenant:
five emails, four documents, four Teams messages, four meetings and four people. Dates and relative
labels such as "Today" are fixture text, not a live calendar. Searches match terms case-insensitively;
missing evidence stays missing rather than being generated.

The workplace tools never call Microsoft Graph or access a real account. `SendMail` is also a
simulation: it checks for a nonblank recipient containing `@` and rejects a message whose subject
and body are both blank. It returns a fictional receipt without delivering mail or mutating the data.
The existing prompt and tool description deliberately model a real send action, and its **Medium**
risk label illustrates that action's consequences. Asking the model to confirm the message is an
instruction, not a host-enforced approval gate; the sample must not be wired to real mail as-is.

## Registration And API

[Copilot365Example](Copilot365Example.cs) implements `IAiServiceExample`. AiService registers its
three `IAgentDefinition` instances and `IVendorHarness` only when enabled. Web registers the same
module for metadata only; `RequiresUi=false`, with no `IWebExample`, MCP or A2A contribution.
Its manifest owns the Microsoft 365 resource, `SendMail` risk description and host presentation.
The logo is a local RCL asset under `wwwroot/host.svg`; ordering, the shared Learn concept link and
the legacy `Microsoft365` selection alias are declared alongside it, not in core Web switches.

Use the **aiservice** URL from Aspire with [Copilot365.http](Copilot365.http). `GET /examples` exposes
the `copilot365` manifest. The three `/agents` entries and `/vendors` host carry
`exampleId: "copilot365"` and `requiresExampleUi: false`. Requests still specify an agent such as
`M365Analyst` and `vendor: "microsoft365"`; a vendor alone does not select an agent.
The catalogue and harness requests do not invoke a model. The `.http` chat requests do.

## Verification

Credential-free checks from the repository root:

```sh
dotnet test src/AgenticLab.Examples.Copilot365/Tests/AgenticLab.Examples.Copilot365.Tests.csproj
dotnet test tests/AgenticLab.AiService.Tests/AgenticLab.AiService.Tests.csproj --filter FullyQualifiedName~ExampleExtensionTests
dotnet test tests/AgenticLab.Web.Tests/AgenticLab.Web.Tests.csproj --filter FullyQualifiedName~ExampleHosts
```

The module tests cover default-off and explicit-off registration, panel-less Web metadata, stable
identities/modes, exact tool subsets, fixture lookups and simulated-mail validation. The host tests
cover bounded tool publication and legacy/current saved-host preferences. No test sends mail, calls
Microsoft Graph or invokes an Azure model. Wikipedia invocation is stubbed in the host tests.
Nested `Tests/**` items are excluded from the production RCL.

For browser verification, use isolated contexts and the setup in [tools/README.md](../../tools/README.md).
Check both enabled and disabled runs: the host should appear only when enabled, its three modes
should be selectable, and `SendMail` should show Medium risk with the simulation boundary. Check
desktop and mobile widths without submitting chat. The unchanged `Microsoft365` and `microsoft365`
saved selections should restore when enabled and fall back when disabled.