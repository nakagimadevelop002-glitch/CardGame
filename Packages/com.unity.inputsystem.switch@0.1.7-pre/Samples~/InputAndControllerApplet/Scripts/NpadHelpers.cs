using System;

namespace UnityEngine.InputSystem.Switch.Samples.InputAndControllerApplet
{
    public static class NpadHelpers
    {
        public static int GetSlotForNPad(NPad.NpadId npadId)
        {
            return npadId switch
            {
                NPad.NpadId.Debug => -1,
                NPad.NpadId.Invalid => -1,
                NPad.NpadId.Handheld => 0,
                _ => (int)npadId
            };
        }

        public static NPad GetNpadFromNPadId(NPad.NpadId id)
        {
            foreach (var pad in Gamepad.all)
            {
                if (pad is not NPad nPad)
                {
                    continue;
                }

                if (nPad.npadId == id)
                {
                    return nPad;
                }
            }

            return null;
        }
    }
}