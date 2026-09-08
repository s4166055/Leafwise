using UnityEngine;

namespace Forage
{
    /// <summary>
    /// Central registry of shared runtime materials, assigned once by the
    /// scene builder. Runtime systems read from <see cref="Instance"/>.
    /// </summary>
    public class ForageAssets : MonoBehaviour
    {
        static ForageAssets _instance;
        /// <summary>Survives mid-play domain reloads by re-finding itself.</summary>
        public static ForageAssets Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<ForageAssets>();
                return _instance;
            }
        }

        [Header("Item materials")]
        public Material stickWood;
        public Material tinderStraw;
        public Material stone;
        public Material potMetal;
        public Material charredWood;

        [Header("Effect materials")]
        public Material flame;
        public Material smoke;
        public Material ember;

        [Header("Mushroom materials")]
        public Material mushroomStem;
        public Material capBrown;
        public Material capYellow;
        public Material capRed;
        public Material capPale;

        void Awake() => _instance = this;
    }
}
