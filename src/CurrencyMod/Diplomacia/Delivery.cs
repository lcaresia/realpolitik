using System;
using CurrencyMod.Diplomacia.Capture;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Letter delivery time by the sender's era ([IA] AtrasoCartasPorEra).
    /// Phase 1: only the era counts; distance comes in together with the mail screen (phase 2).
    /// </summary>
    internal static class Delivery
    {
        internal static int TurnsFor(WorldCapture capture, int senderEmpire) => TurnsForEra(capture?.Empire(senderEmpire)?.EraIndex ?? 0);

        internal static int TurnsForEra(int era)
        {
            era = Math.Max(0, era);
            string[] parts = (IaConfig.DeliveryDelayByEra.Value ?? string.Empty).Split(',');
            if (parts.Length == 0)
            {
                return 1;
            }
            string value = parts[Math.Min(era, parts.Length - 1)].Trim();
            return int.TryParse(value, out int turns) ? Math.Max(0, Math.Min(10, turns)) : 1;
        }

        /// <summary>How the letter travels, by the sender's era (flavor text only).</summary>
        internal static string CourierName(int era)
        {
            // Only shown in the UI (mail and Letters tab); the dossier uses Describe, which stays in Portuguese.
            if (era <= 2) return L.T("mensageiro");
            if (era <= 4) return L.T("mensageiro a cavalo");
            if (era == 5) return L.T("telégrafo");
            return L.T("rádio");
        }

        internal static string Describe(int turns)
        {
            switch (turns)
            {
                case 0: return "chegam no mesmo turno";
                case 1: return "chegam no próximo turno";
                default: return $"chegam em {turns} turnos";
            }
        }
    }
}
