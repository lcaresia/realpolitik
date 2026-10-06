using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.Simulation;
using BepInEx;
using HarmonyLib;

namespace MoreEmpires
{
    // ===================================================================================================
    // Tarefa 8 — Desempenho: medição da passagem de turno.
    //
    // Do clique em "fim de turno" (Empire.SetReady(true) do império local) até a tela mostrar o turno seguinte
    // (GameSnapshot.PresentationData.CurrentTurn muda). Fases: espera pela IA (até SandboxState_TurnFinish.Begin),
    // processamento do fim/início de turno (até SandboxState_TurnMain.Begin) e apresentação. Também soma o tempo do ciclo
    // de decisão da IA (AIController.RunAIDecisionCycle, thread própria — a ordem dos cérebros NÃO é alterada) e o de
    // VisibilityController.UpdateVisibilityBits. Saída: LogOutput.log ("[Turno] ...") e BepInEx\MoreEmpires\turnos.csv.
    // ===================================================================================================

    [HarmonyPatch(typeof(Empire), nameof(Empire.SetReady))]
    internal static class Empire_SetReady_Patch
    {
        private static void Postfix(Empire __instance, bool isReady)
        {
            try
            {
                TurnTimer.OnSetReady(__instance, isReady);
            }
            catch (Exception ex)
            {
                Log.Exception("Empire.SetReady", ex);
            }
        }
    }

    [HarmonyPatch(typeof(SandboxState_TurnFinish), nameof(SandboxState_TurnFinish.Begin))]
    internal static class SandboxState_TurnFinish_Begin_Patch
    {
        private static void Postfix()
        {
            TurnTimer.Mark(ref TurnTimer.FinishTicks);
        }
    }

    [HarmonyPatch(typeof(SandboxState_TurnMain), nameof(SandboxState_TurnMain.Begin))]
    internal static class SandboxState_TurnMain_Begin_Patch
    {
        [HarmonyPriority(Priority.First)]
        private static void Postfix()
        {
            TurnTimer.Mark(ref TurnTimer.MainTicks);
        }
    }

    [HarmonyPatch(typeof(Amplitude.Mercury.AI.AIController), "RunAIDecisionCycle")]
    internal static class AIController_RunAIDecisionCycle_Patch
    {
        private static void Prefix(out long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }

        private static void Postfix(long __state)
        {
            Interlocked.Add(ref TurnTimer.AiTicks, Stopwatch.GetTimestamp() - __state);
        }
    }

    [HarmonyPatch(typeof(VisibilityController), nameof(VisibilityController.UpdateVisibilityBits))]
    internal static class VisibilityController_UpdateVisibilityBits_Patch
    {
        private static void Prefix(out long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }

        private static void Postfix(long __state)
        {
            long elapsed = Stopwatch.GetTimestamp() - __state;
            Interlocked.Add(ref TurnTimer.VisibilityTicks, elapsed);
            Interlocked.Increment(ref TurnTimer.VisibilityCalls);
            VisibilityFast.NoteUpdate(elapsed);
        }
    }

    internal static class TurnTimer
    {
        internal static long ClickTicks;
        internal static int ClickTurn = -1;
        internal static long FinishTicks;
        internal static long MainTicks;
        internal static long AiTicks;
        internal static long VisibilityTicks;
        internal static long VisibilityCalls;

        private static int lastUiTurn = -1;
        private static long aiAtClick;
        private static long visibilityAtClick;
        private static long visibilityCallsAtClick;
        private static string csvPath;

        internal sealed class Record
        {
            internal int FromTurn;
            internal int Majors;
            internal int Minors;
            internal double Total;
            internal double WaitAi;
            internal double Processing;
            internal double Presentation;
            internal double AiBusy;
            internal double Visibility;
            internal long VisibilityCalls;
        }

        internal static readonly List<Record> Records = new List<Record>();

        internal static void Mark(ref long field)
        {
            if (Interlocked.Read(ref ClickTicks) != 0)
            {
                Interlocked.Exchange(ref field, Stopwatch.GetTimestamp());
            }
        }

        internal static void OnSetReady(Empire empire, bool isReady)
        {
            if (!MeConfig.Medir)
            {
                return;
            }
            Sandbox sandbox = SandboxManager.Sandbox;
            if (sandbox == null || empire == null || empire.Index != sandbox.LocalEmpireIndex || !empire.IsControlledByHuman)
            {
                return;
            }
            if (isReady && empire.IsReady)
            {
                ClickTurn = sandbox.Turn;
                Interlocked.Exchange(ref FinishTicks, 0);
                Interlocked.Exchange(ref MainTicks, 0);
                aiAtClick = Interlocked.Read(ref AiTicks);
                visibilityAtClick = Interlocked.Read(ref VisibilityTicks);
                visibilityCallsAtClick = Interlocked.Read(ref VisibilityCalls);
                Interlocked.Exchange(ref ClickTicks, Stopwatch.GetTimestamp());
            }
            else if (!isReady && sandbox.Turn == ClickTurn)
            {
                // O jogador desfez o fim de turno: descarta a medição.
                Interlocked.Exchange(ref ClickTicks, 0);
            }
        }

        internal static void MainThreadUpdate()
        {
            GameSnapshot snapshot = Snapshots.GameSnapshot;
            if (snapshot == null || snapshot.PresentationData == null)
            {
                lastUiTurn = -1;
                // Saiu da partida no meio do fim de turno: sem isto, o próximo save carregado gravava um "turno" com os
                // minutos de menu dentro.
                Interlocked.Exchange(ref ClickTicks, 0);
                return;
            }
            int uiTurn = snapshot.PresentationData.CurrentTurn;
            if (uiTurn == lastUiTurn)
            {
                return;
            }
            lastUiTurn = uiTurn;
            long click = Interlocked.Read(ref ClickTicks);
            if (click == 0 || uiTurn <= ClickTurn)
            {
                return;
            }
            Interlocked.Exchange(ref ClickTicks, 0);
            long now = Stopwatch.GetTimestamp();
            long finish = Interlocked.Read(ref FinishTicks);
            long main = Interlocked.Read(ref MainTicks);
            var record = new Record
            {
                FromTurn = ClickTurn,
                Majors = snapshot.PresentationData.NumberOfMajorEmpires,
                Minors = snapshot.PresentationData.NumberOfMinorEmpires,
                Total = Seconds(now - click),
                WaitAi = finish > click ? Seconds(finish - click) : 0,
                Processing = main > finish && finish > 0 ? Seconds(main - finish) : 0,
                Presentation = main > 0 ? Seconds(now - main) : 0,
                AiBusy = Seconds(Interlocked.Read(ref AiTicks) - aiAtClick),
                Visibility = Seconds(Interlocked.Read(ref VisibilityTicks) - visibilityAtClick),
                VisibilityCalls = Interlocked.Read(ref VisibilityCalls) - visibilityCallsAtClick,
            };
            lock (Records)
            {
                Records.Add(record);
            }
            Log.Info($"[Turno] {record.FromTurn}→{record.FromTurn + 1}: {record.Total:0.00} s (espera IA {record.WaitAi:0.00} s, " +
                     $"processamento {record.Processing:0.00} s, tela {record.Presentation:0.00} s; IA ocupada {record.AiBusy:0.00} s; " +
                     $"visibilidade {record.Visibility * 1000:0} ms em {record.VisibilityCalls} chamadas) — {record.Majors} maiores, {record.Minors} menores.");
            AppendCsv(record);
        }

        private static double Seconds(long ticks) => ticks / (double)Stopwatch.Frequency;

        private static void AppendCsv(Record record)
        {
            try
            {
                if (csvPath == null)
                {
                    string dir = Path.Combine(Paths.BepInExRootPath, "MoreEmpires");
                    Directory.CreateDirectory(dir);
                    csvPath = Path.Combine(dir, "turnos.csv");
                    if (!File.Exists(csvPath))
                    {
                        File.WriteAllText(csvPath, "data;turno;maiores;menores;total_s;espera_ia_s;processamento_s;tela_s;ia_ocupada_s;visibilidade_ms;visibilidade_chamadas\n");
                    }
                }
                CultureInfo c = CultureInfo.InvariantCulture;
                File.AppendAllText(csvPath, string.Join(";", new[]
                {
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", c), record.FromTurn.ToString(c), record.Majors.ToString(c), record.Minors.ToString(c),
                    record.Total.ToString("0.000", c), record.WaitAi.ToString("0.000", c), record.Processing.ToString("0.000", c),
                    record.Presentation.ToString("0.000", c), record.AiBusy.ToString("0.000", c), (record.Visibility * 1000).ToString("0.0", c),
                    record.VisibilityCalls.ToString(c),
                }) + "\n");
            }
            catch (Exception ex)
            {
                Log.Exception("turnos.csv", ex);
            }
        }

        internal static string Describe(int last)
        {
            var text = new StringBuilder();
            List<Record> copy;
            lock (Records)
            {
                copy = Records.ToList();
            }
            text.AppendLine($"Passagens de turno medidas nesta sessão: {copy.Count} (arquivo: BepInEx\\MoreEmpires\\turnos.csv).");
            if (copy.Count == 0)
            {
                return text.ToString();
            }
            foreach (IGrouping<int, Record> group in copy.GroupBy(r => r.Majors))
            {
                double[] totals = group.Select(r => r.Total).OrderBy(x => x).ToArray();
                text.AppendLine($"{group.Key} maiores: {totals.Length} turnos, média {totals.Average():0.00} s, mediana {totals[totals.Length / 2]:0.00} s, " +
                                $"máx {totals.Max():0.00} s; IA ocupada média {group.Average(r => r.AiBusy):0.00} s; visibilidade média {group.Average(r => r.Visibility) * 1000:0} ms.");
            }
            foreach (Record r in copy.Skip(System.Math.Max(0, copy.Count - last)))
            {
                text.AppendLine($"  turno {r.FromTurn}: {r.Total:0.00} s (IA {r.WaitAi:0.00} / proc {r.Processing:0.00} / tela {r.Presentation:0.00}; visib. {r.Visibility * 1000:0} ms)");
            }
            return text.ToString();
        }
    }
}
