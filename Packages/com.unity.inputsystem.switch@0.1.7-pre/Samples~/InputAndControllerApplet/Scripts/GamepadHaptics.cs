using System;

namespace UnityEngine.InputSystem.Switch.Samples.InputAndControllerApplet
{
    public class GamepadHaptics : MonoBehaviour
    {
        [SerializeField] SampleGamepad sampleGamepad;

        [Header("Motor Speeds")]
        [SerializeField] MotorSpeedParameters leftParameters;
        [SerializeField] MotorSpeedParameters rightParameters;
        
        void OnEnable()
        {
            sampleGamepad.Actions["RumbleLeft"].performed += HandleLeftRumblePressed;
            sampleGamepad.Actions["RumbleRight"].performed += HandleRightRumblePressed;
            sampleGamepad.Actions["RumbleLeft"].canceled += HandleLeftRumblePressed;
            sampleGamepad.Actions["RumbleRight"].canceled += HandleRightRumblePressed;
        }

        void OnDisable()
        {
            sampleGamepad.Actions["RumbleLeft"].performed -= HandleLeftRumblePressed;
            sampleGamepad.Actions["RumbleRight"].performed -= HandleRightRumblePressed;
            sampleGamepad.Actions["RumbleLeft"].canceled -= HandleLeftRumblePressed;
            sampleGamepad.Actions["RumbleRight"].performed -= HandleRightRumblePressed;
        }

        public void HandleLeftRumblePressed(InputAction.CallbackContext ctx)
        {
            //In Single JoyCon Mode the left stick is the only stick and the JoyCon only has 1 motor that 
            //is the same as the JoyCon style (Left or Right)
            if (ctx.control.device is NPad { styleMask: NPad.NpadStyles.JoyLeft or NPad.NpadStyles.JoyRight } nPad)
            {
                bool left = nPad.styleMask == NPad.NpadStyles.JoyLeft;
                bool right = nPad.styleMask == NPad.NpadStyles.JoyRight;

                if (ctx.performed)
                {
                    StartRumble(left, right);
                    return;
                }
                if (ctx.canceled)
                {
                    StopRumble(left, right);
                    return;
                }
            }
            
            if (ctx.performed)
            {
                StartRumble(true, false);
            }else if (ctx.canceled)
            {
                StopRumble(true, false);
            }
        }

        public void HandleRightRumblePressed(InputAction.CallbackContext ctx)
        {
            if (ctx.performed)
            {
                StartRumble(false, true);
            }
            else if (ctx.canceled)
            {
                StopRumble(false, true);
            }
        }
        
        public void StartRumble(bool left, bool right)
        {
            /*
             * Start rumble on the left or right motor of an NPad for DualJoy & Handheld the Left and Right Motors
             * match to the Left and Right JoyCons.
             *
             * For FullKey (Pro Controller) there is a motor in each side of the controller
             *
             * Single JoyCons match SetMotorSpeedLeft/Right to the appropriate JoyCon where SetMotorSpeedLeft will
             * set the motor speed for a JoyLeft and SetMotorSpeedRight for a JoyRight
             */
            
            if (left)
            {
                sampleGamepad.NPad.SetMotorSpeedLeft(leftParameters.lowAmplitude, leftParameters.lowFrequency, 
                    leftParameters.highAmplitude, leftParameters.highFrequency);
            }

            if (right)
            {
                sampleGamepad.NPad.SetMotorSpeedRight(rightParameters.lowAmplitude, rightParameters.lowFrequency, 
                    rightParameters.highAmplitude, rightParameters.highFrequency);
            }
        }
        
        public void StopRumble(bool left, bool right)
        {
            if (left)
            {
                sampleGamepad.NPad.SetMotorSpeedLeft(0f,0f,0f,0f);
            }

            if (right)
            {
                sampleGamepad.NPad.SetMotorSpeedRight(0f,0f,0f,0f);
            }
        }
    }

    [Serializable]
    public struct MotorSpeedParameters
    {
        public float lowAmplitude;
        public float lowFrequency;
        public float highAmplitude;
        public float highFrequency;
    }
}
