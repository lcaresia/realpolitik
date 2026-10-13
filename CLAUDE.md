# Project rules (Realpolitik / CurrencyMod)

## STRICT: all code in English

Everything that is code is written in **English, without exception**. This is a hard rule set by the owner (Lucas,
2026-10-11). Talking with Lucas in Portuguese is fine; the code is not.

Must be English:
- identifiers: namespaces, classes, methods, properties, fields, variables, parameters, enum values, file and folder
  names of code;
- comments and XML doc comments (`///`);
- log messages (`Plugin.Log`, `Runtime.Event`, `IaLog`) and internal error/exception strings;
- dev-channel command names and their help/usage text (`devcmd`, `ai ...`, `game ...`);
- JSON field names of saved state;
- LLM action names and their JSON fields (e.g. `choose_civic` with `civic`/`option`), dev/tool calls, config keys —
  the names the AI calls are code, even though the prose around them in the prompt is Portuguese;
- scripts in `tools/` and their comments;
- commit messages.

Not code (stays as is):
- player-facing text goes through `L.T` / `L.F` / `L.N` and the `Lang/*.json` translations;
- docs in `docs/` written for Lucas, and LLM prompt prose meant to be read by the AI nations.

When touching existing Portuguese code, write the new lines in English; do not add new Portuguese code anywhere.
Legacy Portuguese code (e.g. the old action names `propor_votacao`, `renomear`, fields `nacao`, `motivo`) must be
refactored to English (Lucas, 2026-10-12); until that is done, never copy its naming into new code.
Review before every commit: `git diff` must not add Portuguese identifiers, comments or log lines.

## Code map first (graphify)

Before changing code, locate it through the code maps, not by guessing. Maps are built by graphify (tree-sitter AST,
local, no LLM) into `codemap/` (git-ignored; the game map derives from decompiled SEGA/Amplitude code):
- `codemap/mod/graphify-out/` — the mod (`src/`); `codemap/game/graphify-out/` — the decompiled game;
  `codemap/full/` — both merged plus a `patches` edge per Harmony patch (`HARMONY_PATCHES.md`), built by
  `python3 tools/codemap/harmony_edges.py src/CurrencyMod codemap/mod/graphify-out/graph.json codemap/game/graphify-out/graph.json codemap/full`.
- Run through WSL (`graphify` is a uv tool there), e.g.
  `wsl -d Ubuntu-24.04 -- bash -lc 'cd "/mnt/c/Program Files (x86)/Steam/steamapps/common/Humankind/_Modding" && graphify explain "<path>::.Method()" --graph codemap/mod/graphify-out/graph.json'`
- Useful: `explain` (callers/callees with file:line), `affected` (reverse impact), `path A B`, `query`, `god-nodes`.
- After code changes, refresh (AST only, no LLM). `graphify update` has no `--out`: it writes `graphify-out/` inside
  the scanned folder, so move it into `codemap/mod` and never leave it in `src/`:
  `graphify update src && rm -rf codemap/mod/graphify-out && mv src/graphify-out codemap/mod/graphify-out`,
  then rebuild `codemap/full` with the `harmony_edges.py` command above.

## Reverse engineering: REA MCP

Use the REA MCP server (`rea`, package `rea-agents`, registered in `.mcp.json`) for reverse-engineering questions the
decompiled source and the code maps do not answer: inspecting the game's .NET assemblies directly
(`Humankind_Data\Managed\*.dll`, e.g. `Amplitude.Mercury.*`), checking a method's real IL/signature after a game
update, native binaries (`UnityPlayer.dll`, other native DLLs) and runtime behaviour.
- Order: `codemap` (graphify) first to locate code, `decompiled\` to read it, REA when those are missing, stale or the
  target is native.
- REA runs inside WSL (Ubuntu-24.04, Node 22 via nvm), so give it WSL paths:
  `/mnt/c/Program Files (x86)/Steam/steamapps/common/Humankind/Humankind_Data/Managed/<Assembly>.dll`.
- .NET and JavaScript analysis need no native engine; native binaries need Ghidra, IDA or Hopper (not installed).
- Pin the version in `.mcp.json` (`rea-agents@<version>`); update it on purpose, never `@latest`.

## Other standing rules
- Lucas's real save (`Save Files\Cloud\NAO DELETE PELO AMOR DE DEUS.ctr`) is never touched; test on copies.
- Never build with deploy or hot-reload while Lucas is playing (`-p:SkipDeploy=true`).
- Tooling runs in WSL Ubuntu-24.04; git through WSL, never Windows Git.
- Never commit secrets (`.env`, API keys); the repository is public.
