# Privacy and game observations

Terraria ChatGPT connects directly to the selected service from tModLoader.
It has no developer-operated relay, analytics service, or separate companion
process. This describes version 0.9.0.

## What goes online

When you request an online answer, the mod sends your question, a bounded recent
conversation, and its response instructions to the selected provider. Recent
history is held in memory: at most 12 messages, 2,000 characters per message,
and 12,000 characters overall. Clearing chat, leaving the world, or changing
provider resets the outgoing history.

**Share observed progress with GPT** is on by default. When enabled, a bounded
summary may include:

- Carried and equipped items and their quantities.
- Single-player chest contents remembered after you opened those chests.
- Recorded world boss defeats and supported NPC rescue flags.
- Biome visits and NPC conversations observed while the mod is active.
- Local account achievement records, identified as account-wide records.

It does not send your player name, world identifier, world seed, map, unexplored
locations, unopened-chest contents, or off-screen entity locations. Multiplayer
chest memory is disabled. What you type yourself is still sent as conversation
text; these exclusions cannot remove private information you put in a question.

Turn off **Share observed progress with GPT** to exclude observations from
ChatGPT/API requests. Changing the setting cancels the active request and clears
outgoing conversation history. It cannot recall information already sent.

When **Allow live web search** is enabled, the chosen provider can generate
related search queries using the conversation. Its search service may receive
those queries. Clicking a source opens that website in your browser.

**Wiki only** sends public article titles or your typed wiki search to
`terraria.wiki.gg`. Its separate **Include observed items in Wiki only mode**
setting controls local inventory assistance; item and chest snapshots are not
sent to the wiki.

The selected provider's own terms, privacy settings, and data handling apply to
information it receives. The mod does not make a separate data-retention promise
on that provider's behalf.

## What stays on your computer

Goal notes, their positions, remembered chest snapshots, and recorded encounters
are saved with local player/mod data. Goal checks run locally, without repeated
AI calls. Closed-chest snapshots can become stale; they are not live scans.

ChatGPT sign-in credentials and optional API keys are encrypted for the current
Windows account and stored separately from the normal mod settings. The mod
opens OpenAI's sign-in page in your browser and uses a temporary loopback callback
listener to complete the login. No persistent companion server is required.

For compatibility with earlier local builds, encrypted credential directories
may still contain the older `TerrariaGPT` name. Do not include those directories,
credential files, or saved player/world files in source uploads or issue reports.
The public repository contains neither account credentials nor saved game data.

Use **Sign out** to disconnect the saved account, and **Clear key** to remove the
saved API key. **Manage usage** opens ChatGPT's account usage settings. Removing
the `.tmod` alone is not a request to erase saved player data or credentials.

## Fair-play boundary

The assistant can provide general public game knowledge regardless of what you
have discovered. It cannot inspect hidden world data or perform game actions.
There are no assistant tools for spawning, killing, teleporting, changing items,
or editing progress. Accepting a goal adds a note; checking a manual step changes
that note, not the game world.

Before sharing a diagnostic log, remove private questions, account information,
credentials, and any player/world details. The project's live test is opt-in and
uses the saved account allowance; offline tests use synthetic credentials.
