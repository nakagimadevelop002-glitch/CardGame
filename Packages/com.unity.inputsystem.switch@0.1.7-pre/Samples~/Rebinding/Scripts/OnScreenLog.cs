using System.Collections.Generic;
using System.Text;
using UnityEngine.UI;

namespace UnityEngine.InputSystem.Switch.Samples.Rebinding
{
    public class OnScreenLog : MonoBehaviour
    {
        [SerializeField] Text displayText;
        [SerializeField] int maxLines = 6;
        [SerializeField] Gradient displayGradient;
        
        public Queue<string> textQueue = new Queue<string>();
        
        public void AddLine(string text)
        {
            textQueue.Enqueue(text);

            while (textQueue.Count > maxLines)
            {
                textQueue.Dequeue();
            }
            
            UpdateTextDisplay();
        }

        private void UpdateTextDisplay()
        {
            StringBuilder sb = new StringBuilder(textQueue.Count);

            int count = 0;
            foreach (var text in textQueue)
            {
                Color c = displayGradient.Evaluate(1 - (float)count / maxLines);
                string t = $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{text}</color>\n";
                sb.Insert(0, t);
                ++count;
            }

            displayText.text = sb.ToString();
        }
    }
}
