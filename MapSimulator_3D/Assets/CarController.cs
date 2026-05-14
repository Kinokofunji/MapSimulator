using UnityEngine;
using TMPro;

public class CarController : MonoBehaviour
{
    public float speed = 15f;
    

    // 儲存手機畫面的虛擬按鈕輸入值
    private float mobileMove = 0f;
    private float mobileTurn = 0f;

    [Header("車輛物理參數")]
    public float maxSpeed = 15f;      // 最高速度限制
    public float acceleration = 5f;   // 加速力道 (數值越大，起步越快)
    public float deceleration = 3f;   // 煞車/滑行阻力 (數值越小，滑行越遠)
    public float turnSpeed = 100f;    // 控制車子左右轉的速度
    private float currentSpeed = 0f;  // 記憶車輛當前的真實速度 (不要在介面上改它)
    public Rigidbody rb;  // 宣告 rb 變數

    [Header("儀表板 UI 設定")]
    public TMP_Text speedText; // 用來裝我們剛剛建立的文字 UI
    private Vector3 lastPosition; // 記憶車子上一秒的位置

    void Start()
    {
        // 遊戲一開始，先記錄車子的起始位置
        lastPosition = transform.position;
    }

    void FixedUpdate()
    {
        // 1. 透過內積 (Dot Product) 算出車子在真實世界中，往車頭方向的「實際速度」
        float actualForwardSpeed = Vector3.Dot(transform.forward, rb.velocity);

        // 2. 如果「大腦以為的速度(currentSpeed)」跟「真實速度」落差太大 (代表撞牆被擋住了)
        if (Mathf.Abs(currentSpeed) - Mathf.Abs(actualForwardSpeed) > 1f)
        {
            // 強制讓大腦清醒，把速度降到跟現實一樣。
            // 這樣你一按倒車，速度就會直接從 0 開始往後扣，瞬間脫困！
            currentSpeed = actualForwardSpeed;
        }
        // 1. 取得玩家輸入
        // Vertical 是前後 (W/S 或 上/下)
        // Horizontal 是左右 (A/D 或 左/右)
        float moveInput = Input.GetAxisRaw("Vertical"); 
        float turnInput = Input.GetAxisRaw("Horizontal"); // 讀取左右按鍵
        // 方向盤死區 (Deadzone)
        // 只要左右輸入的數值小於 0.1，就當作沒按，強制歸零。
        // 這能徹底解決因為設備飄移或雜訊導致的「直走偏向」問題！
        if (Mathf.Abs(turnInput) < 0.1f)
        {
            turnInput = 0f;
        }
        // 2. 升級版油門與煞車邏輯
        if (Mathf.Abs(moveInput) > 0)
        {
            // 判斷是否在「煞車/急換檔」：輸入方向(moveInput)與當前速度(currentSpeed)正負號相反
            // 例如：正在往前(速度為正)，但玩家按下退(輸入為負)
            if (moveInput * currentSpeed < 0)
            {
                // 給予 2 倍的減速力道，讓你撞牆後倒車能迅速切換爆發！
                currentSpeed = Mathf.MoveTowards(currentSpeed, maxSpeed * moveInput, deceleration * 2f * Time.deltaTime);
            }
            else
            {
                // 正常踩油門加速
                currentSpeed = Mathf.MoveTowards(currentSpeed, maxSpeed * moveInput, acceleration * Time.deltaTime);
            }
        }
        else
        {
            // 放開按鍵，自然滑行
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0, deceleration * Time.deltaTime);
        }

        // 3. 套用前進速度到物理引擎
        Vector3 forwardMove = transform.forward * currentSpeed;
        rb.velocity = new Vector3(forwardMove.x, rb.velocity.y, forwardMove.z);

        // === 左右轉向 (Steering) ===
        float currentRealSpeed = rb.velocity.magnitude;  // 只看目前的真實物理速度 (magnitude)        
        if (currentRealSpeed > 0.1f)  // 速度大於 0.1 才有轉向能力
        {
            // 倒車時反轉方向盤
            float directionMultiplier = (Vector3.Dot(transform.forward, rb.velocity) > 0) ? 1f : -1f;

            // ⚠️ 關鍵：把轉向角度乘上 (當前速度 / 最高速度) 的比例！
            // 這樣開得越慢，轉彎越緩慢，原地絕對轉不動。
            // 只要速度超過 3 (可依手感微調)，方向盤靈敏度就會達到 100%
            float speedFactor = Mathf.Clamp01(currentRealSpeed / 3f);
            float turnAmount = turnInput * turnSpeed * directionMultiplier * speedFactor * Time.fixedDeltaTime;

            transform.Rotate(Vector3.up * turnAmount);
        }
    }

    void Update()
    {
        // 如果你有綁定 speedText，才執行更新
        if (speedText != null && rb != null)
        {
            // rb.velocity.magnitude 會直接回傳真實的移動秒速
            float speedMS = rb.velocity.magnitude;
            int speedKMH = Mathf.RoundToInt(speedMS * 3.6f);
            speedText.text = speedKMH.ToString("000") + " km/h";
        }
    }

    // --- 以下是開放給手機 UI 按鈕呼叫的專屬函數 ---
    public void PressForward() { mobileMove = 1f; }
    public void PressBackward() { mobileMove = -1f; }
    public void ReleaseMove() { mobileMove = 0f; } // 放開油門或倒車

    public void PressRight() { mobileTurn = 1f; }
    public void PressLeft() { mobileTurn = -1f; }
    public void ReleaseTurn() { mobileTurn = 0f; } // 放開方向盤
}
