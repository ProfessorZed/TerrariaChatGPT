# Terraria ChatGPT 0.9.9 acceptance checks

These are manual expectations, not a claim of completed gameplay testing.
Publication does not imply that every manual check below has been performed.
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

## NPC happiness and version context

| Check | Procedure | Expected |
| --- | --- | --- |
| First observation | Speak to a town NPC, close dialogue and ask its price modifier. | Actual observed multiplier, discount/markup arithmetic and remembered status; no guessed reading. |
| Unknown NPC | Ask about an NPC not spoken to since updating. | No invented price. General preferences remain answerable. |
| Refresh housing | Change housing normally, speak to the NPC again, then ask. | New game-computed reading replaces the old one; the mod makes no housing changes. |
| Shop switch | Speak to two NPCs with different modifiers in succession. | Each reading belongs to the correct NPC; a closed shop is not recorded as neutral. |
| Restart/world switch | Save, re-enter, and change worlds/characters. | Remembered reading persists only for its character/world and never returns as currently observed. |
| Advice | Ask how to improve an unhappy NPC. | Explains observed causes; distinguishes general recommendations from unobserved housing details and predicted prices. |
| Version | Ask the current Terraria version and a version-sensitive question. | Running Terraria and tModLoader versions are distinct; mechanics and searches default to the Terraria version. |
| Sharing off | Disable observation sharing and ask about version and NPCs. | Version remains available; no NPC readings are sent. |

## Contextual memory

| Check | Procedure | Expected |
| --- | --- | --- |
| Natural preference | Say “I prefer concise bullet-point answers” without a remember command. | Useful answer and a visible Memory saved notice if validated; manager shows the preference. |
| Context correction | Discuss Gel sorting, then say “Actually, classify it as Block for future sorting instead.” | Resolves the user-mentioned item, replaces its rule, and shows the saved change. |
| Edit saved memory | After restarting with a single Goodie Bag rule, say “Actually, change that sorting preference to Consumables instead.” | Replaces the same entry with a visible update and Undo. Ambiguous references ask which rule. |
| Add preference detail | Save concise answers, then say “Update my answer style preference to also use bullet points.” | Revised entry preserves concise answers and adds bullets. |
| Combined label | Say “Sort my Goodie Bag into Vanity” with a known Vanity & Paints chest. | Saves Category: Vanity, and preview targets the combined label if nearby and eligible. |
| Item family | Say “Sort my grappling hooks into Travel.” | Applies to all supported grappling hooks, not raw Hook material. Exact item exceptions win. |
| Temporary state | Mention current stock, a boss defeat or a one-time crafting goal. | No permanent inventory/progress fact; normal answer/goal functionality remains. |
| Questions/quotes | Ask “How many Gel do I have?” after a sorting discussion; quote another person's preference. | Does not resurrect a historical rule or learn a quoted preference. |
| Restart/scope | Save & Quit, restart, use the same character in another world, then switch characters. | Original character retains memory; another character has its own entries. |
| Undo/remove | Undo the latest save or remove a manager entry while an answer is pending. | Correct record is reverted/removed; old request and conversational copies cannot re-add it. |
| Pause | Pause Memory, ask a question, then preview sorting. | No memory payload or learning; normal category matching returns; saved entries remain reviewable. |
| Clear | Click Clear all once, cancel; then click twice deliberately. | First attempt leaves data; confirmed second action removes all entries. |
| Sort override | Save an exact-item category or custom chest-name rule and preview. | Preferred destination appears only if eligible; Confirm sort still required and protections hold. |
| Missing destination | Remove/rename/move beyond the preferred chest after saving a rule. | Item stays with a recommendation; no fallback into an unrelated chest. |
| Bad data | Test unknown/ambiguous item IDs, malformed metadata/save rows and obvious secrets. | Safe rejection; ordinary reply and character loading survive. |
| Clear chat | Use `/clear`, then ask about a saved preference. | Chat context cleared; saved memory remains until explicitly removed or paused. |

## Named-chest sorting

| Check | Procedure | Expected |
| --- | --- | --- |
| Preview first | In single-player, open and close named storage, then click Sort chests in GPT chat. | Shows item quantities and destination names; no items move. |
| Original mismatch | With known Tools & Travel and Consumables chests, ask “Categorise them according to my chests” for Amethyst Hook, Magic Mirror, Money Trough, Shadow Key, Blue Jellyfish Kite and Defender Medal. | Validated choices produce Review sort; preview uses the chosen exact chest names, not an unrelated default category match. No transfer occurs from the reply. |
| Header parity | Open Review sort, cancel, then use the header Sort chests button. | Both show the latest accepted choices. Refresh keeps the same mapping. |
| Contextual correction | Follow a sorting answer with “Actually, put those in Tools & Travel” or “Use Consumables instead.” | Uses recent context to propose a revised reviewable plan; rejected/ambiguous metadata cannot move items. |
| Partial proposal | Return valid assignments for only some eligible item keys. | Unassigned items stay in inventory; no generic fallback fills omitted assignments. |
| Whole-batch memory | Click Remember choices before Confirm, with more than three valid assignments. | All validated choices save as exact item/chest preferences within the 32-entry total; visible receipt and one Undo. No items move. |
| Memory limit/failure | Try a batch exceeding remaining memory capacity, containing a stale eligible key, or naming unknown storage. | No partial memory writes and no transfers; clear status explains the failure. |
| Save then confirm | Remember choices, then deliberately click Confirm sort. | The same destinations have a refreshed preview; only the separate confirmation transfers items. |
| Remembered restart | Remember choices, Save & Quit, restart and load the same character; use the header sorter without re-asking. | Saved preferences remain; known destinations need no reopening. A different character does not inherit the rules. |
| Temporary restart | Review choices without remembering, then restart. | Temporary plan is gone; no silent permanent preferences. Normal local sorting remains available. |
| Plan expiration | Supersede a plan, clear chat, change world/character, then click its old reply button. | Old choices cannot reopen. Successful confirmation consumes a plan while its result stays visible. |
| Short viewport | Review at 640×360 and 480×270; scroll to blocked destinations and use separate buttons. | Remember/Refresh/Cancel/Confirm do not overlap; item/chest names remain readable, wheel cannot skip unread rows, Confirm is disabled when nothing can move. |
| Explicit action | Type “sort my inventory” or “Confirm sort”; press Enter with a preview open. | Chat cannot authorize a transfer; only clicking the visible Confirm sort button can apply the preview. |
| Correct categories | Preview Glass, bars, weapons, paint and consumables with suitable named chests nearby. | Uses an appropriate recognized category or an exact item-name chest; existing chest items stay in their slots. |
| No destination | Preview an item without a suitable chest, including with a Misc chest nearby. | Leaves it in inventory, flags it under Stay in inventory and suggests a chest name. No Misc fallback. |
| Full storage | Fill the matching chest; include another unrelated empty chest. | Explains the capacity issue and leaves the remainder in inventory. |
| Protected slots | Include hotbar items, favorites, equipment, dedicated ammo/coins, personal-bank and cursor items. | Protected items stay untouched; holding a cursor item or having a chest/bank open blocks preview. |
| Modded item state | Use synthetic custom/global item fixtures. | Whole original stack moves only into an empty slot; no merging, splitting or item-data cloning hooks. |
| Stale preview | After preview, change a bag stack, favorite, chest name/content, chest tile, world or character; move out of range. | Confirmation cancels with no transfer and asks for a fresh preview. |
| Range | Test chest centers just inside, at, and outside 600 pixels from player center. | Only strictly inside vanilla Quick Stack range is eligible. |
| Known storage | Include unopened, locked, in-use, removed/replaced and ignored chests. Restart with previously opened or player-placed destinations. | Unknown/invalid destinations excluded; known eligible chests need no reopening after restart. |
| Repeat/cancel | Double-click, hold the mouse, resize before clicking, cancel with Escape, close chat or leave the world. | No replay, stale hitbox or click-through; cancel never moves items. |
| Leftovers | Confirm a mixed preview with movable and unmatched items. | Success keeps the leftover list and chest recommendations visible. |
| Offline/sharing off | Disconnect the AI connection or disable observation sharing. | Local sorting and enabled saved preferences still work; no new GPT destination plan without shared eligible-item/chest context. Local transfer previews and storage identifiers are not sent online. |
| Multiplayer/custom storage | Attempt in multiplayer or with a Magic Storage network. | No transfer; first release supports ordinary named single-player chests only. |

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
