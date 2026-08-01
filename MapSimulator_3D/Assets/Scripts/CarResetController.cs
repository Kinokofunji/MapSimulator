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
