# Privacy and game observations

Terraria ChatGPT connects directly to the selected service from tModLoader.
It has no developer-operated relay, analytics service, or separate companion
process. This describes version 0.9.9.

## What goes online

When you request an online answer, the mod sends your question, a bounded recent
conversation, and its response instructions to the selected provider. Recent
history is held in memory: at most 12 messages, 2,000 characters per message,
and 12,000 characters overall. Clearing chat, leaving the world, or changing
provider resets the outgoing history.

The running Terraria and tModLoader version numbers are included as public
software context, even when observation sharing is disabled.

When **Memory** is enabled, up to 32 saved preferences and item-sorting rules
are also included in online ChatGPT/API requests. This is independent of
**Share observed progress**. Only bounded preference text, topic, item key and
sorting target are sent; local record identifiers, source quotes, timestamps,
character identifiers and save paths are excluded. Saved chest-name preferences
can therefore be sent as preference context, but are not permission to inspect
that chest. Instructions prohibit sending these private preferences to web search.
Pause Memory in its chat panel to stop both learning and sharing existing entries.

**Share observed progress with GPT** is on by default. When enabled, a bounded
summary may include:

- Carried and equipped items and their quantities.
- Single-player chest contents remembered after you opened those chests.
- Remembered names of opened or player-placed chests, including empty chests.
  Names do not assert current proximity, free space or up-to-date contents.
- Recorded world boss defeats and supported NPC rescue flags.
- Biome visits and NPC conversations observed while the mod is active.
- NPC happiness price modifiers, mood explanations, observed biomes and reading
  times collected while speaking to that NPC. Older readings are marked remembered.
- Local account achievement records, identified as account-wide records.

For a request to categorize inventory into chests, the shared context can also
include up to 40 distinct eligible main-inventory item keys and display names,
plus remembered names of chests you opened or placed. These allow GPT to return
specific item-to-chest choices. The local preview's slot-by-slot transfer plan,
current range/capacity checks and storage identity tokens are not sent to GPT.
New GPT sorting choices require this observation-sharing setting; disabling it
does not disable the local category sorter or enabled saved preferences.

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

Memory entries, a short source quote, and an update timestamp are stored in the
character's normal mod data. They follow that character across worlds and normal
character saves/restarts. They are not encrypted credential records: do not put
secrets into chat or memory. Automatic filters reject obvious credentials and
sensitive strings but are not a guarantee of detecting every secret. No complete
chat transcript is persisted by this feature. Undo receipts exist only during
the session. Save & Quit normally; abrupt crashes may lose unsaved changes.

Use Memory to review/remove entries or clear all. Removal, Undo and pausing also
clear the outgoing recent conversation and cancel old requests to stop stale
context from immediately reintroducing a forgotten preference. They cannot recall
data already sent to a provider. `/clear` clears chat without deleting memory.

Goal notes, their positions, remembered chest snapshots, NPC happiness readings, and recorded encounters
are saved with local player/mod data. Goal checks run locally, without repeated
AI calls. Closed-chest snapshots can become stale; they are not live scans.

The **Sort chests** preview is computed locally. Clicking it checks only named,
remembered chests you previously opened or placed and that are within the
normal Quick Stack range. The preview and its local destination identifiers are
not sent to the AI. **Confirm sort** rechecks the reviewed state and transfers
eligible inventory items. It refreshes memories of the affected known chests;
those memories remain subject to the normal observation-sharing setting.

GPT can return a temporary proposal containing exact item keys and known chest
names. **Review sort** and the header's **Sort chests** button use the same active
proposal, which remains in session memory and is not restored after a restart.
It cannot move items without **Confirm sort**. Unassigned items stay untouched.
Before transferring, **Remember choices for future sorting** can save the entire
validated batch as character memories, with an Undo receipt. This action is
separate from transfer confirmation, observes the 32-entry total memory limit,
and saves no partial batch if validation or capacity fails. Saved choices follow
the normal Memory sharing control described above, independently of observation
sharing. Use Save & Quit to retain them with the character.

Known chest identity tokens are saved locally with world/character mod data so
an observed chest can be recognized after restarting. These tokens, tile styles
and coordinates are excluded from GPT's observation projection. Actual local
placement/removal events update the registry without scanning hidden storage.
Previously known chests need no reopening solely because the game restarted;
remembered contents can still be stale, and actual destinations are checked
locally when you request a preview.

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
have discovered. It cannot inspect hidden world data or execute game actions.
There are no assistant tools for spawning, killing, teleporting, editing item
attributes, or changing progress. Accepting a goal adds a note; checking a manual
step changes that note, not the game world. The separate **Sort chests** feature
can transfer items only after the user reviews a local preview and clicks
**Confirm sort**. Model replies, web pages and typed commands cannot approve it.
Protected slots and favorites remain untouched; unknown or unsuitable storage
does not become a destination. Multiplayer sorting is disabled.

Before sharing a diagnostic log, remove private questions, account information,
credentials, and any player/world details. The project's live test is opt-in and
uses the saved account allowance; offline tests use synthetic credentials.
