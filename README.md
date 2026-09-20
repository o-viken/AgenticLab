# Agentic Lab

## What Is It?

Agentic Lab is an educational application for understanding how AI agents work. It combines
guided lessons with a live workspace where real agents answer questions, call tools, and expose
the requests and results behind their answers.

It is for learners, educators, developers, and teams evaluating agentic systems. Built with
[.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0),
[Aspire](https://aspire.dev/), and Azure OpenAI, it includes a Blazor web app, a console client,
and an optional React frontend.

You can:

- Follow guided lessons about agents, context, tools, and execution boundaries.
- Inspect captured model requests, responses, tool arguments, and results.
- Pause and step through real execution, then revisit captured exchanges without running them again.
- Explore workspace agents, skills, instructions, MCP tools, and A2A delegation.

## Why It Exists

A chat box shows the question and answer, but hides much of the system in between. Agentic Lab
makes that system visible so you can understand what an agent knows, what it can do, and what
controls its actions.

The central idea is **Agent = Agent host + Model**. The model chooses a next step or an answer;
the host manages context, instructions, memory, tools, and execution controls. A model's request
to use a tool is not permission to execute it.

Captured activity shows what the application actually sent and received, not the model's private
reasoning. Inference, embedding, and neural-network illustrations are labelled simulations.
Read the [vision](VISION.md) for the project's purpose and direction.

## Prerequisites

For the full local application:

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- [Aspire CLI](https://aspire.dev/get-started/install-cli/) matching the pinned **13.5.4** version.
  The AppHost restores its Aspire SDK automatically and uses the SDK-paired CLI through `dnx`
  as a fallback when a compatible CLI is not on `PATH`.
- An [Azure OpenAI](https://learn.microsoft.com/azure/ai-services/openai/) resource with a deployed
  chat model that supports tool calling, its endpoint, deployment name, and API key.
- Git to clone the repository, or download and extract its source archive.

Node.js **24 LTS** is needed only for the optional React frontend. Docker is not required for
the default local setup. The [learning-only option](#learning-only) needs just the .NET SDK
and the source code, without Azure credentials or Aspire orchestration.

## Run Locally

### Get the Source

```sh
git clone https://github.com/o-viken/agenticlab.git
cd agenticlab
```

Run the following commands from the repository root.

### Configure Azure OpenAI

Store your settings in the AppHost's local user-secrets store:

```sh
dotnet user-secrets set "AzureOpenAI:Endpoint" "<your-azure-openai-endpoint>" --project src/AgenticLab.AppHost
dotnet user-secrets set "AzureOpenAI:Deployment" "<your-deployment-name>" --project src/AgenticLab.AppHost
dotnet user-secrets set "AzureOpenAI:ApiKey" "<your-api-key>" --project src/AgenticLab.AppHost
```

Use the **deployment name** you created in Azure, not just the model's name. Aspire passes these
settings to the services that call the model. Never commit credentials. Live runs use your Azure
resource and may incur charges; prompts and selected context are sent to the configured model service.

### Build and Start

Using the .NET CLI:

```sh
dotnet build AgenticLab.slnx
dotnet run --project src/AgenticLab.AppHost
```

Alternatively, use the Aspire CLI from the repository root:

```sh
aspire run
```

Aspire finds the AppHost through [aspire.config.json](aspire.config.json), restores and builds
it and its dependencies, then starts the application. No separate build command is needed to run it.

Open the Aspire dashboard URL printed in the terminal, then open the **web** resource's endpoint.
The dashboard also provides service logs and traces.

Start with a conversation, or select **ChatGPT / chat** and try:

> Find the height of the Eiffel Tower on Wikipedia, then calculate how much taller it is
> than a 250-metre building.

Watch the model and tool activity, inspect the captured data, or use Manual mode to advance one
step at a time. The vendor-labelled experiences are representative demos backed by your Azure
OpenAI deployment, not connections to those vendors' products.

### Learning Only

To explore the guided lessons without configuring Azure OpenAI, run only the Web project:

```sh
dotnet run --project src/AgenticLab.Web
```

Open the listening URL printed in the terminal and visit `/learn`. The guide works without
the AI service; live chat requires the full setup above.

### With the React Frontend

With Node.js **24 LTS** installed and Azure OpenAI configured above, install the frontend
dependencies and start with React enabled:

```sh
npm --prefix src/AgenticLab.React ci
aspire run -- --ReactFrontend:Enabled=true
```

Or use the .NET CLI after installing the same frontend dependencies:

```sh
dotnet run --project src/AgenticLab.AppHost -- --ReactFrontend:Enabled=true
```

Open the **react** resource's endpoint in the Aspire dashboard. The **web** resource still opens
Blazor; React runs alongside it and uses the same AI service through a backend-for-frontend.
See the [React setup guide](docs/react-frontend.md#run-with-aspire) for customization and builds.

### Console Client

The **console** resource does not start automatically. Start it explicitly from Aspire with an
attached terminal for input, or follow the [standalone console instructions](docs/web-flow-page.md#running-the-console).

### Self-Contained Examples

Optional examples are registered as independent projects, keeping their domain code, UI, assets,
tests and documentation together. See the [example catalogue and contributor guide](docs/examples.md)
and the [Windfarm project](src/AgenticLab.Examples.Windfarm/README.md) for an end-to-end process demo,
or [Copilot 365](src/AgenticLab.Examples.Copilot365/README.md) for workplace chat, research and analysis
over synthetic Microsoft 365 data.
Examples are disabled by default and reuse the existing hosts when enabled.

The previously built-in workplace Copilot host and its three agents are now opt-in:

```sh
dotnet run --project src/AgenticLab.AppHost -- --Examples:copilot365:Enabled=true
```

## Documentation

| Guide | What You Will Find |
|-------|--------------------|
| [Agents and API](docs/agents.md) | Agent personas, chat API, conversation memory, prompts, and model configuration. |
| [Web Flow Workspace](docs/web-flow-page.md) | Live visualization, panels, captured context, and execution controls. |
| [Blazor Design System](docs/design-system.md) | Shared tokens, components, local fonts, accessibility, and the development catalogue. |
| [Execution Explorer](docs/execution-explorer.md) | Replay, breakpoints, streaming/control API, and retention limits. |
| [Learning](docs/learning.md) | Guided lessons and the contextual Learn panel. |
| [Workspace Features](docs/workspace.md) | Workspace agents, skills, custom instructions, and file/terminal tools. |
| [Protocols](docs/protocols.md) | MCP tools, A2A agents, discovery, and protocol integration tests. |
| [React Frontend](docs/react-frontend.md) | Optional frontend setup, customization, builds, and browser tests. |
| [Example Modules](docs/examples.md) | Self-contained community examples, extension contracts and registration. |
| [Architecture and Conventions](AGENTS.md) | Project structure and implementation guidance for contributors. |
| [Browser Checks](tools/README.md) | Responsive UI smoke checks and separate browser load-test setup. |

## Contributing

Contributions can improve code, tests, documentation, lessons, or examples.

1. [Open an issue](https://github.com/o-viken/the-series/issues) with a reproducible bug report or a
   proposed improvement. Discuss substantial changes before investing in implementation.
2. Fork the repository and create a branch for your change. Read [AGENTS.md](AGENTS.md) and the
   relevant feature guide before editing.
3. Keep the change focused. Add or update tests for changed behavior and update the matching docs.
4. Run the relevant checks from the repository root:

```sh
dotnet build AgenticLab.slnx
dotnet test AgenticLab.slnx
```

The deterministic agent and protocol tests use a fake model and do not need Azure credentials.
For React changes, also run the [frontend verification steps](docs/react-frontend.md#verification).
For documentation-only changes, check links, commands, and the rendered Markdown.

5. Open a pull request explaining the problem, your change, and how you verified it. Include
   screenshots for UI changes and note any checks you could not run.

Do not include API keys, private workspace content, or sensitive captured prompts in issues,
screenshots, logs, or pull requests.

## License

Originally conceived and created by [o-viken](https://github.com/o-viken). Code and documentation
are licensed under the [Apache License 2.0](LICENSE), except where otherwise noted.
See [NOTICE](NOTICE) for project attribution. Third-party assets retain their own licenses,
including the [Web icons](src/AgenticLab.Web/wwwroot/icons/lucide/LICENSE) and
[React assets](src/AgenticLab.React/public/licenses/). Published container images include the
project license and attribution at `/app/LICENSE` and `/app/NOTICE`.