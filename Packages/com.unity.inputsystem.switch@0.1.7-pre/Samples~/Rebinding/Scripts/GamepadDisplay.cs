using System;
using UnityEngine.UI;

namespace UnityEngine.InputSystem.Switch.Samples.Rebinding
{
    public class GamepadDisplay : MonoBehaviour
    {
        [SerializeField] StyleImages styleImages;
        [SerializeField] OnScreenLog log;

        NPad m_NPad;

        const float k_VerticalZRotation = 0f;
        const float k_LeftHorizontalZRotation = 90f;
        const float k_RightHorizontalZRotation = -90f;

        void OnEnable()
        {
            InputSystem.onDeviceChange += OnDeviceChange;
        }

        void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is not NPad nPad)
            {
                return;
            }

            if (nPad.npadId != NPad.NpadId.Handheld && nPad.npadId != NPad.NpadId.No1)
            {
                return;
            }
            
            log.AddLine($"{Time.frameCount} :: {device.name} :: {change.ToString()}");

            m_NPad = nPad;
            RefreshPadVisuals();
        }

        void RefreshPadVisuals()
        {
            NPad.NpadStyles style = m_NPad.styleMask;
            NPad.Orientation orientation = m_NPad.orientation;

            foreach (var img in styleImages.all)
            {
                img.enabled = false;
            }

            switch (style)
            {
                case NPad.NpadStyles.FullKey:
                    styleImages.proController.enabled = true;
                    break;
                case NPad.NpadStyles.Handheld:
                    styleImages.handheld.enabled = true;
                    break;
                case NPad.NpadStyles.JoyDual:
                    styleImages.dualJoy.enabled = true;
                    break;
                case NPad.NpadStyles.JoyLeft:
                    styleImages.singleJoyLeft.enabled = true;
                    styleImages.singleJoyLeft.gameObject.transform.eulerAngles = orientation switch
                    {
                        NPad.Orientation.Vertical or NPad.Orientation.Default => new Vector3(0f, 0f, k_VerticalZRotation),
                        NPad.Orientation.Horizontal => new Vector3(0f, 0f, k_LeftHorizontalZRotation),
                        _ => throw new ArgumentOutOfRangeException()
                    };
                    break;
                case NPad.NpadStyles.JoyRight:
                    styleImages.singleJoyRight.enabled = true;
                    styleImages.singleJoyRight.gameObject.transform.eulerAngles = orientation switch
                    {
                        NPad.Orientation.Vertical or NPad.Orientation.Default => new Vector3(0f, 0f, k_VerticalZRotation),
                        NPad.Orientation.Horizontal => new Vector3(0f, 0f, k_RightHorizontalZRotation),
                        _ => throw new ArgumentOutOfRangeException()
                    };
                    break;
                default:
                    Debug.LogError($"Unexpected Style {style.ToString()}");
                    break;
            }
        }

    }
        
    [Serializable]
    public struct StyleImages
    {
        public Image singleJoyLeft;
        public Image singleJoyRight;
        public Image dualJoy;
        public Image handheld;
        public Image proController;

        public Image[] all => new[] { singleJoyLeft, singleJoyRight, dualJoy, handheld, proController };
    }
}
