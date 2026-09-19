## Microsoft 365 Copilot

**Microsoft 365 Copilot** is Microsoft's AI assistant for work. It is the *same* agent
pattern this app visualises — a model wrapped in a harness that assembles context, exposes a
bounded toolset and runs the think-act-observe loop — but its tools reach into your
**workplace** instead of a code repository:

- **Your work content** — it grounds answers in your organization's email, documents
  (OneDrive/SharePoint), Teams chats, calendar and the people directory, retrieved through
  **Microsoft Graph**.
- **In the apps you use** — it lives inside Word, Excel, PowerPoint, Outlook and Teams,
  drafting documents, analysing spreadsheets, building decks and summarising mail and
  meetings.
- **Specialist agents** — beyond general chat, it offers focused agents such as
  **Researcher** (deep, multi-source research) and **Analyst** (data analysis).
- **Copilot Studio** — lets organizations build their own custom agents and connect new
  data sources and actions.

The lesson is that *all* agentic assistants work the same way: a model, a harness and a
**toolset**. Swap the tools — code files for one product, Graph connectors for another — and
you get a different assistant on the same foundation.

## In this application (Agentic Lab)

Pick the **Microsoft 365 Copilot** vendor to see the diagram in its light, gradient-tinted
colours. The vendor offers three modes — **chat** (general work assistant), **researcher**
and **analyst** — mapped to the `M365Copilot`, `M365Researcher` and `M365Analyst` agents.

These agents call a **fake** Microsoft 365 / Graph tool set (`SearchEmail`, `SearchFiles`,
`SearchChats`, `GetCalendar`, `FindPeople`, `SummarizeDocument`) that returns small, canned,
in-memory sample data — there is no real Microsoft Graph or network call. The Researcher mode
also uses the Wikipedia tools for public-web grounding, and the Analyst mode adds the
calculator. Watch the **Tools** box light up as a mode searches your "work content".
