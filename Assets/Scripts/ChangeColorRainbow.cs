using UnityEngine;
using UnityEngine.UI;

public class ChangeColorRainbow : MonoBehaviour
{
    [SerializeField]
    Image image;
    void Update()
    {
        image.color = Color.HSVToRGB(Time.time % 1, 1, 1);
    }
}
