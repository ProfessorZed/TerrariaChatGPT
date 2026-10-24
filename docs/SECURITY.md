# Security notes

Version 0.9.9 keeps account credentials encrypted with Windows CurrentUser DPAPI.
It does not save plaintext access tokens, refresh tokens or API keys in normal
mod settings. Credentials live under `Main.SavePath/TerrariaChatGPT`, with
restricted Windows permissions, atomic writes and checks against linked paths.
The mod never imports old companion files or searches external credential stores.

The separate upgrade installer copies encrypted native files without decrypting
them. It refuses to overwrite an existing login or recover a missing initialized
store from older credentials, since that could replay a rotated refresh token.
Verify the upgraded connection before removing old credential files. There is no
promise of forensic erasure from backups, cloud synchronization or storage media.

Sign-in uses a temporary loopback listener, random state and nonce, PKCE, and
verified OpenAI identity signatures/issuer/audience. It has a bounded lifetime
and can be canceled. Copying a sign-in link is explicit; the link contains no
saved token or email hint. Do not share a pending sign-in link with others.
Source and usage links are copied instead of launching external programs.

OAuth and inference use fixed/validated HTTPS destinations and disable automatic
redirects. Credentials are not included in model prompts, user-facing errors or
diagnostic output. Live web content can supply information, but cannot execute
commands or game actions. Optional API billing requires explicit selection.

Release packaging uses a strict source allowlist and rejects linked paths and
unexpected state/credential files. Private profiles, game saves, logs, retired
companion files and build output are not source-release inputs.

Named-chest sorting is a local user-confirmed action, not an AI tool. A preview
does not transfer items; only the explicit confirmation control can commit the
reviewed plan. Before committing, it checks the player, world, eligible inventory
slots, favoriting, exact destination chest instances, contents, range and capacity.
Plans are single-use. Chat replies and chest labels cannot invoke code or approve
a move. Custom item behavior is not invoked to merge or split stacks; whole-stack
transfers preserve the original item. No sorting request sends an unreviewed
multiplayer inventory update.

GPT sorting proposals are bounded, untrusted optional metadata: at most 40 item
assignments, each matched to a main-inventory item key and known chest label from
the request's permitted observation snapshot. Unknown targets and conflicting
rows are rejected rather than guessed. The accepted proposal is detached and
read-only, scoped to the requesting character, world, session and conversation.
The UI routes both **Review sort** and the header control to that accepted object.
Refreshing retains the exact destinations; a proposal cannot silently fall back
to generic sorting. Unassigned items remain untouched. Expired proposals cannot
be reopened, and a successful transfer consumes the active proposal.

**Remember choices** is a separate explicit user action. It validates the entire
batch against currently eligible items and known labels, resolves exact public
item identities, and applies all changes together within the existing 32-entry
memory limit. Failure leaves memories unchanged; success offers a single Undo
receipt and rebuilds the transfer preview because its memory revision changed.
This save button cannot transfer items or bypass **Confirm sort**. It is offered
before transfer, while the affected items are still available for validation.

Known-chest identities persist through normal world/character saves. Actual
placement/removal events register or invalidate only the supplied coordinates.
Legacy observations are adopted conservatively at an explicit nearby preview;
they cannot retrospectively prove identity before this version began recording
it. Locked, unknown, replaced or out-of-range storage stays ineligible. Names of
empty known chests may be shared as untrusted labels, never as instructions.

## Limits and reporting

Model memory suggestions are untrusted optional data. A local bounded validator
requires source evidence from the current user message, rejects obvious secrets,
temporary state and instruction-like records, resolves exact public item
identities, and accepts only supported preference topics and sorting targets.
External pages and assistant replies cannot authorize a memory. Memories are
preferences, not authoritative world or game facts. They cannot execute code or
transfer items. Character ownership, memory revisions and conversation epochs
reject stale responses, Undo receipts and sorting previews. Malformed memory
metadata cannot discard an otherwise usable answer or prevent character loading.

The explicit **Remember choices** path uses the user's reviewed button click as
authorization for that validated batch. Automatic conversational learning still
requires user-message evidence; model prose alone cannot silently persist a
group of proposed sorting destinations.

Semantic recognition can still misunderstand a preference; records are visible
and removable. Memory filtering is not a credential vault or a complete sensitive
information detector. Player/mod saves can be copied or shared by their owner;
memory data follows them and is not protected by the separate login encryption.

No application can guarantee that a login cannot be stolen. Malware, an
administrator, or another untrusted mod running as the same Windows user can
access process memory or use that user's decryption privileges. Clipboard and
browser history are also outside this mod's control. Only install trusted mods.
Encryption protects stored files; it is not isolation from other code in
tModLoader. Keep Windows and tModLoader updated.

Use Sign out to clear the local ChatGPT connection and request remote revocation.
If remote disconnection cannot be confirmed, use ChatGPT's connected-app settings
to disconnect the app. Clear key removes the locally saved API key. Removing the
mod file alone does not erase credentials or goal/player data.

Report a suspected vulnerability to the repository owner with a minimal
description. Never include tokens, API keys, credential files, pending sign-in
links, or private player/world data in a public issue.

Workshop publication and review status can change independently of source updates.
These notes do not claim moderator approval or that use of Windows encryption
avoids the native library rules.
