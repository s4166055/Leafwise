using UnityEngine;

namespace Forage
{
    /// <summary>
    /// Unity's XR Interaction Simulator sample ships a UI script that throws a
    /// NullReferenceException every frame (ClearOtherActiveInputPanels). It is
    /// editor-only cosmetics and never ships to Quest, so we disable it to keep
    /// the console readable during testing.
    /// </summary>
    public class SimulatorUiFix : MonoBehaviour
    {
        void Start()
        {
            if (!Application.isEditor) { enabled = false; return; }
            InvokeRepeating(nameof(Sweep), 0.5f, 2f);
        }

        void Sweep()
        {
            int disabled = 0;
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb == null || !mb.enabled) continue;
                if (mb.GetType().Name == "XRInteractionSimulatorInputFeedbackUI")
                {
                    mb.enabled = false;
                    disabled++;
                }
            }
            if (disabled > 0)
                Debug.Log($"[Forage] Disabled {disabled} buggy simulator feedback UI component(s) (Unity sample bug).");
        }
    }
}
