using System;
using UnityEngine;
using UnityEngine.UI;

namespace UnityEngine.InputSystem.Switch.Samples.InputAndControllerApplet
{
    /// <summary>
    /// Class to display the inputs of the gamepad, gamepad type and colours
    /// </summary>
    public class GamepadDisplay : MonoBehaviour
    {
        [SerializeField] SampleGamepad SampleGamepad;
        [SerializeField] StyleImages styleImages;

        [SerializeField] Image leftColourImage, rightColourImage;
        
        [Header("Controls Display")]
        [SerializeField] ButtonImages buttonImages;
        [SerializeField] GameObject buttonImagePrefab;
        [SerializeField] Transform buttonParent;

        [SerializeField] Text leftStickDisplay;
        [SerializeField] Text rightStickDisplay;
        
        [Header("Info")]
        [SerializeField] Text titleText;
        [SerializeField] Text infoText;
        
        const float k_VerticalZRotation = 0f;
        const float k_LeftHorizontalZRotation = 90f;
        const float k_RightHorizontalZRotation = -90f;

        Vector2 m_lastDpad;

        void Update()
        {
            /*
             * Draw all of the buttons to the screen if they are pressed. In a regular game you would bind to specific
             * actions (e.g. Press Jump - samplePadActions["Jump"].Performed += MyJumpFunction;
             */
            
            //Control Pad (XABY)
            var samplePadActions = SampleGamepad.Actions;
            UpdateButtonDraw(buttonImages.xabyPad.north, samplePadActions["Press X"].IsPressed(), samplePadActions["Press X"].WasPerformedThisFrame());
            UpdateButtonDraw(buttonImages.xabyPad.east, samplePadActions["Press A"].IsPressed(), samplePadActions["Press A"].WasPerformedThisFrame());
            UpdateButtonDraw(buttonImages.xabyPad.south, samplePadActions["Press B"].IsPressed(), samplePadActions["Press B"].WasPerformedThisFrame());
            UpdateButtonDraw(buttonImages.xabyPad.west, samplePadActions["Press Y"].IsPressed(), samplePadActions["Press Y"].WasPerformedThisFrame());
            
            //Dpad
            Vector2 dpadValue = samplePadActions["DPad"].ReadValue<Vector2>();
            UpdateButtonDraw(buttonImages.dpad.north, dpadValue.y > 0f, !(m_lastDpad.y > 0));
            UpdateButtonDraw(buttonImages.dpad.south, dpadValue.y < 0f, !(m_lastDpad.y < 0));
            UpdateButtonDraw(buttonImages.dpad.east, dpadValue.x > 0f, !(m_lastDpad.x > 0));
            UpdateButtonDraw(buttonImages.dpad.west, dpadValue.x < 0f, !(m_lastDpad.x < 0));
            m_lastDpad = dpadValue;
            
            //Sticks
            Vector2 leftStickValue = samplePadActions["LeftStick"].ReadValue<Vector2>();
            Vector2 rightStickValue = samplePadActions["RightStick"].ReadValue<Vector2>();
            UpdateButtonDraw(buttonImages.leftStickMove, leftStickValue.sqrMagnitude > 0f, samplePadActions["LeftStick"].WasPressedThisFrame());
            UpdateButtonDraw(buttonImages.rightStickMove, rightStickValue.sqrMagnitude > 0f, samplePadActions["RightStick"].WasPressedThisFrame());
            UpdateButtonDraw(buttonImages.leftStickDown, samplePadActions["LeftStickPress"].IsPressed(), samplePadActions["LeftStickPress"].WasPressedThisFrame());
            UpdateButtonDraw(buttonImages.rightStickDown, samplePadActions["RightStickPress"].IsPressed(), samplePadActions["RightStickPress"].WasPressedThisFrame());
            
            //Bumpers
            UpdateButtonDraw(buttonImages.l, samplePadActions["Press L"].IsPressed(), samplePadActions["Press L"].WasPerformedThisFrame());
            UpdateButtonDraw(buttonImages.r, samplePadActions["Press R"].IsPressed(), samplePadActions["Press R"].WasPerformedThisFrame());
            UpdateButtonDraw(buttonImages.zl, samplePadActions["Press ZL"].IsPressed(), samplePadActions["Press ZL"].WasPerformedThisFrame());
            UpdateButtonDraw(buttonImages.zr, samplePadActions["Press ZR"].IsPressed(), samplePadActions["Press ZR"].WasPerformedThisFrame());
            UpdateButtonDraw(buttonImages.sr, samplePadActions["Press SR"].IsPressed(), samplePadActions["Press SR"].WasPerformedThisFrame());
            UpdateButtonDraw(buttonImages.sl, samplePadActions["Press SL"].IsPressed(),  samplePadActions["Press SL"].WasPerformedThisFrame());
            
            //Extra Buttons (+,-)
            UpdateButtonDraw(buttonImages.plus, samplePadActions["Press Plus"].IsPressed(), samplePadActions["Press Plus"].WasPerformedThisFrame());
            UpdateButtonDraw(buttonImages.minus, samplePadActions["Press Minus"].IsPressed(), samplePadActions["Press Minus"].WasPerformedThisFrame());
            
            //Display the values from the sticks
            UpdateStickValueDisplay(leftStickDisplay, samplePadActions["LeftStick"]);
            UpdateStickValueDisplay(rightStickDisplay, samplePadActions["RightStick"]);
            
            UpdateInfoDraw();
        }

        void UpdateInfoDraw()
        {
            if (SampleGamepad.NPad == null)
            {
                return;
            }
            
            titleText.text = SampleGamepad.NPad.npadId.ToString();

            if (SampleGamepad.NPad.styleMask == NPad.NpadStyles.JoyDual)
            {
                infoText.text = ($"Left Connected: {SampleGamepad.NPad?.isLeftConnected} :: Wired: {SampleGamepad.NPad?.isLeftWired}\n");
                infoText.text += ($"Right Connected: {SampleGamepad.NPad?.isRightConnected} :: Wired: {SampleGamepad.NPad?.isRightWired}");
            }
            else
            {
                infoText.text = ($"Connected: {SampleGamepad.NPad?.isConnected} :: Wired: {SampleGamepad.NPad?.isWired}\n");
            }
        }

        public void RefreshPadVisuals()
        {
            NPad.NpadStyles style = SampleGamepad.NPad.styleMask;
            NPad.Orientation orientation = SampleGamepad.NPad.orientation;
            
            foreach (var img in styleImages.all)
            {
                img.enabled = false;
            }
             
            //Display the correct image for the current NPad config.
            //Rotate if required (i.e. Left/Right Joycon is Horizontal or Vertical)
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
                    Debug.LogError($"Unexpected Style");
                    break;
            }

            leftColourImage.color = SampleGamepad.NPad.leftControllerColor.Main;
            rightColourImage.color = SampleGamepad.NPad.rightControllerColor.Main;
        }

        static void UpdateStickValueDisplay(Text textObject, InputAction action)
        {
            if (!action.IsPressed())
            {
                textObject.enabled = false;
                return;
            }

            textObject.enabled = true;
            textObject.text = action.ReadValue<Vector2>().ToString("F2");
        }
        
        static void UpdateButtonDraw(Image image, bool isPressed, bool startedThisFrame)
        {
            if (image == null)
            {
                return;
            }
            
            if (startedThisFrame)
            {
                image.transform.SetAsLastSibling();
            }
            
            image.gameObject.SetActive(isPressed);
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

    [Serializable]
    public struct ButtonImages
    {
        public Image leftStickDown;
        public Image rightStickDown;
        public Image leftStickMove;
        public Image rightStickMove;
        public FourDirectionImages dpad;
        public FourDirectionImages xabyPad;
        
        public Image l;
        public Image r;
        public Image zr;
        public Image zl;
        
        public Image sl;
        public Image sr;
        
        public Image plus;
        public Image minus;
        public Image home;
    }

    [Serializable]
    public struct FourDirectionImages
    {
        public Image north;
        public Image east;
        public Image south;
        public Image west;
    }
}