using UnityEngine;
using UnityEngine.InputSystem;

public class CursorManager : MonoBehaviour
{
    [SerializeField]
    Cursor cursor;
    GameObject[] cursorObjects= { };
    int curentIndex;


    public void SetCursorObjects(GameObject[] cursorObjects)
    {
        this.cursorObjects = cursorObjects;
        curentIndex = 0;
        ResetCursor();
    }


    void GotoNext()
    {
        if (cursorObjects.Length == 0)
        {
            return;
        }
        this.curentIndex = (curentIndex + 1) % cursorObjects.Length;
        cursor.transform.position = cursorObjects[curentIndex].transform.position;
    }
    void GotoPrev()
    {
        if (cursorObjects.Length == 0)
        {
            return;
        }
        this.curentIndex = curentIndex==0? cursorObjects.Length:(curentIndex - 1);
        cursor.transform.position = cursorObjects[curentIndex].transform.position;
    }
    void ResetCursor()
    {
        if (cursorObjects.Length == 0)
        {
            return;
        }
        this.curentIndex = 0;
        cursor.transform.position = cursorObjects[curentIndex].transform.position;
    }
    public void OnMove(InputValue value)
    {
        // MoveAction‚Ì“ü—Í’l‚ðŽæ“¾
        var movementInput = value.Get<Vector2>();
        if (movementInput.x > 0)
        {
            GotoNext();
            //currentHorizontalDirection = Direction.Right;
        }
        else if (movementInput.x < 0)
        {
            GotoPrev();
            //currentHorizontalDirection = Direction.Left;
        }
        if (movementInput.y > 0)
        {
            GotoNext();
            //currentHorizontalDirection = Direction.Up;
        }
        else if (movementInput.y < 0)
        {
            GotoPrev();
            //currentHorizontalDirection = Direction.Down;
        }
        else
        {
            //currentHorizontalDirection = Direction.None;
        }

    }
}
