# Minecraft Skylines: agent instructions

Play Cities: Skylines (CS1) as a Minecraft player; Minecraft Java runs hidden. Start with
`docs/ARCHITECTURE.md` (layers, state ownership), then `docs/MILESTONES.md` (current milestone,
test evidence), `docs/DECISIONS.md` and `docs/SETUP.md`.

## Hard rules (from the project owner)

- **Never execute `rm`, and never write a script, build step or test that deletes files**
  (no shell removal commands, no `shutil.rmtree`/`os.remove`, no `File.Delete`/`Directory.Delete`,
  no `Files.delete`, no `git clean`). Unwanted files are moved into
  `_quarantine/<YYYY-MM-DD>/` inside the project; the owner does the final deletion.
- **Never run a build tool's clean task** (`gradle clean`, `dotnet clean`, `msbuild /t:Clean`).
  `cs1/Directory.Build.targets` disables MSBuild's `IncrementalClean`. Gradle and MSBuild still
  overwrite their own outputs under `build/`, `bin/`, `obj/`; that is accepted.
- Never write into a game installation, the owner's real CS1 saves, or their real Minecraft
  worlds/instances. Tests use dedicated test saves and a separate instance.
- **No proprietary game files and no credentials in Git.** CS1 assemblies are referenced from
  `~/.cache/minecraft-skylines/refs/cs1/Managed/` (see `docs/SETUP.md`), never copied into the repo.
- No spending, publishing or releases without the owner's explicit yes.

## Layering (the owner's requirement)

Lower layers stay free of upper-layer knowledge so a future "CS1 + another game" project can
lift them unchanged: `Skylines.Bridge` and `Skylines.Core` (no Unity), `Skylines.Host` (CS1, no
Minecraft), `MinecraftSkylines.Mod` (Minecraft-specific). The Java `minecraft/bridge` module has
no Minecraft imports. Nothing references upward.

## Verification

- `protocol/bridge-v1.md` is the contract. Every implementation passes
  `python3 protocol/reference/conformance.py {host|guest} --junit <file> -- <iut command>` and the
  golden vectors in `protocol/vectors/`. Change vectors only by changing the spec and
  `protocol/reference/gen_vectors.py` together.
- `tools/check.sh` runs everything that can run without the games.
- Mocked or sandbox tests are never evidence that game-engine integration works. Report each
  milestone as implemented / built / sandbox-tested / verified in game / blocked.
- Every CS1 API the code relies on is verified against the real assemblies and recorded in
  `docs/CS1-API-NOTES.md` (assembly, type, signature) first.

## Toolchain

- .NET SDK: `~/.cache/dotnet/sdk10/dotnet` (10.0.401). CS1-loaded projects target `net35`; pure
  libraries also target `net10.0` for tests.
- Java 25 (system), `GRADLE_USER_HOME=~/.cache/gradle-home`, Minecraft 26.3 + Fabric.
- Python 3 standard library only in `protocol/reference`.
