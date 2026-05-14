using UnityEngine;
using TMPro;

public class CarController : MonoBehaviour
{
    public float speed = 15f;
    public float turnSpeed = 80f;

    // 儲存手機畫面的虛擬按鈕輸入值
    private float mobileMove = 0f;
    private float mobileTurn = 0f;

    [Header("儀表板 UI 設定")]
    public TMP_Text speedText; // 用來裝我們剛剛建立的文字 UI
    private Vector3 lastPosition; // 記憶車子上一秒的位置

    void Start()
    {
        // 遊戲一開始，先記錄車子的起始位置
        lastPosition = transform.position;
    }

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

        // === 以下是新增的「測速照相機」邏輯 ===

        // 1. 算出這個影格內，車子移動了多遠 (目前位置 減去 上一幀位置)
        float distance = Vector3.Distance(transform.position, lastPosition);

        // 2. 距離除以時間 = 真實秒速 (m/s)
        float speedMS = distance / Time.deltaTime;

        // 3. 秒速乘上 3.6 轉換成我們熟悉的時速 (km/h)，並四捨五入成整數
        int speedKMH = Mathf.RoundToInt(speedMS * 3.6f);

        // 4. 把算出來的時速，寫入畫面上的文字 UI 裡
        if (speedText != null)
        {
            // 讓數字前面補 0（例如變成 005 km/h，更有儀表板的感覺）
            speedText.text = speedKMH.ToString("000") + " km/h";
        }

        // 5. 紀錄這次的位置，給下一個影格繼續算
        lastPosition = transform.position;
    }

    // --- 以下是開放給手機 UI 按鈕呼叫的專屬函數 ---
    public void PressForward() { mobileMove = 1f; }
    public void PressBackward() { mobileMove = -1f; }
    public void ReleaseMove() { mobileMove = 0f; } // 放開油門或倒車

    public void PressRight() { mobileTurn = 1f; }
    public void PressLeft() { mobileTurn = -1f; }
    public void ReleaseTurn() { mobileTurn = 0f; } // 放開方向盤
}
