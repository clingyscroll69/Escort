using System.Collections.Generic;
using HS.Core;
using UnityEngine;

namespace HS.Tutorial
{
    /// <summary>
    /// Freeze-frame lessons, the pause menu and the Field Guide each hold a token here: while any token pauses, the sim is
    /// paused; while any blocks gameplay, the Gameplay action map is off (so the key that closes a card can't also dodge
    /// or stab). When the last token goes, whatever was there before comes back (a sim the flow had paused stays paused).
    /// </summary>
    public static class ModalGate
    {
        sealed class Token
        {
            public string Reason;
            public bool Pause, Block;
        }

        static readonly List<Token> Tokens = new List<Token>();
        static int _pausing, _blocking;
        static bool _pausedBefore, _gameplayBefore;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Tokens.Clear();
            _pausing = _blocking = 0;
        }

        public static int Count => Tokens.Count;
        public static bool Any => Tokens.Count > 0;
        public static bool Has(string reason) => Tokens.Exists(t => t.Reason == reason);

        public static object Push(string reason, bool pauseSim = true, bool blockGameplay = true)
        {
            var t = new Token { Reason = reason, Pause = pauseSim, Block = blockGameplay };
            Tokens.Add(t);
            if (pauseSim && _pausing++ == 0)
            {
                var loop = SimLoop.Instance;
                _pausedBefore = loop != null && loop.Paused;
                if (loop != null) loop.Paused = true;
            }
            if (blockGameplay && _blocking++ == 0)
            {
                var map = GameInput.Instance.Gameplay;
                _gameplayBefore = map.enabled;
                map.Disable();
            }
            return t;
        }

        /// <summary>Unknown or already-popped tokens are ignored.</summary>
        public static void Pop(object token)
        {
            if (!(token is Token t) || !Tokens.Remove(t)) return;
            if (t.Pause && --_pausing == 0)
            {
                var loop = SimLoop.Instance;
                if (loop != null) loop.Paused = _pausedBefore;
            }
            if (t.Block && --_blocking == 0 && _gameplayBefore) GameInput.Instance.Gameplay.Enable();
        }

        /// <summary>Release everything (a new scene starts clean even if an owner never got to pop).</summary>
        public static void Clear()
        {
            while (Tokens.Count > 0) Pop(Tokens[Tokens.Count - 1]);
        }
    }
}
