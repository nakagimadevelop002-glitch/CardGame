#if UNITY_EDITOR
using System;
using UnityEngine;

namespace UnityEngine.InputSystem.Switch.Samples.Rebinding.Editor
{
    [CreateAssetMenu(menuName = "Create/Readme")]
    public class Readme : ScriptableObject
    {
        public Texture2D icon;
        public string title;
        public Section[] sections;
        public bool loadedLayout;

        [Serializable]
        public class Section
        {
            [TextArea]
            public string heading, text, linkText, url;
        }
    }
}
#endif