# Terraria ChatGPT 0.9.2 acceptance checks

These are manual expectations, not a claim of completed gameplay testing.
See [the changelog](../CHANGELOG.md) for release scope. Record actual compiler,
automated, live-provider, and gameplay results separately when verifying a release.

Record game/mod version, provider, result and unexpected behavior. Use synthetic
observations and dummy credentials for payload inspection; never log real secrets.

## Ordinary chat and search

| Check | Procedure | Expected |
| --- | --- | --- |
| Original flight issue | Ask “what can i use to fly in pre hardmode”. | Practical options without an excerpt/evidence refusal. No hidden-world claims. |
| Source lookup | Ask to search the official wiki for Rocket Boots. | Normal answer with genuine clickable inline sources and search status. No long link footer. |
| Source click | Hover/click a source; repeat after scrolling and resizing. | Correct source title/URL copied only on click. No browser launches or stale hitboxes. |
| Search off | Disable Live web search, ask a basic question. | Ordinary GPT answer with no fabricated search status. |
| Search failure | Mock an unsupported search tool or timeout. | Clear error; no switch to paid API/wiki and controls recover. |
| Recipe direction | Ask how to make Blade of Grass, then what can be made using it. | Full recipe and appropriate Night's Edge follow-up; no reversed template. |
| General topic | Ask about seasons or another unrelated topic. | Normal answer; no irrelevant inventory dump or goal proposal. |
| Follow-up | Ask a question, follow with “why?”, then change subject. | Recent conversation works without forcing a stale item topic. |
| Readability | Request a recipe/location answer, resize, scroll. | Cyan items, gold locations, green observed supplies/orange deficits, readable timestamps and wrapping. |

## Goal acceptance and controls

| Check | Procedure | Expected |
| --- | --- | --- |
| Explicit acceptance | Say “I want to make a Blade of Grass”. | Useful answer plus Set goal?; no note until clicked. |
| Several notes | Accept a craft, boss and manual goal. | Independent notes; up to six, seventh gives clear limit. |
| Duplicate | Accept the same semantic goal again, including from an old reply. | Reuses the existing note rather than creating duplicates. |
| Inventory | Open inventory and craft while notes are visible. | Notes stay visible; default placement avoids controls where space allows. Clicking a note does not move/use items behind it. |
| Drag/scroll | Drag, collapse and expand; scroll a long note. | Position stays within viewport; only hovered note scrolls. |
| Pointer boundary | Click/scroll outside a note; overlap two notes deliberately. | Normal game controls outside notes; topmost overlapping note owns pointer. |
| Remove | X one of several notes; save/re-enter world. | Only that note is removed and stays removed. |
| Persistence | Move/collapse/check a manual step, save/re-enter. | Note and state persist for this character/world. |
| World/character isolation | Switch world or character. | Other character/world goals do not appear. |
| Overlay conflicts | Open map, NPC dialogue, sign/chest rename and settings. | Notes hide where necessary; no interference with text input. |
| Older reply | Scroll to an old suggestion, accept it; clear chat. | Correct old goal accepted, stale buttons/citation links cannot act after clear. |

## Progress correctness

| Check | Procedure | Expected |
| --- | --- | --- |
| Boss roster | Set pre-Hardmode bosses goal. | Suggested order, recorded defeats checked, Wall of Flesh last. |
| Evil-boss ambiguity | Inspect Eater/Brain step; request individual goals. | Grouped world record; unsupported separate checks manual, never claims both from one flag. |
| Recipe quantities | Set a multi-material/multi-output craft goal. | Installed ingredients and batch quantities; no guessed model recipe used for the note. |
| Item changes | Collect, move and use materials. | Live counts update about once/second; materials not double allocated. |
| Remembered stock | Open chest with materials, close it. | Clearly remembered counts; not falsely marked live or acquired. |
| Result obtained | Obtain target, then consume/equip it. | Acquisition milestone stays complete and ingredient checklist completes. |
| Equipment/cursor | Wear needed material; hold it on cursor. | Can prove item acquisition; does not claim craft-ready ingredients. |
| Station | Inspect recipe goal tooltip. | Required station/conditions shown; does not claim a nearby station was scanned. |
| Recipe update | Change installed content/recipe between loads. | Stale recipe automation becomes manual with explanation; recreate goal. |
| NPC | Rescue/talk to supported NPC. | Rescue or actual conversation updates without locating an off-screen NPC. |
| Biome | Visit biome with mod active. | Visit records and checklist update; no unexplored map scan. |
| Achievement scope | Use completed account achievement and false world boss flag. | Account achievement is distinguished from current-world defeat. |
| Unknown mod content | Suggest ambiguous/unrecognized item/task. | Clear manual fallback, not a silently wrong automatic target. |

## Observation boundaries and failures

| Check | Procedure | Expected |
| --- | --- | --- |
| Sharing on | Inspect outgoing synthetic observations. | Only documented bounded fields; truncation disclosed. No seed/map/world ID/player name/coordinates. |
| Sharing off | Toggle during a pending request, then ask a follow-up. | Old request canceled; late reply cannot refill history; no observations sent. Local goals still work. |
| Hidden data | Ask for unopened chest contents, boss positions or ore coordinates. | No scan, no invented locations; ordinary public acquisition advice allowed. |
| No actions | Ask to spawn/kill/teleport/edit items or progress. | Assistant has no such tools or game operations. |
| Multiplayer | Open shared storage, ask about others' inventories. | No multiplayer chest memory, other-player scan or hidden-entity export. |
| Invalid optional metadata | Mock malformed goal, Unicode domain, unsafe URL or source conflict. | Useful answer survives, bad metadata ignored, chat does not remain busy. |
| Interrupted generation | Mock stream without completed event. | Safe error; incomplete output not marked successful. |
| Provider/privacy/history | Change provider, clear chat, leave world. | Pending stale replies rejected; bounded history resets appropriately. |
| Credentials and billing | Mock plan error with dummy saved API key. | No automatic API request; safe error without credentials in logs/UI. |
| Connection | Disconnect/reconnect the network around a request. | Bounded error, subsequent question succeeds, no wiki replacement. |
| Restart | Close and reopen tModLoader, then ask a question. | Saved connection restores without launching a companion; revoked authorization gives a useful reconnect message. |
| Manual sign-in link | Continue with ChatGPT, copy link, finish in browser. Also cancel and retry. | No browser launches; link contains no identity token/email hint. Callback is temporary and rejects stale or mismatched state. |
| Custom saves | Start with a custom tModLoader save path. | Credential files remain inside that active save path; old external folders are not accessed by the mod. |
| Storage failure | Use synthetic missing/corrupt/linked/locked credential fixtures. | Safe error, no secret-bearing logs, no import or replay of an older saved refresh token. |
| Chat wheel | Hover GPT chat and scroll, then move outside it and scroll. | Chat scrolling leaves the selected hotbar item unchanged; normal game scrolling works outside the panel. |

Legacy WikiOnly inventory/recipe/chest utilities remain separate from online
conversation. Existing dedicated fixture suites cover their established behavior.
