# Security notes

Version 0.9.2 keeps account credentials encrypted with Windows CurrentUser DPAPI.
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

## Limits and reporting

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

The Workshop item remains private pending moderator review. The project does
not claim Workshop approval or that use of Windows encryption avoids the native
library rules.
