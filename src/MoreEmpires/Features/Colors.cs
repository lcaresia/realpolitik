using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Amplitude.Framework;
using Amplitude.Mercury.Data;
using Amplitude.Mercury.PlayerProfile;
using HarmonyLib;
using UnityEngine;

namespace MoreEmpires
{
    // ===================================================================================================
    // Tarefa 4 — Cores dos impérios.
    //
    // O jogo tem 12 cores: PaletteDefinition.MajorEmpiresColors[12] (Data\PaletteDefinition.cs:7) e
    // G2GPlayerProfileManager.numberOfColorForSlot = 12 (G2GPlayerProfileManager.cs:138, campo readonly).
    // A cor "do índice = Length" é a dos povos independentes (PaletteContent.GetPaletteColorForSlotOrMinor).
    //
    // Estratégia (só quando MaxImperios > 12):
    //  - todas as PaletteDefinition (padrão e daltonismo) ganham as cores 13..N, geradas contra as 12 da própria paleta;
    //  - numberOfColorForSlot = N desde o construtor (antes de qualquer tela ler o número);
    //  - LoadPalette lê o registro no formato ORIGINAL (12 + 1 cores, G2GPlayerProfileManager.cs:729) e depois completa;
    //  - SaveColors continua gravando só 12 + 1 no registro (o jogo sem o mod continua lendo a paleta do usuário);
    //    as cores 13..N editadas na tela de cores vão para [Cores] PaletaExtra no .cfg do mod.
    // Cada PaletteColor só tem Primary/Secondary/Tertiary; fronteira, bandeira e interface derivam delas
    // (EmpireNamesRepository.cs:105/302 → EmpireInfo.Primary/Secondary/TertiaryColor).
    // ===================================================================================================

    [HarmonyPatch(typeof(G2GPlayerProfileManager), MethodType.Constructor)]
    internal static class G2GPlayerProfileManager_Ctor_Patch
    {
        private static void Postfix(G2GPlayerProfileManager __instance)
        {
            try
            {
                if (Colors.Enabled)
                {
                    Colors.SetNumberOfColors(__instance, Limits.ColorCount);
                }
            }
            catch (Exception ex)
            {
                Log.Exception("G2GPlayerProfileManager..ctor", ex);
            }
        }
    }

    [HarmonyPatch(typeof(G2GPlayerProfileManager), "LoadPalette")]
    internal static class G2GPlayerProfileManager_LoadPalette_Patch
    {
        private static void Prefix(G2GPlayerProfileManager __instance)
        {
            try
            {
                if (!Colors.Enabled)
                {
                    return;
                }
                Colors.ExtendAllPaletteDefinitions();
                // O registro guarda (12 + 1) * 3 cores; com o número em 12 o parse do jogo funciona como sempre.
                Colors.SetNumberOfColors(__instance, Limits.VanillaColorCount);
            }
            catch (Exception ex)
            {
                Log.Exception("LoadPalette(prefix)", ex);
            }
        }

        private static void Postfix(G2GPlayerProfileManager __instance)
        {
            try
            {
                if (Colors.Enabled)
                {
                    Colors.SetNumberOfColors(__instance, Limits.ColorCount);
                    Colors.CompleteRuntimePalettes(__instance);
                }
            }
            catch (Exception ex)
            {
                Log.Exception("LoadPalette(postfix)", ex);
            }
        }

        private static Exception Finalizer(G2GPlayerProfileManager __instance, Exception __exception)
        {
            if (__exception == null || !Colors.Enabled)
            {
                return __exception;
            }
            try
            {
                Colors.SetNumberOfColors(__instance, Limits.ColorCount);
                __instance.ResetToDefaultColors();
                Colors.CompleteRuntimePalettes(__instance);
                Log.Error("LoadPalette falhou e a paleta foi restaurada para o padrão: " + __exception);
                return null;
            }
            catch (Exception repair)
            {
                Log.Error("LoadPalette falhou e não foi possível restaurar: " + repair);
                return __exception;
            }
        }
    }

    /// <summary>G2GPlayerProfileManager.cs:660: o laço grava ColorBySlotIndex.Length cores; limitamos a 12 (formato original).</summary>
    [HarmonyPatch(typeof(G2GPlayerProfileManager), nameof(G2GPlayerProfileManager.SaveColors))]
    internal static class G2GPlayerProfileManager_SaveColors_Patch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = instructions.ToList();
            FieldInfo colors = AccessTools.Field(typeof(PaletteContent), nameof(PaletteContent.ColorBySlotIndex));
            MethodInfo clamp = AccessTools.Method(typeof(Colors), nameof(Colors.ClampSavedCount));
            int hits = 0;
            int at = -1;
            for (int i = 0; i + 2 < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldfld && Equals(list[i].operand, colors) && list[i + 1].opcode == OpCodes.Ldlen && list[i + 2].opcode == OpCodes.Conv_I4)
                {
                    hits++;
                    at = i + 3;
                }
            }
            if (hits != 1)
            {
                Log.Error($"[Transpiler] SaveColors: esperava 1 laço sobre ColorBySlotIndex.Length, achei {hits}. Método mantido original.");
                return list;
            }
            list.Insert(at, new CodeInstruction(OpCodes.Call, clamp));
            Log.Info("[Transpiler] SaveColors: o registro continua com 12 cores + povos independentes.");
            return list;
        }

        private static void Prefix(G2GPlayerProfileManager __instance, out bool __state)
        {
            __state = false;
            try
            {
                __state = __instance.currentHasBeenModified;
            }
            catch
            {
            }
        }

        private static void Postfix(G2GPlayerProfileManager __instance, bool __state)
        {
            try
            {
                if (__state && Colors.Enabled)
                {
                    Colors.SaveExtras(__instance.currentPalette);
                }
            }
            catch (Exception ex)
            {
                Log.Exception("SaveColors(postfix)", ex);
            }
        }
    }

    /// <summary>Paletas de daltonismo (ColorSettingsPanel.cs:438) precisam ter N cores antes de serem aplicadas.</summary>
    [HarmonyPatch(typeof(G2GPlayerProfileManager), nameof(G2GPlayerProfileManager.UsePaletteDefinition))]
    internal static class G2GPlayerProfileManager_UsePaletteDefinition_Patch
    {
        private static void Prefix(PaletteDefinition paletteDefinition)
        {
            try
            {
                if (Colors.Enabled && paletteDefinition != null)
                {
                    Colors.ExtendPaletteDefinition(paletteDefinition);
                }
            }
            catch (Exception ex)
            {
                Log.Exception("UsePaletteDefinition", ex);
            }
        }
    }

    internal static class Colors
    {
        private static FieldInfo numberField;
        internal static readonly List<string> Report = new List<string>();

        internal static bool Enabled => Limits.Active && Limits.ColorCount > Limits.VanillaColorCount;

        /// <summary>Usado pelo transpiler de SaveColors.</summary>
        public static int ClampSavedCount(int length)
        {
            return Enabled ? System.Math.Min(length, Limits.VanillaColorCount) : length;
        }

        internal static void SetNumberOfColors(G2GPlayerProfileManager manager, int count)
        {
            if (numberField == null)
            {
                numberField = AccessTools.Field(typeof(G2GPlayerProfileManager), "numberOfColorForSlot");
            }
            if (numberField != null && manager != null && (int)numberField.GetValue(manager) != count)
            {
                numberField.SetValue(manager, count);
            }
        }

        internal static void ExtendAllPaletteDefinitions()
        {
            IDatabase<PaletteDefinition> database = Databases.GetDatabase<PaletteDefinition>(instantiateNewDatabaseUponFailure: true);
            if (database == null)
            {
                return;
            }
            foreach (PaletteDefinition definition in database)
            {
                ExtendPaletteDefinition(definition);
            }
        }

        internal static void ExtendPaletteDefinition(PaletteDefinition definition)
        {
            int target = Limits.ColorCount;
            PaletteColor[] colors = definition.MajorEmpiresColors;
            if (colors == null || colors.Length >= target || colors.Length == 0)
            {
                return;
            }
            PaletteColor[] extras = ColorGenerator.Generate(colors, definition.MinorEmpiresColor, target - colors.Length);
            definition.MajorEmpiresColors = colors.Concat(extras).ToArray();
            string line = $"Paleta '{definition.name}': {colors.Length} → {target} cores (+ {string.Join(" ", extras.Select(c => "#" + ColorUtility.ToHtmlStringRGB(c.PrimaryColor)).ToArray())}).";
            Report.Add(line);
            Log.Info(line);
        }

        /// <summary>Depois do LoadPalette: completa padrão, atual e "última salva" até N cores.</summary>
        internal static void CompleteRuntimePalettes(G2GPlayerProfileManager manager)
        {
            int target = Limits.ColorCount;
            ref PaletteContent defaults = ref manager.defaultPalette;
            ref PaletteContent current = ref manager.currentPalette;
            if (defaults.ColorBySlotIndex == null || defaults.ColorBySlotIndex.Length < target)
            {
                PaletteColor[] baseColors = defaults.ColorBySlotIndex ?? new PaletteColor[0];
                defaults.ColorBySlotIndex = baseColors.Concat(ColorGenerator.Generate(baseColors, defaults.MinorFactionColor, target - baseColors.Length)).ToArray();
            }
            if (current.ColorBySlotIndex == null || current.ColorBySlotIndex.Length == 0)
            {
                defaults.CopyTo(ref current);
            }
            if (current.ColorBySlotIndex.Length < target)
            {
                PaletteColor[] mine = current.ColorBySlotIndex;
                PaletteColor[] extras = LoadExtras(target - mine.Length) ?? ColorGenerator.Generate(mine, current.MinorFactionColor, target - mine.Length);
                current.ColorBySlotIndex = mine.Concat(extras).ToArray();
            }
            current.CopyTo(ref manager.lastSavedPalette);
            Report.Add($"Paleta em uso: {current.ColorBySlotIndex.Length} cores; registro preservado no formato original (12 + 1).");
            Log.Info($"Cores: paleta em uso com {current.ColorBySlotIndex.Length} cores (12 do jogo/usuário + {current.ColorBySlotIndex.Length - Limits.VanillaColorCount} do mod).");
        }

        internal static void SaveExtras(PaletteContent palette)
        {
            if (palette.ColorBySlotIndex == null || palette.ColorBySlotIndex.Length <= Limits.VanillaColorCount)
            {
                return;
            }
            var parts = new List<string>();
            for (int i = Limits.VanillaColorCount; i < palette.ColorBySlotIndex.Length; i++)
            {
                PaletteColor c = palette.ColorBySlotIndex[i];
                parts.Add(ColorUtility.ToHtmlStringRGBA(c.PrimaryColor));
                parts.Add(ColorUtility.ToHtmlStringRGBA(c.SecondaryColor));
                parts.Add(ColorUtility.ToHtmlStringRGBA(c.TertiaryColor));
            }
            if (MeConfig.PaletaExtra == null)
            {
                return;
            }
            MeConfig.PaletaExtra.Value = string.Join(",", parts.ToArray());
            Log.Info($"Cores: {parts.Count / 3} cores extras salvas em [Cores] PaletaExtra.");
        }

        private static PaletteColor[] LoadExtras(int count)
        {
            string raw = MeConfig.PaletaExtra?.Value;
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }
            string[] parts = raw.Split(',');
            if (parts.Length < count * 3)
            {
                Log.Warn($"[Cores] PaletaExtra tem {parts.Length / 3} cores; esperava {count}. Usando cores geradas.");
                return null;
            }
            var result = new PaletteColor[count];
            for (int i = 0; i < count; i++)
            {
                if (!ColorUtility.TryParseHtmlString("#" + parts[i * 3], out result[i].PrimaryColor)
                    || !ColorUtility.TryParseHtmlString("#" + parts[i * 3 + 1], out result[i].SecondaryColor)
                    || !ColorUtility.TryParseHtmlString("#" + parts[i * 3 + 2], out result[i].TertiaryColor))
                {
                    Log.Warn("[Cores] PaletaExtra inválida; usando cores geradas.");
                    return null;
                }
            }
            return result;
        }

        internal static string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine($"Cores: {(Enabled ? "ativas" : "originais (12)")}, alvo {Limits.ColorCount}.");
            foreach (string line in Report)
            {
                text.AppendLine(line);
            }
            try
            {
                IColorPaletteService service = Services.GetService<IColorPaletteService>();
                if (service != null)
                {
                    text.AppendLine($"NumberOfColorForSlot = {service.NumberOfColorForSlot}");
                    var palette = default(PaletteContent);
                    service.FillColorPalette(ref palette);
                    for (int i = 0; i < palette.ColorBySlotIndex.Length; i++)
                    {
                        PaletteColor c = palette.ColorBySlotIndex[i];
                        text.AppendLine($"  {i,2}: #{ColorUtility.ToHtmlStringRGB(c.PrimaryColor)} #{ColorUtility.ToHtmlStringRGB(c.SecondaryColor)} #{ColorUtility.ToHtmlStringRGB(c.TertiaryColor)}");
                    }
                    text.AppendLine($"  povos independentes: #{ColorUtility.ToHtmlStringRGB(palette.MinorFactionColor.PrimaryColor)}");
                    text.AppendLine("Menor distância de cor (ΔE) entre impérios: " + ColorGenerator.MinDistanceReport(palette.ColorBySlotIndex));
                }
            }
            catch (Exception ex)
            {
                text.AppendLine("(serviço de cores indisponível: " + ex.Message + ")");
            }
            return text.ToString();
        }
    }

    /// <summary>Ponte entre PaletteColor (Unity) e o gerador ColorMath (sem Unity, testável fora do jogo).</summary>
    internal static class ColorGenerator
    {
        internal static PaletteColor[] Generate(IList<PaletteColor> natives, PaletteColor minor, int count)
        {
            List<ColorTriple> triples = natives.Select(c => new ColorTriple
            {
                Primary = ToRgba(c.PrimaryColor),
                Secondary = ToRgba(c.SecondaryColor),
                Tertiary = ToRgba(c.TertiaryColor),
            }).ToList();
            return ColorMath.Generate(triples, ToRgba(minor.PrimaryColor), count)
                .Select(t => new PaletteColor { PrimaryColor = ToColor(t.Primary), SecondaryColor = ToColor(t.Secondary), TertiaryColor = ToColor(t.Tertiary) })
                .ToArray();
        }

        internal static string MinDistanceReport(PaletteColor[] colors)
        {
            if (colors == null || colors.Length < 2)
            {
                return "-";
            }
            double d = ColorMath.MinDistance(colors.Select(c => ToRgba(c.PrimaryColor)).ToList(), out int a, out int b);
            return $"{d.ToString("0.0", CultureInfo.InvariantCulture)} (cores {a} e {b})";
        }

        private static Rgba ToRgba(Color c) => new Rgba(c.r, c.g, c.b, c.a);

        private static Color ToColor(Rgba c) => new Color(c.R, c.G, c.B, c.A);
    }
}