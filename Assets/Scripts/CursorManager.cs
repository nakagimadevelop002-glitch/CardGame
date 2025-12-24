using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class CursorManager : MonoBehaviour
{
    [SerializeField]
    Cursor cursor;
    //GameObject[] cursorObjects= { };
    int curentIndex;
    const float UPDATE_CURSOR_TIMER = 1;
    float currentTimer = 0;
    [SerializeField]
    List<Button> buttons = new List<Button>();

    private void Update()
    {
        currentTimer += Time.deltaTime;
        if(currentTimer> UPDATE_CURSOR_TIMER)
        {
            
            currentTimer = 0;
            var newButtons = FindObjectsByType<Button>(FindObjectsSortMode.InstanceID).OrderBy(item=>item.transform.GetSiblingIndex()).ToList();
            //TODO:グルーピングを行い上部と下部でボタン分けする
            bool same = buttons.SequenceEqual(newButtons);
            if (!same)
            {
                SetCursorObjects(newButtons);
            }
            
        }
    }


    public void SetCursorObjects(List<Button> buttons)
    {
        this.buttons = buttons;
        curentIndex = 0;
        ResetCursor();
    }


    void GotoNext()
    {
        if (buttons.Count == 0)
        {
            return;
        }
        this.curentIndex = (curentIndex + 1) % buttons.Count;
        cursor.transform.position = buttons[curentIndex].transform.position;

        SetCursorSize();
    }

    Vector2 GetSize(RectTransform rectTransform)
    {
        if (rectTransform == null) return Vector2.zero;
        return rectTransform.rect.size;
    }

    void GotoPrev()
    {
        if (buttons.Count == 0)
        {
            return;
        }
        this.curentIndex = curentIndex==0? buttons.Count: (curentIndex - 1);
        cursor.transform.position = buttons[curentIndex].transform.position;
        SetCursorSize();
    }

    void SetCursorSize()
    {
        var size = GetSize(buttons[curentIndex].GetComponent<RectTransform>());
        cursor.GetComponent<RectTransform>().sizeDelta = size;
    }

    void ResetCursor()
    {
        if (buttons.Count == 0)
        {
            return;
        }
        this.curentIndex = 0;
        cursor.transform.position = buttons[curentIndex].transform.position;
        SetCursorSize();
    }
    public void OnAttack(InputValue value)
    {
        if (buttons.Count == 0)
        {
            return;
        }
        buttons[curentIndex].onClick.Invoke();
        //Debug.Log("OnAttack");
    }
    public void OnMove(InputValue value)
    {
        // MoveActionの入力値を取得
        var movementInput = value.Get<Vector2>();
        if (movementInput.x > 0)
        {
            GotoNext();
        }
        else if (movementInput.x < 0)
        {
            GotoPrev();
        }
        if (movementInput.y > 0)
        {
            GotoNext();
        }
        else if (movementInput.y < 0)
        {
            GotoPrev();
        }
        else
        {
        }

    }
}
