using System;
using System.Collections.Generic;
using Amplitude;
using Amplitude.Framework;
using Amplitude.Mercury;
using Amplitude.Mercury.Persona;
using Amplitude.Mercury.PlayerProfile;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Session;
using Amplitude.Mercury.Simulation;
using HarmonyLib;

namespace MoreEmpires
{
    // ===================================================================================================
    // Tarefa 5 — Personas, facções e símbolos.
    // ===================================================================================================

    /// <summary>
    /// Session.cs:1854-1866 (FillRandomPersonaForSlot) tira a persona sorteada do grupo e não tem plano B: com o grupo
    /// vazio, GetWeightedRandom devolve 0 e o jogo lê/remove um item que não existe. Quando o grupo acaba, recarregamos com
    /// todas as personas utilizáveis, ficando só as MENOS usadas pelos outros slots (repetir é permitido, mas por último).
    /// </summary>
    [HarmonyPatch(typeof(Session), nameof(Session.FillRandomPersonaForSlot))]
    internal static class Session_FillRandomPersonaForSlot_Patch
    {
        private static bool Prefix(Session __instance, SessionSlot sessionSlot, ListOfStruct<AvailablePersona> availablePersonaList)
        {
            try
            {
                if (availablePersonaList == null || availablePersonaList.Length > 0)
                {
                    return true;
                }
                return Personas.Recycle(__instance, sessionSlot, availablePersonaList);
            }
            catch (Exception ex)
            {
                Log.Exception("Session.FillRandomPersonaForSlot", ex);
                return true;
            }
        }
    }

    /// <summary>G2GPlayerProfileManager.cs:184/1367: DefaultPersonaForFirstLaunch tem 10 itens e é indexado pelo slot.</summary>
    [HarmonyPatch(typeof(G2GPlayerProfileManager), nameof(G2GPlayerProfileManager.TryFillDefaultPersonaForSlot))]
    internal static class G2GPlayerProfileManager_TryFillDefaultPersonaForSlot_Patch
    {
        private static void Prefix(G2GPlayerProfileManager __instance, ref int slotIndex)
        {
            try
            {
                int length = __instance.DefaultPersonaForFirstLaunch?.Length ?? 0;
                if (length > 0 && slotIndex >= length)
                {
                    slotIndex %= length;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("TryFillDefaultPersonaForSlot", ex);
            }
        }
    }

    /// <summary>Session.cs:1915-1919: sem facção livre o jogo já duplica, mas loga erro. Com MaxImperios ativo vira informação.</summary>
    [HarmonyPatch(typeof(Session), "InitializeSessionSlotFaction")]
    internal static class Session_InitializeSessionSlotFaction_Patch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return PatchUtil.ReplaceLogErrorAfterString(instructions, "Could not find any available faction left",
                AccessTools.Method(typeof(Quiet), nameof(Quiet.FactionFallback)), "Session.InitializeSessionSlotFaction");
        }
    }

    /// <summary>Session.cs:1952-1956: idem para os símbolos de império.</summary>
    [HarmonyPatch(typeof(Session), "InitializeSessionSlotSymbol")]
    internal static class Session_InitializeSessionSlotSymbol_Patch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return PatchUtil.ReplaceLogErrorAfterString(instructions, "Could not find any available symbol left",
                AccessTools.Method(typeof(Quiet), nameof(Quiet.SymbolFallback)), "Session.InitializeSessionSlotSymbol");
        }
    }

    /// <summary>
    /// MinorFactionUtils.cs:35-41: povos independentes necessários = maior janela de 2 eras + 5 por império maior, com teto
    /// de 100 (Sandbox.cs:3526 lança exceção acima de 100). Com 16 maiores o teto quase sempre é atingido e o jogo loga erro;
    /// é esperado e inofensivo, então vira aviso.
    /// </summary>
    [HarmonyPatch(typeof(MinorFactionUtils), nameof(MinorFactionUtils.ComputeNumberOfMinorEmpiresNeeded))]
    internal static class MinorFactionUtils_ComputeNumberOfMinorEmpiresNeeded_Patch
    {
        private static void Prefix(SandboxStartSettings sandboxStartSettings)
        {
            Quiet.MajorsAtStart = sandboxStartSettings?.NumberOfMajorEmpires ?? 0;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return PatchUtil.ReplaceLogErrorAfterString(instructions, "Too many minor empires needed",
                AccessTools.Method(typeof(Quiet), nameof(Quiet.MinorCap)), "MinorFactionUtils.ComputeNumberOfMinorEmpiresNeeded");
        }
    }

    internal static class Quiet
    {
        [ThreadStatic]
        internal static int MajorsAtStart;

        public static void FactionFallback(string format, object[] args)
        {
            if (Limits.Active)
            {
                Log.Info("Facção exclusiva repetida (mais impérios que facções disponíveis): " + SafeFormat(format, args));
                return;
            }
            Diagnostics.LogError(format, args);
        }

        public static void SymbolFallback(string format, object[] args)
        {
            if (Limits.Active)
            {
                Log.Info("Símbolo de império repetido (mais impérios que símbolos livres): " + SafeFormat(format, args));
                return;
            }
            Diagnostics.LogError(format, args);
        }

        public static void MinorCap(string format, object[] args)
        {
            if (Limits.Active && MajorsAtStart > Limits.VanillaMaxSlots)
            {
                Log.Warn($"Povos independentes limitados a 100 pelo jogo ({MajorsAtStart} impérios maiores): " + SafeFormat(format, args));
                return;
            }
            Diagnostics.LogError(format, args);
        }

        private static string SafeFormat(string format, object[] args)
        {
            try
            {
                return string.Format(format, args ?? new object[0]);
            }
            catch
            {
                return format;
            }
        }
    }

    internal static class Personas
    {
        internal static int Recycled;

        internal static bool Recycle(Session session, SessionSlot target, ListOfStruct<AvailablePersona> pool)
        {
            IPersonaListingService listing = Services.GetService<IPersonaListingService>();
            if (listing == null)
            {
                Log.Warn("Personas: grupo vazio e serviço de personas indisponível; slot mantido sem sorteio.");
                return false;
            }
            var all = new ListOfStruct<AvailablePersona>();
            listing.FillAvailablePersona(all, fillOnlyUseable: true);
            // Mesmo filtro do jogo (Session.FillListsOfAvailablePersonae): persona com resumo inválido fica de fora.
            IPersonaService personas = Services.GetService<IPersonaService>();
            if (personas != null)
            {
                for (int i = all.Length - 1; i >= 0; i--)
                {
                    if (!personas.IsSummaryValid(ref all.Data[i].PersonaSummary))
                    {
                        all.RemoveAt(i);
                    }
                }
            }
            if (all.Length == 0)
            {
                Log.Warn("Personas: nenhuma persona utilizável; slot mantido sem sorteio.");
                return false;
            }
            var usage = new Dictionary<string, int>();
            foreach (SessionSlot slot in session.Slots)
            {
                if (slot == null || slot == target)
                {
                    continue;
                }
                string key = KeyOf(slot.PersonaSummary.Key);
                usage.TryGetValue(key, out int count);
                usage[key] = count + 1;
            }
            int minUse = int.MaxValue;
            for (int i = 0; i < all.Length; i++)
            {
                usage.TryGetValue(KeyOf(all.Data[i].PersonaSummary.Key), out int count);
                minUse = System.Math.Min(minUse, count);
            }
            for (int i = 0; i < all.Length; i++)
            {
                usage.TryGetValue(KeyOf(all.Data[i].PersonaSummary.Key), out int count);
                if (count == minUse)
                {
                    pool.Add(ref all.Data[i]);
                }
            }
            Recycled++;
            Log.Info($"Personas: o grupo acabou no slot {target?.Index}; sorteando de novo entre {pool.Length} persona(s) usadas {minUse} vez(es).");
            return pool.Length > 0;
        }

        private static string KeyOf(PersonaSummary.PersonaUniqueKey key)
        {
            return ((int)key.Domain).ToString() + ":" + key.UniqueIdentifier.ToString();
        }
    }
}
