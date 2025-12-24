#if NINTENDO_SDK_PLUGIN_IMPORTED
using System;
using nn.hid;
using UnityEngine;
using UnityEngine.Serialization;

namespace UnityEngine.InputSystem.Switch.Samples.InputAndControllerApplet
{
    /// <summary>
    /// Helper class to create a layout for how controllers should be handled in the game
    /// For example in this asset you can create a single player game that only supports dual joycons or handheld play
    /// </summary>
    [CreateAssetMenu(fileName = "Controller Config", menuName = "Assets/Create/Input Demo/Config", order = 0)]
    public class ControllerConfigStyle : ScriptableObject
    {
        const int k_MaxPlayers = 8;

        [SerializeField]
        public NpadId[] AllowedNpadIds;
        [SerializeField, Tooltip("This will override PlayerSettings")]
        public NpadStyle styles;
        [SerializeField]
        public NpadJoyHoldType holdType;
        [FormerlySerializedAs("singlePlayerMode")]
        [SerializeField]
        public bool singleMode;
        [SerializeField, Range(1, k_MaxPlayers)]
        public byte maxPlayerCount = 1;
        [SerializeField, Range(1, k_MaxPlayers)]
        public byte minPlayerCount = 1;
    }
}
#endif