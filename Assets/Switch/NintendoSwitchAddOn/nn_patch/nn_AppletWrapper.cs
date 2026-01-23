using UnityEngine;

namespace SwitchOneTwo.Extentions
{
#if UNITY_SWITCH || UNITY_SWITCH2
    public static class nn_AppletWrapper
    {
        public static void Begin()
        {
#if UNITY_SWITCH
            UnityEngine.Switch.Applet.Begin();

#elif UNITY_SWITCH2
            UnityEngine.Nintendo.Applet.Begin();
#endif
        }

        public static void End()
        {
#if UNITY_SWITCH
            UnityEngine.Switch.Applet.End();

#elif UNITY_SWITCH2
            UnityEngine.Nintendo.Applet.End();
#endif
        }

        public static void LeaveNetworkConnecting()
        {
#if UNITY_SWITCH
            UnityEngine.Switch.NetworkInterfaceWrapper.LeaveNetworkConnecting();

#elif UNITY_SWITCH2
            UnityEngine.Nintendo.NetworkInterfaceWrapper.LeaveNetworkConnecting();
#endif
        }
        public static void EnterNetworkConnecting(bool isLocalNetworkMode, bool reportIfUnavailable)
        {
#if UNITY_SWITCH
            UnityEngine.Switch.NetworkInterfaceWrapper.EnterNetworkConnecting(isLocalNetworkMode, reportIfUnavailable);

#elif UNITY_SWITCH2
            UnityEngine.Nintendo.NetworkInterfaceWrapper.EnterNetworkConnecting(isLocalNetworkMode, reportIfUnavailable);
#endif
        }
        public static void WaitForNetworkConnecting()
        {
#if UNITY_SWITCH
            UnityEngine.Switch.NetworkInterfaceWrapper.WaitForNetworkConnecting();

#elif UNITY_SWITCH2
            UnityEngine.Nintendo.NetworkInterfaceWrapper.WaitForNetworkConnecting();
#endif
        }

        public static bool IsNetworkAccepted()
        {

#if UNITY_SWITCH
            return UnityEngine.Switch.NetworkInterfaceWrapper.IsNetworkAccepted();

#elif UNITY_SWITCH2
            return UnityEngine.Nintendo.NetworkInterfaceWrapper.IsNetworkAccepted();
#endif
        }

        public static bool IsNetworkAvailable()
        {
#if UNITY_SWITCH
            return UnityEngine.Switch.NetworkInterfaceWrapper.IsNetworkAvailable();

#elif UNITY_SWITCH2
            return UnityEngine.Nintendo.NetworkInterfaceWrapper.IsNetworkAvailable();
#endif
        }


    }
#endif

}