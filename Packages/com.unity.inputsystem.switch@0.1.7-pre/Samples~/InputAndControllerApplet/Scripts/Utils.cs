using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine.InputSystem.Switch.Samples.InputAndControllerApplet
{
    public static class Utils
    {
        public static string GetSetFlagsAsString<T>(T flags) where T : Enum
        {
            // Get all enum values
            var enumValues = Enum.GetValues(typeof(T)).Cast<Enum>();
        
            // Collect the set flags
            var setFlags = new List<string>();
            foreach (var value in enumValues )
            {
                if (flags.HasFlag(value) && !value.Equals(Enum.Parse(typeof(T), "None")))
                {
                    setFlags.Add(value.ToString());
                }
            }
        
            // Convert to a comma-separated string
            string result = string.Join(", ", setFlags);
        
            return result;
        }
    }
}
