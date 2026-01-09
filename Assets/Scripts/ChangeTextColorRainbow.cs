using UnityEngine;
using UnityEngine.UI;

public class ChangeTextColorRainbow : MonoBehaviour
{
    [SerializeField]
    Text text;

    // Update is called once per frame
    void Update()
    {
        text.color = Color.HSVToRGB(Time.time % 1, 1, 1);
    }
}
