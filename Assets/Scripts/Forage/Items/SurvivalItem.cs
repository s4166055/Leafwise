using UnityEngine;

namespace Forage
{
    public enum ItemKind
    {
        Stick,
        Tinder,
        DrillStick,
        Pot,
        Mushroom,
        Branch,      // shelter building (C7)
        LeafBundle,  // shelter building (C7)
        Flint        // strike two together for sparks
    }

    /// <summary>Marks a grabbable survival item and its properties.</summary>
    public class SurvivalItem : MonoBehaviour
    {
        public ItemKind kind;
        public bool isWet;
    }
}
