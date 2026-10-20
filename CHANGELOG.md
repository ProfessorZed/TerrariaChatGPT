# Changelog

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
  item remains private pending moderator approval of the remaining native
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
