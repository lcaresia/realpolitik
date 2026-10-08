# Reference assemblies

`Managed/` holds **reference assemblies** of HUMANKIND, Unity and Newtonsoft.Json: type and member signatures only.
Every method body was stripped by [JetBrains Refasmer](https://github.com/JetBrains/Refasmer), so there is no game
code here. They exist only so the mod can be compiled without the game installed (GitHub Actions).
The game itself is required to run the mod.

Regenerate them after a game update:

```
dotnet tool install -g JetBrains.Refasmer.CliTool
refasmer --all --omit-non-api-members=false -c -O _Modding\refs\Managed <each DLL referenced by the .csproj files, from Humankind_Data\Managed>
```

HUMANKIND is © Amplitude Studios / SEGA. These files are not covered by this repository's MIT license.
