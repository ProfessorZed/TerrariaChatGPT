# Terraria ChatGPT

![Terraria ChatGPT icon](icon.png)

Chat with ChatGPT while playing Terraria. Ask questions, look things up online,
and pin goals that follow your progress—all inside tModLoader.

**Version 0.9.4 · Windows · Client-side tModLoader mod**

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3822943933)
· [GitHub releases](https://github.com/ProfessorZed/TerrariaChatGPT/releases)

This is an unofficial community project by
[ProfessorZed](https://github.com/ProfessorZed), not a product of Re-Logic or
OpenAI. It is free to download, and its source is public. The project uses
[mixed licenses](#license), including a noncommercial license for its adapted
ChatGPT connection code.

## What it does

- Adds separate **Terraria / GPT** chat tabs, with independent drafts.
- Lets you switch model and reasoning effort directly in GPT chat. Choices are
  saved immediately and apply to the next question without clearing the conversation.
- Shows a silent, fading reply preview while GPT chat is closed, with an unread
  `*` until you open GPT. Notifications never take input focus or intercept clicks.
- Gives conversational answers, remembers recent follow-ups, and supports live
  web search with small source markers that copy the source link when clicked.
- Highlights items in **cyan**, locations in **gold**, observed supplies in
  **green**, and missing quantities in **orange**.
- Offers **Set goal?** buttons for up to six draggable, collapsible goal notes.
  Notes stay visible with your inventory open and update locally.
- Can use your carried items, equipment, opened-chest memories, recorded boss
  defeats, achievements, and observed biome/NPC progress.
- Includes text and panel size controls, timestamps, question history, and a
  copy-answer button. Scrolling the chat does not also scroll the hotbar.

There is no separate companion to install or start. Online answers require an
internet connection and an eligible ChatGPT connection, or an explicitly selected
OpenAI API account. Goal notes continue tracking locally while offline.

## Install and connect

1. Subscribe on the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3822943933), or download
   `TerrariaChatGPT.tmod` from this repository's
   [releases](https://github.com/ProfessorZed/TerrariaChatGPT/releases).
   With tModLoader closed, place it in
   `Documents\My Games\Terraria\tModLoader\Mods`.
2. Open tModLoader and enable **Terraria ChatGPT** under **Workshop → Manage Mods**.
3. Open **Settings → Mod Configuration → GPT**. Choose **ChatGPT plan** as the
   answer provider and select **Continue with ChatGPT**. Select **Copy sign-in
   link**, paste it into your browser's address bar, and finish sign-in. Return
   to the game and acknowledge **Got it**. The mod does not launch a browser.
4. Enter a world, press **Enter**, and select the **GPT** tab. **Tab** switches
   between Terraria and GPT chat.

The mod restores your saved connection when tModLoader starts. Reconnect if your
account authorization expires or is revoked. **Copy usage link** copies the link
to ChatGPT's usage settings. Account eligibility, available models, search support, and usage
limits depend on the connected account and service.

To choose a model or effort, use the **Model** and **Effort** buttons at the top
of GPT chat. Opening a picker loads your account's available choices; **Refresh**
updates the list. Changes save immediately. The same choices are available under
**Mod Configuration → GPT**, where you use **Save Config** after selecting them.
**Auto** model uses the first available model; **Auto** effort lets the service
choose its default. An unavailable model produces a message asking you to choose
another; the mod does not silently use a different one. When switching models in
chat, an unsupported effort resets to Auto with a visible notice.

Effort controls how much reasoning the model uses: **Fast**, **Balanced**,
**Thorough**, **Extra thorough**, and **Maximum**, where supported. **Instant**
is offered only if the account's model supports no-reasoning requests. These names
describe reasoning preferences, not guaranteed response times or priority service.
Higher effort can take longer and use more plan allowance. Model and effort
selectors apply to ChatGPT plan mode only.

The **OpenAI API** option is separately billed and requires an API key. Enter its
key in the masked mod setting and explicitly choose that provider to use it.
The mod never switches from your ChatGPT plan to API billing automatically.
**Wiki only** is an optional reference mode that needs no AI account.

When upgrading an earlier native build, use **Install Mod.cmd** from the matching
source release after building. It copies the existing encrypted login into
`TerrariaChatGPT` inside your active tModLoader save directory, without decrypting
it or overwriting a newer login. Pass `-SaveDirectory` for custom saves. A bare
`.tmod` replacement cannot import files outside tModLoader's save directory.

Try asking:

- “What can I use to fly in pre-Hardmode?”
- “Search the official wiki to check how I get Rocket Boots.”
- “What can I make using Blade of Grass?”
- “I want to beat all pre-Hardmode bosses.”

Click **Set goal?** below a suggested goal to pin it. Drag its header to move it,
collapse it to save space, or click **X** to remove it. Automatic steps use
supported game records and installed recipes; unsupported tasks become manual
checkboxes. Closed-chest counts are remembered snapshots and may be out of date.

## Fair play and privacy

The mod has no tools to spawn, kill, teleport, edit items, or change progression.
It does not scan unexplored map areas, unopened storage, or off-screen entity
locations. General advice about where items normally appear is still available.
Multiplayer chest memory is disabled.

**Share observed progress with GPT** is enabled by default. Turn it off in mod
settings to keep those observations local. Your typed questions and recent
conversation are sent to the chosen online provider; search may send related
queries to that provider's search service. Goals and encrypted credentials stay
on your computer. See [the privacy details](docs/PRIVACY.md).

Answers can be wrong. Live search and local recipe checks help, but do not make
every generated answer authoritative. The mod does not import conversations from
the ChatGPT website.

## Build from source

If upgrading from this project's local **Terraria GPT 0.8.x** builds, use
**Install Mod.cmd** after building. It preserves settings, disables that verified
old local build, and enables the renamed mod. Existing goals and chest memories
are imported when your character loads. Keep the old build disabled before
entering a world; do not run both versions together.

Install tModLoader through Steam, then clone or download this repository.
**Build before installing:** GitHub's source archive does not include a compiled
mod.

On Windows, run **Build Mod.cmd**, then close tModLoader and run
**Install Mod.cmd**. The build uses tModLoader's included compiler and creates
`dist/TerrariaChatGPT.tmod`; Python, Node.js, and a separate .NET SDK are not
required. Every build also checks the packaged assembly using the installed
tModLoader loader's namespace and mod-type validation. For a different Steam
library or save location:

```powershell
.\tools\Build.ps1 -TmlPath 'D:\SteamLibrary\steamapps\common\tModLoader'
.\tools\InstallNativeUpdate.ps1 -SaveDirectory 'D:\TerrariaSaves'
```

The current native credential storage requires Windows. Linux and macOS online
sign-in are not supported by this release. Development has used Terraria
1.4.4.9 with tModLoader 2026.8.3.0.

### Checks

The standalone C# checks use **PowerShell 7** (`pwsh`) and mocked services.
Run each script in a fresh process, for example:

```powershell
pwsh -NoProfile -File .\tools\TestNativeAuth.ps1
pwsh -NoProfile -File .\tools\TestCredentialStorage.ps1
pwsh -NoProfile -File .\tools\TestCredentialMigration.ps1
pwsh -NoProfile -File .\tools\TestReleaseSafety.ps1
pwsh -NoProfile -File .\tools\TestNativeResponses.ps1
pwsh -NoProfile -File .\tools\TestNativeReply.ps1
pwsh -NoProfile -File .\tools\TestAssistantService.ps1
pwsh -NoProfile -File .\tools\TestChatLayout.ps1
pwsh -NoProfile -File .\tools\TestGoalProgress.ps1
pwsh -NoProfile -File .\tools\TestChatFeatures.ps1
pwsh -NoProfile -File .\tools\TestModelSelector.ps1
pwsh -NoProfile -File .\tools\TestEffortSelector.ps1
pwsh -NoProfile -File .\tools\TestChatToolbar.ps1
pwsh -NoProfile -File .\tools\TestChatToolbarInput.ps1
pwsh -NoProfile -File .\tools\TestChatPreferences.ps1
pwsh -NoProfile -File .\tools\TestReplyNotifications.ps1
```

Authentication checks use synthetic credentials and a local callback listener.
`TestNativeLive.ps1` is a separate, opt-in check that requires `-RunLive` and uses
the saved ChatGPT connection and its allowance. Do not run it as part of an
unattended source build. [Manual acceptance checks](docs/ACCEPTANCE.md) describe
in-game verification; they are not a claim that every scenario has been tested.

See the [changelog](CHANGELOG.md), [security notes](docs/SECURITY.md), [publishing guide](docs/PUBLISHING.md), or
[report an issue](https://github.com/ProfessorZed/TerrariaChatGPT/issues). Remove
credentials, account information, and private chat text from any attached logs.

## License

Independently authored project code is available under the [MIT license](LICENSE).
The adapted native authentication and response transport files retain the
[Sign-in with ChatGPT DevKit Noncommercial License v1.0](Common/Native/SignInWithChatGPT-LICENSE.txt),
as listed in [their attribution notice](Common/Native/NOTICE.txt). Their third-party
notices are also included. These restrictions mean the combined release is
**source-available**, rather than wholly covered by an unrestricted open-source
license. Service access and third-party names are not granted by the code license.
