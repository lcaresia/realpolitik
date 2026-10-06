using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

// Ensaio fora do jogo: carrega os assemblies reais do jogo e aplica todos os patches do MoreEmpires com Harmony.
// Não executa nenhum método do jogo; só resolve alvos, roda os transpilers e compila os métodos substitutos.
public static class Program
{
    private const string Game = @"C:\Program Files (x86)\Steam\steamapps\common\Humankind\";

    public static int Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string name = new AssemblyName(args.Name).Name;
            foreach (string dir in new[] { Game + @"Humankind_Data\Managed\", Game + @"BepInEx\core\", Game + @"_Modding\src\MoreEmpires\bin\Release\net472\" })
            {
                string path = Path.Combine(dir, name + ".dll");
                if (File.Exists(path))
                {
                    return Assembly.LoadFrom(path);
                }
            }
            return null;
        };
        return Run();
    }

    private static int Run()
    {
        Assembly plugin = typeof(MoreEmpires.MoreEmpiresPlugin).Assembly;
        var source = new ManualLogSource("MoreEmpires");
        source.LogEvent += (sender, e) => Console.WriteLine($"   [{e.Level}] {e.Data}");
        Type log = plugin.GetType("MoreEmpires.Log");
        log.GetMethod("Init", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { source });

        var harmony = new Harmony("patchcheck");
        int ok = 0;
        int failed = 0;
        Type[] types;
        try
        {
            types = plugin.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            Console.WriteLine("Falha ao listar tipos: " + string.Join("\n", ex.LoaderExceptions.Select(e => e.Message).Distinct()));
            return 2;
        }
        foreach (Type type in types.Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0).OrderBy(t => t.Name))
        {
            try
            {
                var processor = harmony.CreateClassProcessor(type);
                var patched = processor.Patch();
                Console.WriteLine($"OK     {type.Name} → {string.Join(", ", patched.Select(m => m.DeclaringType?.Name + "." + m.Name))}");
                ok++;
            }
            catch (Exception ex)
            {
                Exception root = ex;
                while (root.InnerException != null)
                {
                    root = root.InnerException;
                }
                Console.WriteLine($"FALHOU {type.Name}: {root.GetType().Name}: {root.Message}");
                failed++;
            }
        }
        Console.WriteLine($"\nPatches: {ok} ok, {failed} falharam.");

        // Transpilers rodados diretamente sobre o IL original (sem compilar), para os casos que o .NET Framework recusa.
        foreach (var (patchType, targetType, method) in new[]
        {
            ("MoreEmpires.LobbySlotsPanel_PostLoad_Patch", "Amplitude.Mercury.UI.LobbyScreen_LobbySlotsPanel, Assembly-CSharp", "PostLoad"),
            ("MoreEmpires.LobbySlotsPanel_Refresh_Patch", "Amplitude.Mercury.UI.LobbyScreen_LobbySlotsPanel, Assembly-CSharp", "Refresh"),
            ("MoreEmpires.G2GPlayerProfileManager_SaveColors_Patch", "Amplitude.Mercury.PlayerProfile.G2GPlayerProfileManager, Assembly-CSharp", "SaveColors"),
        })
        {
            MethodBase original = AccessTools.Method(Type.GetType(targetType, true), method);
            var instructions = PatchProcessor.GetOriginalInstructions(original);
            MethodInfo transpiler = plugin.GetType(patchType).GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic);
            var result = ((System.Collections.Generic.IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { instructions })).ToList();
            Console.WriteLine($"\n{targetType.Split(',')[0]}.{method}: {instructions.Count} → {result.Count} instruções; trechos alterados:");
            foreach (CodeInstruction ci in result.Where(c => c.operand is MethodInfo m && m.DeclaringType != null && m.DeclaringType.Namespace == "MoreEmpires"))
            {
                int i = result.IndexOf(ci);
                for (int k = Math.Max(0, i - 3); k <= Math.Min(result.Count - 1, i + 2); k++)
                {
                    Console.WriteLine($"     {(k == i ? ">" : " ")} {result[k]}");
                }
                Console.WriteLine("     ---");
            }
        }
        return failed == 0 ? 0 : 1;
    }
}
