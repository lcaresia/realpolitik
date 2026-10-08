# Realpolitik: Living Nations for HUMANKIND

A code mod for **HUMANKIND** (Amplitude Studios / SEGA) built on BepInEx 5 + Harmony. Single-player only.

- **AI Diplomacy:** every computer nation thinks once per turn with a language model of your choice. It reads a
  dossier with only what it knows, keeps a secret diary and memory, writes letters to you and to other nations, and
  its decisions become real game orders (war, peace, treaties, surrender terms, gifts, army orders, trade policy,
  World Congress votes). Providers: OpenRouter, DeepSeek, OpenAI, Gemini, GLM, xAI, ChatGPT via Codex, or a local
  model (Ollama / LM Studio). You bring your own API key.
- **Diplomatic mail, council of ministers, espionage** that intercepts private letters.
- **Economy:** a currency per empire, exchange rates, inflation and interest, a native-looking Central Bank window.
- **Trade blockades:** free / toll / blocked per trade post, with an instant detour preview.
- **MoreEmpires:** up to 16 empires on any map size (separate plugin).
- UI in Portuguese, English, Spanish, French and German.

Website: https://realpolitik-living-nations.pages.dev

> Not affiliated with or endorsed by Amplitude Studios or SEGA. HUMANKIND is a trademark of its owners. This
> repository contains **no game code or assets**: you need your own copy of the game to build or run the mod.

## Repository layout

| Folder | Contents |
|---|---|
| `src/CurrencyMod/` | The mod core (hot-reloadable): economy, trade, native UI, AI Diplomacy (`Diplomacia/`), translations (`Lang/`). |
| `src/CurrencyMod.Loader/` | Fixed BepInEx plugin that loads and hot-reloads the core. |
| `src/MoreEmpires/` | Separate plugin: 2–16 empires on every map size. |
| `installer/` | Inno Setup script, texts, art, BepInEx redistributable + licenses. |
| `tools/` | Release pipeline (`gerar-release.ps1`), dev scripts (`tools/dev/`), AI benchmark (`ia-bench/`). |
| `docs/` | Technical docs (mostly in Portuguese). Start with `docs/mapa-do-projeto.md`. |
| `research/` | Notes on the game's internals used to build each feature. |
| `site/live/` | The landing page. |
| `loja/` | Former store backend (Stripe + Cloudflare Worker). Kept for reference. |

Most docs and code comments are in Portuguese.

## Building

Requirements: Windows, HUMANKIND (Steam or Epic), [BepInEx 5.4.23.5 x64](https://github.com/BepInEx/BepInEx/releases)
installed in the game folder, and the .NET 8 SDK.

The projects reference the game's DLLs by relative path, so clone this repo as a folder named `_Modding` **inside the
game folder**:

```
cd "C:\Program Files (x86)\Steam\steamapps\common\Humankind"
git clone https://github.com/lcaresia/realpolitik.git _Modding
dotnet build _Modding\src\CurrencyMod.Loader -c Release
dotnet build _Modding\src\CurrencyMod -c Release
dotnet build _Modding\src\MoreEmpires -c Release
```

To build the installer (`Realpolitik_Setup_<version>.exe`) you also need Inno Setup 6:

```
powershell -ExecutionPolicy Bypass -File _Modding\tools\gerar-release.ps1 -Versao 1.1.0
```

Installed layout, config options and dev commands: `docs/mapa-do-projeto.md`, `docs/instalacao.md`,
`docs/diplomacia-ia.md`.

## Secrets

API keys are never stored in the repo. In game, provider keys are saved encrypted (Windows DPAPI) under
`BepInEx\config\credenciais\`. The deploy scripts read their tokens from a local `.env` (see `.env.example`), which is
git-ignored.

## License

Code: [MIT](LICENSE). Third-party components (BepInEx, HarmonyX, MonoMod, Mono.Cecil, UnityDoorstop) keep their own
licenses, listed in `installer/textos/THIRD-PARTY.txt` and `installer/vendor/licencas/`.
