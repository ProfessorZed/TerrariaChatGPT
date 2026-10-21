# Publishing Terraria ChatGPT

The source repository is
[ProfessorZed/TerrariaChatGPT](https://github.com/ProfessorZed/TerrariaChatGPT).
The display name is **Terraria ChatGPT** and the internal mod name is
`TerrariaChatGPT`. Keep that internal name stable after publication so updates
continue to refer to the same mod.

## Prepare a release

1. Update `build.txt` and `CHANGELOG.md` together. Keep the current version visible
   in the README.
2. Run **Build Mod.cmd**. For another Steam library, pass `-TmlPath` to
   `tools/Build.ps1`. The script validates the artwork, builds with tModLoader,
   and checks the resulting package name and version.
3. Run the relevant offline checks listed in the README and review
   [the manual acceptance checklist](ACCEPTANCE.md). A successful compilation
   does not prove every in-game interaction works.
4. Save and close tModLoader before running **Install Mod.cmd**. Enable
   **Terraria ChatGPT** and verify the icon, chat, account connection, and goal
   notes in game.
5. Use `dist/TerrariaChatGPT.tmod` as the downloadable release asset. Publish the
   matching source revision and release notes on GitHub. Do not upload the whole
   local working directory as a binary release.

The source archive requires a build. The `.tmod` release asset can be installed
directly. Python, Node.js, and the archived companion are not required by either
the build or the installed mod.

## Steam Workshop

The existing item is [3822943933](https://steamcommunity.com/sharedfiles/filedetails/?id=3822943933).
Update this item; do not create a duplicate. The owner has made the item
**Public** and authorized public updates. Preserve that visibility unless the
owner requests a change. The 0.9.2 storage and copy-link changes address the
external-file and external-program concerns. Windows DPAPI/native security
operations and the temporary loopback sign-in listener remain disclosed for
review; public visibility does not establish moderator approval. Do not describe
the build as approved without confirmation from the moderators.

After building, run `tools/PrepareWorkshop.ps1`. This adds the correctly named
source entry to tModLoader's Develop Mods menu without moving your project.
It leaves an existing unrelated source folder untouched.

Use tModLoader's **Workshop → Develop Mods** publishing flow with the built
**Terraria ChatGPT** entry. Review the preview, title, description, and visibility
before the final Publish action. `description.txt` supplies the Workshop copy.
The publishing source directory must still exist and contain both image files:

| File | Purpose | Size |
| --- | --- | --- |
| `icon.png` | Mod browser and in-game icon | 80 × 80 PNG |
| `icon_workshop.png` | Workshop preview artwork | 512 × 512 PNG |

`icon.png` is included in the `.tmod`. The larger Workshop image stays in the
publishing source directory. Do not delete the build's staged source before
finishing publication; the publishing UI may use the path recorded at build time.
If a preview reports a missing image, rebuild from the current source directory
before reopening Publish.

After the first successful upload, preserve any Workshop ownership/publication
metadata created by tModLoader. Verify the actual published item URL before
adding a Workshop badge or install link to the README. A successful local build
or a public GitHub repository is not itself a Workshop publication.

## Public source contents

Include the mod's source, localization, project/build metadata, icons, current
documentation, relevant test sources, and build/test scripts. Preserve the root
`LICENSE` and all three native attribution/license files in `Common/Native`.

Exclude generated `.build`, `dist`, `bin`, `obj`, game saves, exported Terraria
font/texture assets, local test reports, logs, credentials, `.env` files, and
editor/account state. The old `bridge` companion is not needed for this release;
do not include its dependencies or legacy setup utilities in the release archive.

The independently authored code is MIT-licensed. The adapted native connection
files retain their included noncommercial license. Describe the combined release
as free and source-available, and preserve that distinction when redistributing
it. This project is an unofficial community mod, not an official Re-Logic or
OpenAI product.
