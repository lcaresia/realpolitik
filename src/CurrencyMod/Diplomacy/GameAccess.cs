using System;
using System.Text.RegularExpressions;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;

namespace CurrencyMod.Diplomacy
{
    /// <summary>
    /// The little the AI reads from the game outside the turn snapshot, on the main thread and only through the
    /// presentation snapshots (safe copies the UI itself uses).
    /// </summary>
    internal static class GameAccess
    {
        private static readonly Regex RichTextTags = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex Symbols = new Regex(@"\[[^\]]*\]", RegexOptions.Compiled);

        /// <summary>Loaded game: GUID, turn (the same number the player sees) and local player index.</summary>
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
                // The end-of-turn screen shows CurrentTurn directly (EndTurnWindow); the first turn is 1.
                turn = game.CurrentTurn;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>The game is in the main turn phase (players acting), not mid turn change or autosave.</summary>
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

        /// <summary>Strips rich-text markup (&lt;color&gt;) and icon tags ([EmpireSymbol_...]).</summary>
        internal static string Clean(string text) => Symbols.Replace(RichTextTags.Replace(text ?? string.Empty, string.Empty), string.Empty).Trim();
    }
}
