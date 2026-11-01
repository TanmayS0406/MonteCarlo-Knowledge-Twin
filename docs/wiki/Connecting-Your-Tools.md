# Connecting Your Tools (all optional)

The app works fully on its own: it has its own simple task board, it reads transcripts you paste in, and it understands them with built-in text rules. **You do not need any of the connections below to try it.**

When you are ready to hook it up to real tools, everything is configured in one file:

`MonteCarlo.Backend/src/MonteCarlo.Api/appsettings.json`

Leave a setting blank to keep that feature switched off. After editing the file, restart the backend. The **Privacy & settings** screen shows which connections are active.

> **Keep secrets out of Git.** Do not commit real keys. Set them as environment variables instead. For example, `Llm__ApiKey` overrides `Llm:ApiKey` (a double underscore stands for the nesting).

---

## AI understanding of transcripts (the `Llm` section)

| | |
|---|---|
| **Out of the box** | A built-in set of text rules finds decisions, promises and blockers. Free, offline, instant. |
| **Optional upgrade** | Use an AI model for smarter understanding. Set `Provider` to `OpenAI` (any OpenAI-compatible service) or `AzureOpenAI`, then fill in `ApiKey` and `Model` (or `AzureDeployment`). |
| **Safety net** | The AI must answer in a strict format. If it fails or returns nonsense, the app **falls back to the built-in rules** instead of breaking. |

Meetings marked **Opt out** are never sent to the AI.

## Jira (the `Jira` section)

Fill in `BaseUrl`, `Email` and `ApiToken`. Then tickets sync from Jira, and when you **Accept** a suggestion, the change is written back to Jira. Without this, the app uses its own local board.

## GitHub (the `GitHub` section)

Fill in `Owner`, `Repo` and `Token`, and the app pulls recent commits and pull requests every hour. That is how it knows a ticket's code was merged. Prefer push over pull? Point a GitHub webhook at `POST /api/webhooks/github`.

## Microsoft Teams (the `Graph` section)

Fill in `TenantId`, `ClientId`, `ClientSecret`, `UserId` and `NotificationUrl` (the public HTTPS address of `/api/webhooks/graph`). Your organisation's admin must grant the *OnlineMeetingTranscript.Read.All* permission. After that, transcripts of finished Teams meetings arrive automatically, and the app renews its subscription on its own.

Without this, upload transcripts by hand on the **Meetings** page.

---

## Privacy controls

| Control | Where | What it does |
|---|---|---|
| **Delete raw transcripts after N days** | Privacy & settings | The full transcript is removed after the period. The extracted decisions and short quotes stay. Default: 30 days. |
| **Opt out of a meeting** | Meetings page | The meeting is stored only; nothing is extracted. |
| **Capture by default** | Privacy & settings | Whether new Teams meetings are captured automatically. |
| **Audit log** | Privacy & settings | A record of every change, including who accepted each AI suggestion. |
| **Data region label** | Privacy & settings | Documents where your data is kept. |
