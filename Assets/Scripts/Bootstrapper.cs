using UnityEngine;

namespace ResearchTCG
{
    /// <summary>
    /// Ensures there is a GameManager in any scene the user opens and runs.
    /// Avoids reliance on pre-baked scenes/prefabs.
    /// </summary>
    public static class Bootstrapper
    {
        //[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Init()
        {
            if (Object.FindFirstObjectByType<GameManager>() == null)
            {
                var go = new GameObject("GameManager");
                go.AddComponent<GameManager>();
            }
        }
    }
}