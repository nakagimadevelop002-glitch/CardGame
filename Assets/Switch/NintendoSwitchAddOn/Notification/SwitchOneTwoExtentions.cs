using UnityEngine;
using System;

namespace SwitchOneTwo.Extentions
{
    public class NintendoNotificationWrapper
    {
        public void EnterExitRequestHandlingSection()
        {
#if UNITY_SWITCH
            UnityEngine.Switch.Notification.EnterExitRequestHandlingSection();
#elif UNITY_SWITCH2
            UnityEngine.Nintendo.Notification.EnterExitRequestHandlingSection();
#endif
        }

        public void LeaveExitRequestHandlingSection()
        {
#if UNITY_SWITCH
            UnityEngine.Switch.Notification.LeaveExitRequestHandlingSection();
#elif UNITY_SWITCH2
            UnityEngine.Nintendo.Notification.LeaveExitRequestHandlingSection();
#endif
        }
    }
}