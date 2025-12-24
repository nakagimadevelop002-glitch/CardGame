using System;
using UnityEngine;
using UnityEngine.InputSystem.Users;

namespace UnityEngine.InputSystem.Switch.Samples.InputAndControllerApplet
{
    public class SampleGamepad : MonoBehaviour
    {
        [SerializeField]
        PlayerInput playerInput;
        [SerializeField]
        GamepadDisplay m_GamepadDisplay;

        public InputActionMap Actions => playerInput.currentActionMap;
        
        public NPad NPad { get; private set; }
        NPad.NpadId m_NPadID;

        public void Setup(NPad pad)
        {
            m_NPadID = pad.npadId;
            NPad = pad;
            PairDevice();
            InputSystem.onDeviceChange  += OnDeviceChange;
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change != InputDeviceChange.ConfigurationChanged)
            {
                return;
            }

            if (device is not NPad nPad)
            {
                return;
            }

            //NPad Devices can change without being given a removed/added event
            //so we should do comparisions based on if the device has the same NPad *ID* rather than matching
            //the device. We should then update the device based on whatever is the new device at the Npad *ID*
            if (nPad.npadId != m_NPadID)
            {
                return;
            }

            NPad = NpadHelpers.GetNpadFromNPadId(nPad.npadId);
            if (NPad == null)
            {
                Debug.LogError($"Failed to find an NPad for {nPad.npadId}");
                return;
            }

            //Set the current controller to the Input Actions
            PairDevice();
        }

        void PairDevice()
        {
            playerInput.user.UnpairDevices();
            InputUser.PerformPairingWithDevice(NPad, playerInput.user);
            m_GamepadDisplay.RefreshPadVisuals();
        }
    }
}