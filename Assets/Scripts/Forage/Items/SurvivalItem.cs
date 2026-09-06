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
        Branch,      // shelter building
        LeafBundle,  // shelter building
        Flint,       // strike two together for sparks
        Fish         // caught from the pond; cook it!
    }

    /// <summary>Marks a grabbable survival item and its properties.</summary>
    public class SurvivalItem : MonoBehaviour
    {
        public ItemKind kind;
        public bool isWet;
    }
}
