using System;
using CurrencyMod.Diplomacia.Capture;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// Tempo de entrega das cartas pela era de quem envia ([IA] AtrasoCartasPorEra).
    /// Fase 1: só a era conta; a distância entra junto com a tela do correio (fase 2).
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

        /// <summary>Como a carta viaja, pela era de quem envia (só texto de ambientação).</summary>
        internal static string CourierName(int era)
        {
            // Só aparece na interface (correio e aba Cartas); o dossiê usa Describe, que fica em português.
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
