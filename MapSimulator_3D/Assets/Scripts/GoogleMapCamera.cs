using UnityEngine;

/// <summary>
/// Google 地圖風格的第三人稱跟隨攝影機。
/// 掛載在 Main Camera 上，平滑跟隨載具並維持固定俯視角度 (Pitch)。
/// 玩家可用滑鼠右鍵拖曳暫時繞著載具轉動視角，放開一段時間後會自動回到預設跟隨角度。
/// </summary>
public class GoogleMapCamera : MonoBehaviour
{
    [Header("跟隨目標")]
    public Transform target; // 要跟隨的載具 (車輛) Transform

    [Header("跟隨位置設定")]
    [Tooltip("相對於載具的預設偏移量：x = 左右, y = 高度, z = 前後 (負值代表在載具後方)")]
    public Vector3 offset = new Vector3(0f, 8f, -12f); // 預設：高 8 米、後退 12 米

    [Tooltip("固定的俯視角度 (Pitch)，數值越大攝影機看得越往下")]
    public float pitchAngle = 35f;

    [Header("平滑參數")]
    [Tooltip("位置平滑時間，數值越小跟隨越緊 (SmoothDamp 用)")]
    public float positionSmoothTime = 0.25f;

    [Tooltip("旋轉平滑速度，數值越大轉向跟隨越快")]
    public float rotationSmoothSpeed = 6f;

    [Header("手動旋轉攝影機 (滑鼠右鍵拖曳)")]
    [Tooltip("水平/垂直旋轉靈敏度")]
    public float manualRotateSensitivity = 3f;

    [Tooltip("垂直方向可額外調整的俯仰角度範圍 (在 pitchAngle 基礎上加減)")]
    public float manualPitchRange = 40f;

    [Tooltip("放開滑鼠後，經過幾秒沒有操作就開始回正")]
    public float returnToDefaultDelay = 1.5f;

    [Tooltip("回正時的旋轉速度")]
    public float returnRotationSpeed = 3f;

    // 目前的手動偏移量 (使用者拖曳造成的額外角度)
    private float manualYawOffset = 0f;
    private float manualPitchOffset = 0f;

    // 記錄最後一次手動操作的時間，用來判斷是否該回正
    private float lastManualInputTime = -999f;

    // SmoothDamp 用的內部速度暫存
    private Vector3 currentVelocity;

    void LateUpdate()
    {
        if (target == null) return;

        HandleManualRotationInput();

        // 目標載具目前面朝的水平角度 (Yaw)
        float vehicleYaw = target.eulerAngles.y;

        // 最終攝影機的水平角度 = 載具角度 + 玩家手動旋轉的額外角度
        float desiredYaw = vehicleYaw + manualYawOffset;

        // 最終俯仰角 = 預設俯視角度 + 玩家手動調整的額外角度
        float desiredPitch = pitchAngle + manualPitchOffset;

        // 用水平角度旋轉「後上方偏移量」，讓攝影機永遠位於載具正後方 (不含手動 pitch，避免位置跑掉)
        Quaternion yawRotation = Quaternion.Euler(0f, desiredYaw, 0f);
        Vector3 desiredPosition = target.position + yawRotation * new Vector3(offset.x, offset.y, offset.z);

        // 位置使用 SmoothDamp，移動感更像 Google 地圖導航鏡頭的「跟隨慣性」
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref currentVelocity, positionSmoothTime);

        // 旋轉使用 Slerp 平滑趨近目標角度
        Quaternion desiredRotation = Quaternion.Euler(desiredPitch, desiredYaw, 0f);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationSmoothSpeed * Time.deltaTime);
    }

    /// <summary>
    /// 處理玩家按住滑鼠右鍵拖曳，暫時脫離預設跟隨視角的操作。
    /// 放開後若超過 returnToDefaultDelay 秒沒有再次操作，manualYawOffset / manualPitchOffset 會自動回到 0。
    /// </summary>
    private void HandleManualRotationInput()
    {
        // 按住滑鼠右鍵時，允許玩家自由環繞載具查看
        if (Input.GetMouseButton(1))
        {
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            manualYawOffset += mouseX * manualRotateSensitivity;
            manualPitchOffset -= mouseY * manualRotateSensitivity;
            manualPitchOffset = Mathf.Clamp(manualPitchOffset, -manualPitchRange, manualPitchRange);

            lastManualInputTime = Time.time;
        }
        else
        {
            // 沒有操作超過設定時間後，才開始緩慢回正到預設跟隨角度
            if (Time.time - lastManualInputTime > returnToDefaultDelay)
            {
                manualYawOffset = Mathf.LerpAngle(manualYawOffset, 0f, returnRotationSpeed * Time.deltaTime);
                manualPitchOffset = Mathf.Lerp(manualPitchOffset, 0f, returnRotationSpeed * Time.deltaTime);
            }
        }
    }
}
