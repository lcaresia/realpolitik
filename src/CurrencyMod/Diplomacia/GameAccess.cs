using System;
using System.Text.RegularExpressions;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;

namespace CurrencyMod.Diplomacia
{
    /// <summary>
    /// O pouco que a IA lê do jogo fora da foto do turno, na thread principal e só pelos snapshots de apresentação
    /// (cópias seguras que a própria interface usa).
    /// </summary>
    internal static class GameAccess
    {
        private static readonly Regex RichTextTags = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex Symbols = new Regex(@"\[[^\]]*\]", RegexOptions.Compiled);

        /// <summary>Partida carregada: GUID, turno (o mesmo número que o jogador vê) e índice do jogador local.</summary>
        internal static bool TryGetSession(out string guid, out int turn, out int playerIndex)
        {
            guid = null;
            turn = -1;
            playerIndex = -1;
            try
            {
                Sandbox sandbox = SandboxManager.Sandbox;
                if (sandbox == null || Snapshots.GameSnapshot == null || sandbox.IsSessionOnline)
                {
                    return false;
                }
                GameSnapshot.Data game = Snapshots.GameSnapshot.PresentationData;
                if (game == null || string.IsNullOrEmpty(game.GameID) || game.NumberOfMajorEmpires <= 0 || game.EmpireInfo == null)
                {
                    return false;
                }
                playerIndex = game.LocalEmpireInfo.EmpireIndex;
                if (playerIndex < 0 || playerIndex >= game.EmpireInfo.Length)
                {
                    return false;
                }
                guid = sandbox.GUID.ToString();
                // A tela de fim de turno mostra CurrentTurn direto (EndTurnWindow); o primeiro turno é 1.
                turn = game.CurrentTurn;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>O jogo está no turno principal (jogadores agindo), não no meio da virada ou do autosave.</summary>
        internal static bool IsTurnMain() => SandboxStateName() == "SandboxState_TurnMain";

        internal static string SandboxStateName()
        {
            try
            {
                return Snapshots.SandboxSnapshot?.PresentationData?.CurrentSandboxStateName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Tira marcações de texto rico (&lt;color&gt;) e de ícones ([EmpireSymbol_...]).</summary>
        internal static string Clean(string text) => Symbols.Replace(RichTextTags.Replace(text ?? string.Empty, string.Empty), string.Empty).Trim();
    }
}
