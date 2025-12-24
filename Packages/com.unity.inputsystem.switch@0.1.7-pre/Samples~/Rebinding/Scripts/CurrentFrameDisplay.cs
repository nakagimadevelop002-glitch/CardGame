using System;
using UnityEngine.UI;

namespace UnityEngine.InputSystem.Switch.Samples.Rebinding
{
    public class CurrentFrameDisplay : MonoBehaviour
    {
        [SerializeField] Text frameDisplayText;
        
        void Update()
        {
            frameDisplayText.text = $"Frame: {Time.frameCount:D5}";
        }
    }
}
