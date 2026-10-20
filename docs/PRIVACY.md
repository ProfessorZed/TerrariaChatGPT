# Privacy and game observations

Terraria ChatGPT connects directly to the selected service from tModLoader.
It has no developer-operated relay, analytics service, or separate companion
process. This describes version 0.9.2.

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
those queries. Clicking a source copies its link. You decide whether to open it
in a browser; the mod does not launch external programs.

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
Windows account and stored separately from normal mod settings, under
`TerrariaChatGPT` inside the active tModLoader save directory (`Main.SavePath`).
Custom save directories are respected. The credential folder is restricted to
the current Windows user and SYSTEM. The mod copies an OpenAI sign-in link only
when you request it; you paste it into your browser. It uses a temporary loopback
callback listener to complete login. No persistent companion server is required.

The mod no longer reads legacy companion files or probes outside the active save
directory. The separately run installer can copy an old encrypted native login
without decrypting it; it never imports legacy JSON and never overwrites a newer
destination login. Installer copies leave the original files available until
the owner verifies the new connection and removes them. Do not include any
credential directories, saved player/world files, or private logs in uploads.

Use **Sign out** to disconnect the saved account, and **Clear key** to remove the
saved API key. **Copy usage link** copies the address of ChatGPT's usage settings. Removing
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
