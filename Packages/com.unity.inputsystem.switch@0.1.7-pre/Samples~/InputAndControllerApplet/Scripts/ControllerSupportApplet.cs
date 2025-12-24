#if NINTENDO_SDK_PLUGIN_IMPORTED
using System;
using nn;
using nn.hid;

namespace UnityEngine.InputSystem.Switch.Samples.InputAndControllerApplet
{
    public static class ControllerSupportApplet
    {
        public static void OpenControllerApplet(byte maxPlayerCount, byte minPlayerCount, bool singleMode)
        {
            /*
             * The Controller Applet allows players to reconfigure the controller styles
             * The allowed styles in this dialog are defined by the arguments passed to
             * Npad.SetSupportedStyleSet
             *
             * When only 1 controller is supported enableSingleMode must be set to true to support Handheld
             */
            
            ControllerSupportArg arg = new ControllerSupportArg();
            arg.SetDefault();
            arg.playerCountMax = maxPlayerCount;
            arg.playerCountMin = minPlayerCount;
            arg.enableSingleMode = singleMode;

            Result result = ControllerSupport.Show(arg, suspendUnityThreads: true);

            if (!result.IsSuccess())
            {
                //Allow canceled to be a valid result, other results indicate a real error,
                //most commonly when an invalid JoyCon Style set has been supplied to 
                //Npad.SetSupportedStyleSet
                if (!ControllerSupport.ResultCanceled.Includes(result))
                {
                    Debug.LogError($"ERROR {result.GetDescription()}");
                }
                else
                {
                    Debug.Log("Canceled Dialog");
                }
            }
        }
    }
}
#endif