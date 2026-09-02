using UnityEngine;

/// <summary>
/// 一鍵重置車輛：記住車輛一開始的位置與朝向，
/// 當車輛卡死、翻覆，或掉出地圖時，可以按按鍵或呼叫 ResetVehicle() 直接傳回起點，不需要重開場景。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CarResetController : MonoBehaviour
{
    [Header("重置按鍵")]
    [Tooltip("按下這個按鍵時，車輛會立刻重置回起點")]
    public KeyCode resetKey = KeyCode.R;

    [Header("掉出地圖自動重置")]
    [Tooltip("勾選後，車輛掉到下方指定高度以下時會自動觸發重置")]
    public bool autoResetWhenFallen = true;

    [Tooltip("車輛 Y 座標低於這個值，視為掉出地圖")]
    public float fallResetHeight = -20f;

    [Header("導航進度")]
    [Tooltip("重置車輛時，若導航還沒完成，一併把已經走過的導航節點還原到起點")]
    public NavigationLineManager lineManager;

    [Header("翻覆自動扶正")]
    [Tooltip("勾選後，車身傾斜超過門檻角度並持續一段時間，會自動原地扶正——" +
             "跟掉出地圖重置不同，不會傳送回起點、不會重置導航進度，只修正姿態")]
    public bool autoRightWhenTipped = true;

    [Tooltip("車身「上方向」跟世界垂直方向的夾角超過這個角度，視為翻覆/嚴重側傾")]
    public float tipAngleThreshold = 60f;

    [Tooltip("翻覆狀態要持續幾秒才觸發自動扶正，避免過彎時的小幅晃動被誤判")]
    public float tipRecoverDelay = 1.5f;

    [Tooltip("扶正時順便把車身墊高多少公尺，避免扶正瞬間卡進地面或路緣")]
    public float uprightLiftHeight = 1f;

    private float tippedTimer = 0f;

    private Rigidbody rb;
    private Vector3 startPosition;
    private Quaternion startRotation;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        // 記錄遊戲一開始的位置/朝向，之後重置都會回到這裡
        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    void Update()
    {
        if (Input.GetKeyDown(resetKey))
        {
            ResetVehicle();
        }

        if (autoResetWhenFallen && transform.position.y < fallResetHeight)
        {
            ResetVehicle();
        }

        UpdateTipRecovery();
    }

    /// <summary>
    /// 甩尾、碰撞等情況偶爾會讓車輛翻覆側躺（即使有 Freeze Rotation，強烈碰撞衝量下
    /// 物理引擎的約束求解還是有機率來不及在單一步驟內收斂），翻覆後車輛卡在原地、
    /// 導航沒辦法繼續，體驗上比「開得不夠像真車」更糟。這裡持續偵測傾斜角度，
    /// 超過門檻夠久就自動原地扶正，不需要玩家自己按重置鍵。
    /// </summary>
    private void UpdateTipRecovery()
    {
        if (!autoRightWhenTipped) return;

        float tiltAngle = Vector3.Angle(transform.up, Vector3.up);
        if (tiltAngle > tipAngleThreshold)
        {
            tippedTimer += Time.deltaTime;
            if (tippedTimer >= tipRecoverDelay)
            {
                RightVehicleInPlace();
                tippedTimer = 0f;
            }
        }
        else
        {
            tippedTimer = 0f;
        }
    }

    /// <summary>
    /// 車身翻覆時「原地扶正」：只修正姿態跟速度，留在原本的 X/Z 位置——跟 ResetVehicle()
    /// 不同，不會傳送回起點、不會動到導航進度，只是把車身重新轉正、稍微墊高避免卡地。
    /// </summary>
    public void RightVehicleInPlace()
    {
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        Vector3 uprightEuler = new Vector3(0f, transform.eulerAngles.y, 0f);
        Vector3 liftedPosition = transform.position + Vector3.up * uprightLiftHeight;
        transform.SetPositionAndRotation(liftedPosition, Quaternion.Euler(uprightEuler));
    }

    /// <summary>
    /// 把車輛傳回起點，並清空物理速度，避免傳送後還帶著原本的衝力亂飛。
    /// 也可以直接綁在 UI 按鈕的 OnClick() 上呼叫。
    /// </summary>
    public void ResetVehicle()
    {
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.SetPositionAndRotation(startPosition, startRotation);

        // 車輛被傳回起點了，導航進度（已經走過哪個節點）也要跟著歸零，避免車跟導航線對不上
        if (lineManager != null && !lineManager.IsDestinationReached)
        {
            lineManager.ResetProgress();
        }
    }
}
