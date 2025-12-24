#if NINTENDO_SDK_PLUGIN_IMPORTED
using System;
using System.Linq;
using nn.hid;
using UnityEngine;
using UnityEngine.InputSystem.Users;
using UnityEngine.UI;

namespace UnityEngine.InputSystem.Switch.Samples.Rebinding
{
    public class RebindingPad : MonoBehaviour
    {
        [SerializeField] PlayerInput input;
        [SerializeField] Text padIDText;
        [SerializeField] Text controlSchemeText;
        [SerializeField] OnScreenLog log;
        
        //Allow only 1 controller (handheld, dualjoy or pad) in this example
        NpadId[] m_AllowedNpadIds = { NpadId.Handheld, NpadId.No1 };
        NpadStyle m_Style = NpadStyle.Handheld | NpadStyle.FullKey | NpadStyle.JoyDual | NpadStyle.JoyLeft | NpadStyle.JoyRight;
#pragma warning disable CS0414 // Field is assigned but its value is never used, suppress editor warning, is used on device
        NpadJoyHoldType m_HoldType = NpadJoyHoldType.Horizontal;
#pragma warning restore CS0414 // Field is assigned but its value is never used
        
        
        //Input action to open the applet for binding controllers - this accepts input from any device, including those 
        //that are unpaired
        [SerializeField]
        InputAction controllerAppletAction;

        void OnEnable()
        {
            InputSystem.onDeviceChange += OnDeviceChange;
            controllerAppletAction.canceled += OnControllerAppletPerformed;
            controllerAppletAction.Enable();
        }

        void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            controllerAppletAction.canceled -= OnControllerAppletPerformed;
        }

        // Start is called before the first frame update
        void Start() 
        {
#if UNITY_SWITCH && !UNITY_EDITOR
            //Configure the supported NPad Styles and Hold Types allowed in the game
            //This allows the system to setup controllers correctly on startup and
            //when ControllerSupport.Show is called
            Npad.SetSupportedIdType(m_AllowedNpadIds);
            NpadJoy.SetHoldType(m_HoldType);
            Npad.SetSupportedStyleSet(m_Style);

            //Treat Dual Joys as a single pad
            //and only active handheld when both JoyCons are connected
            foreach (var ids in m_AllowedNpadIds)
            {
                NpadJoy.SetAssignmentModeDual(ids);
                NpadJoy.SetHandheldActivationMode(NpadHandheldActivationMode.Dual);
            }   

            input.currentActionMap.actionTriggered += OnActionTriggered;
#else
            Debug.LogError($"This Sample is not supported on {Application.platform}, please run on Nintendo Switch");            
#endif
        }

        //Output of all of the actions from the Gamepad, in a regular game you would instead
        //listen for particular actions (e.g. Jump - input.currentActionMap["Jump"].performed)
        void OnActionTriggered(InputAction.CallbackContext obj)
        {
            string value = obj.action.expectedControlType == "Vector2" ? obj.ReadValue<Vector2>().ToString() : obj.ReadValueAsButton().ToString();
            log.AddLine($"{Time.frameCount:D5} :: {obj.action.name} ({obj.control?.displayName}) :: {obj.phase} :: {value}");
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is not NPad nPad)
            {
                return;
            }

            //Check that the supplied style of the NPad is allowed in our defined style set
            if (((int)(m_Style) & (int)(nPad.styleMask)) == 0)
            {
                ControllerSupportApplet.OpenControllerApplet(1,1,true);
                return;
            }

            //Check that no other gamepad has been connected aside from Pad 1 or is in Handheld mode
            if (nPad.npadId != NPad.NpadId.Handheld && nPad.npadId != NPad.NpadId.No1)
            {
                return;
            }

            padIDText.text = nPad.npadId.ToString();

            //Whenever a device changes switch the control scheme for the new layout
            if (change is InputDeviceChange.Added or InputDeviceChange.Removed
                or InputDeviceChange.ConfigurationChanged)
            {
                SwitchControlSchemeFor(nPad);
            }
        }
        
        void OnControllerAppletPerformed(InputAction.CallbackContext obj)
        {
            ControllerSupportApplet.OpenControllerApplet(1,1,true);
        }

        void SwitchControlSchemeFor(NPad pad)
        {
            if (pad == null)
            {
                Debug.LogError("Pad is null");
                return;
            }
            
            /*
                When choosing the control scheme it is common to decide based on what controls are available
                The common scenarios are if we have the full range of controls with either DualJoy, Pro Controller or Handheld
                or if there is a single Joycon which is missing some controls (e.g. Only 1 directional pad, Only 1 Joystick).
                
                In our example configuration the controls are additionally different between if the JoyCon is 
                Left or Right because the assignments or SR & SR change based on if it is the Left or Right Joycon
                see the Input Action Asset for more information
            */
            NPad.NpadStyles style = pad.styleMask;
            string controlSchemeName;
            switch (style)
            {
                case NPad.NpadStyles.JoyDual or NPad.NpadStyles.FullKey or NPad.NpadStyles.Handheld:
                    controlSchemeName = "Full Pad";
                    break;
                case NPad.NpadStyles.JoyLeft:
                    controlSchemeName = "Joy Left";
                    break;
                case NPad.NpadStyles.JoyRight:
                    controlSchemeName = "Joy Right";
                    break;
                default:
                    Debug.LogError($"Unexpected style {style}");
                    return;
            }

            //If this device has not been paired to a user we should pair it and remove the current 
            //devices for this user as we only want 1 gamepad for the single user
            if (!input.user.pairedDevices.Contains(pad))
            {
                input.user.UnpairDevices();
                InputUser.PerformPairingWithDevice(pad, input.user);
            }

            if (input.user.controlScheme.HasValue &&
                input.user.controlScheme.Value.name == controlSchemeName)
            {
                return;
            }

            input.user.ActivateControlScheme(controlSchemeName);
            controlSchemeText.text = input.user.controlScheme.ToString();
        }
    }
}
#endif