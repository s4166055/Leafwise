using System;

namespace Forage
{
    /// <summary>
    /// Tiny static event bus. Mechanics raise gameplay signals here;
    /// the Scout helper (C7) and UI subscribe without direct references.
    /// </summary>
    public static class ForageEvents
    {
        /// <summary>Contextual hint trigger, e.g. "fire-no-tinder", "wood-damp", "drill-too-slow".</summary>
        public static event Action<string> Hint;

        /// <summary>General gameplay signal, e.g. "fire-lit", "water-boiled".</summary>
        public static event Action<string> Signal;

        public static void RaiseHint(string id) => Hint?.Invoke(id);
        public static void RaiseSignal(string id) => Signal?.Invoke(id);
    }
}
