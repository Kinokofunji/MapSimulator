using UnityEngine;

public class CarController : MonoBehaviour
{
    public float speed = 15f;
    public float turnSpeed = 80f;

    // 儲存手機畫面的虛擬按鈕輸入值
    private float mobileMove = 0f;
    private float mobileTurn = 0f;

    void Update()
    {
        // 結合電腦鍵盤與手機按鈕的輸入（只要其中一個有動作，車子就會動）
        float finalMove = Input.GetAxis("Vertical") + mobileMove;
        float finalTurn = Input.GetAxis("Horizontal") + mobileTurn;

        // 限制數值在 -1 到 1 之間，避免玩家同時按鍵盤又按手機導致速度暴走
        finalMove = Mathf.Clamp(finalMove, -1f, 1f);
        finalTurn = Mathf.Clamp(finalTurn, -1f, 1f);

        // 執行移動與旋轉
        transform.Translate(Vector3.forward * finalMove * speed * Time.deltaTime);
        transform.Rotate(Vector3.up * finalTurn * turnSpeed * Time.deltaTime);
    }

    // --- 以下是開放給手機 UI 按鈕呼叫的專屬函數 ---
    public void PressForward() { mobileMove = 1f; }
    public void PressBackward() { mobileMove = -1f; }
    public void ReleaseMove() { mobileMove = 0f; } // 放開油門或倒車

    public void PressRight() { mobileTurn = 1f; }
    public void PressLeft() { mobileTurn = -1f; }
    public void ReleaseTurn() { mobileTurn = 0f; } // 放開方向盤
}
