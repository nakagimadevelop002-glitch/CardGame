#if NINTENDO_SDK_PLUGIN_IMPORTED
using System;
using System.Collections.Generic;
using System.Linq;
using nn.hid;
using UnityEngine;
using UnityEngine.UI;

namespace UnityEngine.InputSystem.Switch.Samples.InputAndControllerApplet
{
    public class GamepadManager : MonoBehaviour
    {
        [SerializeField]
        ControllerConfigStyle playStyle;

        [SerializeField]
        GameObject gamepadPrefab;
        [SerializeField]
        SampleGamepad[] gamePads;

        //Input action to open the applet for binding controllers - this accepts input from any device, including those 
        //that are unpaired
        [SerializeField]
        InputAction controllerAppletAction;

        [SerializeField]
        Text modeDisplayText;

        void Awake()
        {
            foreach (var pad in gamePads)
            {
                pad.gameObject.SetActive(false);
            }
        }

        void OnEnable()
        {
            InputSystem.onDeviceChange += InputSystemOnDeviceChange;
            controllerAppletAction.canceled += OnControllerAppletPerformed;
            controllerAppletAction.Enable();

            modeDisplayText.text = $"Press + or - to open Controller Support Applet | Supported Styles: {Utils.GetSetFlagsAsString(playStyle.styles)} | Supported Hold Type {playStyle.holdType}";
        }

        void OnDisable()
        {
            InputSystem.onDeviceChange -= InputSystemOnDeviceChange;
            controllerAppletAction.canceled -= OnControllerAppletPerformed;
        }

        void Start()
        {
            /*
                Configure the supported NPad Styles and Hold Types allowed in the game
                This allows the system to setup controllers correctly on startup and
                when ControllerSupport.Show is called.
                This will OVERRIDE anything set in your PlayerSettings, in some games it may be sufficient to have
                these values set once in PlayerSettings
            */
#if UNITY_SWITCH && !UNITY_EDITOR
            Npad.SetSupportedIdType(playStyle.AllowedNpadIds);
            NpadJoy.SetHoldType(playStyle.holdType);
            Npad.SetSupportedStyleSet(playStyle.styles);

            //Treat any existing JoyCons as Single JoyCons
            foreach (var ids in playStyle.AllowedNpadIds)
            {
                NpadJoy.SetAssignmentModeSingle(ids);
            }
#else
            Debug.LogError($"This Sample is not supported on {Application.platform}, please run on Nintendo Switch");            
#endif
        }


        void InputSystemOnDeviceChange(InputDevice device, InputDeviceChange changed)
        {
            if (device is not NPad nPad)
            {
                return;
            }

            int slot = NpadHelpers.GetSlotForNPad(nPad.npadId);
            if (slot == -1 || slot >= gamePads.Length) //Invalid or Debug Pad
            {
                return;
            }

            /* There are 3 types of event that we need to be handling in this example:
                1. Device Added - This can be called when a brand new device is added or if a the controller type has changed
                what physical device is being used (e.g. from DualJoy to Handheld)
                2. Device Removed - When a device is disconnected and we no longer care about it's inputs. This can happen
                in conjunction with 1. when the physical device changes
                3. Device Configuration Changed - This is when the JoyCon has changed Style (e.g DualJoy -> JoyLeft) or 
                Orientation. This can happen without a Added event because the physical device is the same, for example
                the Left Joycon in a DualJoy style will remain as the same physical device when it transitions to being
                it's own separate controller.
                In this stage we need to check that the device configuration matches an allowed configuration for the game
             */
            
            switch (changed)
            {
                case InputDeviceChange.Added:
                    gamePads[slot].gameObject.SetActive(true);
                    gamePads[slot].Setup(nPad);
                    break;
                case InputDeviceChange.Removed:
                    gamePads[slot].gameObject.SetActive(false);
                    break;
                case InputDeviceChange.ConfigurationChanged:
                    
                    //A ConfigurationChanged can be sent without an associated added event
                    gamePads[slot].gameObject.SetActive(true);
                    if (((int)(playStyle.styles) & (int)(nPad.styleMask)) == 0)
                    {
                        Debug.Log($"{nPad.npadId} with {nPad.styleMask} is not valid with the current styles {playStyle.styles.ToString()}. Will now show ControllerSupport UI");
                        ControllerSupportApplet.OpenControllerApplet(playStyle.maxPlayerCount, playStyle.minPlayerCount, playStyle.singleMode);
                    }

                    break;
            }
        }

        void OnControllerAppletPerformed(InputAction.CallbackContext obj)
        {
            controllerAppletAction.Disable(); // Do not allow action to take place while showing applet
            ControllerSupportApplet.OpenControllerApplet(playStyle.maxPlayerCount, playStyle.minPlayerCount, playStyle.singleMode);
            controllerAppletAction.Enable();
        }
    }
}
#endif