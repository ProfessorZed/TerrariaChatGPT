# Changelog

## 0.9.9 — GPT sorting choices, persistent memory and world context

This release includes the sorting, memory, NPC happiness and version-aware
answers introduced in local builds 0.9.5–0.9.8 below.

- Requests such as “Categorise them according to my chests” produce validated
  item-to-chest choices, with **Review sort** below the answer. The header's
  **Sort chests** button reviews the same active choices.
- Refresh preserves those exact destinations. Unassigned, protected, out-of-range
  or capacity-blocked items remain in inventory; only **Confirm sort** transfers
  the reviewed items. Completed and stale plans cannot be replayed.
- **Remember choices for future sorting** saves the reviewed batch before a
  transfer, with a visible receipt and Undo. Saving is separate from moving items
  and either saves the whole batch within the 32-entry memory limit or saves none.
- Previously opened or placed chests remain known after normal saves/restarts;
  no reopening is required just to make their names or identities available.
- Improved short-screen previews and scrolling; exact blocked destinations remain
  readable beside their reasons. Added concise 0.9.9 hover notes.

## 0.9.8 — Local build: memory edits and smarter sorting

- Patch-note and reply tooltips render above the Model/Effort controls.
- Natural corrections update existing memories with a visible update notice and Undo.
- Category preferences match combined chest labels: Vanity matches Vanity & Paints.
- Item-family sorting rules support grappling hooks and travel/mobility destinations.
- Previously opened or player-placed chests remain known across restarts; sorting
  still checks the nearby destination and requires preview plus Confirm sort.
- Empty known chest names can inform GPT with observation sharing enabled, without
  exposing coordinates, hidden storage or claiming that a chest is nearby.
- Existing category-like chest-name memories are corrected when their saved
  evidence identifies a category. Explicit exact chest preferences stay exact.

## 0.9.7 — Local build: contextual memory

- Shows the loaded mod version in GPT chat; hover for concise patch notes.
- Saved per-character preferences across worlds and normal character saves/restarts.
- Contextual learning from ordinary chat, with source-grounded validation, bounded
  topics/entries, replacement of old preferences and visible Memory saved/Undo.
- Memory manager for review, individual removal, pause/resume and confirmed clear-all.
- Remembered item category and exact chest-name corrections affect sorting previews
  without bypassing transfer confirmation, range, capacity or item protection.
- Reject transient state, one-off goals, obvious credentials and unsupported
  item identities. Changed or removed memories invalidate old requests/previews.
- Normal chat clearing preserves memory; forgetting/pause clears outgoing history
  to prevent stale conversational copies from reappearing as memory.

## 0.9.6 — Local build: named-chest sorting with a preview

- Added **Sort chests** in GPT chat with an item/destination preview, explicit
  **Confirm sort**, cancel and refresh controls. Typing a request cannot move items.
- Routes eligible regular inventory items by named chest categories or dedicated
  item names within the installed game's Quick Stack radius. Protects the hotbar,
  favorites, equipment, dedicated coin/ammo slots, cursor and personal banks.
- Keeps unmatched items in inventory, flags the reason and recommends a chest
  name. Full or unsupported destinations never become an automatic dump target.
- Revalidates inventory, chest identity/content, range, world and capacity before
  committing the reviewed plan. Changed previews require another review.
- Single-player only; destination chests must be opened in the current session.
  Sorting stays local and does not grant the model game-action tools.
- Prepared for local testing; no GitHub or Workshop release is implied.

## 0.9.5 — Local build: NPC happiness and game versions

- Records the happiness price multiplier and the game's own mood explanation
  while interacting with an NPC. Keeps bounded memories for each character/world.
- Shares current versus remembered readings and observation times with GPT, so it
  can explain price changes and suggest version-appropriate housing improvements.
  Missing observations are unknown, not assumed normal prices.
- Captures Terraria's runtime version and tModLoader's runtime version separately
  for every question, including when progress sharing is disabled. Answers and web
  searches default to the running Terraria version rather than the newest release.
- Keeps happiness data within the existing observation-sharing control. No hidden
  NPC scans, housing edits, price changes, or new game-action tools are added.
- Prepared for local testing; this entry does not indicate a GitHub or Workshop release.

## 0.9.4 — Model and effort controls in chat

- Added model and effort pickers directly inside the GPT chat window. Selections
  save immediately and apply to new questions while preserving the conversation.
- Effort choices follow the selected model's available capabilities. Unsupported
  efforts reset visibly to Auto when switching models in chat.
- Added an effort selector in mod settings, along with validation before sending
  requests. Higher reasoning settings receive longer bounded answer timeouts.
- Includes the model selection and silent reply notifications introduced in the
  local 0.9.3 test build. Saved login, web search and goals are preserved.

## 0.9.3 — Local test build: models and reply notifications

- Added an account-specific ChatGPT model selector with an Auto option, explicit
  list refresh and saved selection. Model changes apply to the next question.
- Added a small silent reply preview while GPT chat is closed. It fades out after
  seven seconds, leaving an unread `*` until GPT chat opens. It does not capture
  input, clicks or the mouse wheel, and does not open the chat automatically.
- Includes completion notifications for connection errors, without flagging
  local help and status notices as new replies. Unread state resets with chat
  clearing, world changes and account changes.
- This version was tested locally; its changes are included in the 0.9.4 release.

## 0.9.2 — Protected storage and copy-link sign-in

- Moved encrypted ChatGPT login and optional API-key storage under the active
  tModLoader save directory, including custom save locations. The installer can
  preserve an existing encrypted native login without decrypting it.
- Removed runtime access to old companion credential files and external paths.
- Replaced browser launching with explicit copy-link controls for sign-in,
  account usage, and web sources. Sign-in links omit saved identity tokens and
  email hints; OAuth state, PKCE, identity verification and timeout remain.
- Hardened credential permissions, linked-path handling, atomic writes and
  release packaging. Added regression checks for storage and migration failures.
- Kept saved login, web search, goals and fair-play restrictions. The Workshop
  item was initially private for review and was made public at the owner's
  request. This does not imply moderator approval of the disclosed native
  encryption and local sign-in callback behavior.

## 0.9.1 — Fix loading after the rename

- Fixed the top-level code namespace to match `TerrariaChatGPT`, as required by
  tModLoader's loader. Version 0.9.0 compiled but was rejected at startup.
- Added compiled-mod identity validation to the build to catch this before
  installation and publication.
- Kept legacy encrypted login paths and player-data migration identifiers intact.

## 0.9.0 — Terraria ChatGPT

- Prepared the public release under the distinct name **Terraria ChatGPT** and
  internal mod identifier `TerrariaChatGPT`.
- Added release artwork, including the mod icon and Steam Workshop preview.
- Added public installation, build, privacy, publishing, and licensing documents.
- Retained native ChatGPT sign-in, optional API access, live search, highlighted
  conversations, and multiple persistent goal notes.

The earlier local development build was named Terraria GPT. It is separate from
the older mod of that name already available in the community.

## 0.8.1 — Chat scroll fix

- Scrolling inside the GPT conversation no longer also changes the selected
  hotbar item. Normal wheel behavior remains available outside the chat panel.

## 0.8.0 — Runs inside tModLoader

- Moved account sign-in, encrypted credentials, online responses, and search into
  the mod. No Python or Node companion needs to start with the game.
- Added saved-login restoration and native connection error handling.
- Kept optional API access explicit, with no automatic switch to API billing.

## Earlier development

- Added separate Terraria/GPT tabs, adjustable layout, timestamps, highlighted
  answers, copy controls, question recall, and recent conversation context.
- Added live web citations and optional observation summaries.
- Added up to six goal notes with local progress, character/world persistence,
  draggable placement, collapse controls, and manual fallback steps.
- Added installed recipe lookup and single-player memories of opened chests.
