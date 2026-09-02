using UnityEngine;

namespace Navigation
{
    /// <summary>攝影機視角模式：第三人稱（跟車後上方）或第一人稱（車內駕駛視角）。</summary>
    public enum CameraViewMode
    {
        ThirdPerson,
        FirstPerson
    }

    /// <summary>
    /// Google Maps 風格的跟隨攝影機，支援第三人稱／第一人稱切換。
    /// 第三人稱：平滑跟隨並保持在載具「正後方 + 固定俯視角」，模擬 Google Maps 3D 導航視角。
    /// 第一人稱：攝影機直接放在駕駛座視角高度，朝車頭方向看出去。
    /// </summary>
    [DisallowMultipleComponent]
    public class GoogleMapCamera : MonoBehaviour
    {
        [Header("跟隨目標")]
        [Tooltip("要跟隨的載具 Transform（拖入車輛/機車的根節點）")]
        [SerializeField] private Transform target;

        [Header("視角模式")]
        [Tooltip("按下這個按鍵可以切換第三人稱／第一人稱視角")]
        [SerializeField] private KeyCode toggleViewModeKey = KeyCode.C;

        [SerializeField] private CameraViewMode viewMode = CameraViewMode.ThirdPerson;

        [Header("第三人稱：攝影機位置偏移 (相對於載具)")]
        [Tooltip("攝影機相對載具的高度（公尺）")]
        [SerializeField] private float heightOffset = 8f;

        [Tooltip("攝影機相對載具的後退距離（公尺）")]
        [SerializeField] private float distanceOffset = 12f;

        [Header("第三人稱：攝影機角度")]
        [Tooltip("固定俯視角（Pitch），數值越大攝影機越往下看。Google Maps 導航模式通常介於 35~55 度")]
        [SerializeField] private float pitchAngle = 45f;

        [Header("第一人稱：駕駛座視角偏移 (相對於載具)")]
        [Tooltip("第一人稱攝影機相對載具原點的高度（公尺，約為駕駛座視線高度）")]
        [SerializeField] private float firstPersonHeightOffset = 1.2f;

        [Tooltip("第一人稱攝影機相對載具原點的前後偏移（公尺，正值往車頭方向）")]
        [SerializeField] private float firstPersonForwardOffset = 0.2f;

        [Tooltip("第一人稱固定俯仰角，通常接近 0（平視前方）")]
        [SerializeField] private float firstPersonPitchAngle = 2f;

        [Header("平滑參數")]
        [Tooltip("位置平滑時間，數值越小跟隨越緊（SmoothDamp 用）")]
        [SerializeField] private float positionSmoothTime = 0.15f;

        [Tooltip("旋轉平滑速度，數值越大轉向跟隨越快（Slerp 用）")]
        [SerializeField] private float rotationSmoothSpeed = 5f;

        [Header("自由環顧視角（按住滑鼠右鍵）")]
        [Tooltip("是否允許按住滑鼠右鍵環顧四周（放開後自動歸位回車後方）")]
        [SerializeField] private bool enableFreeLook = true;

        [Tooltip("滑鼠環顧視角的靈敏度")]
        [SerializeField] private float lookSensitivity = 3f;

        [Tooltip("環顧視角時，上下最多可以看多少度")]
        [SerializeField] private float maxLookPitch = 75f;

        [Tooltip("放開右鍵後，視角歸位回車後方的速度")]
        [SerializeField] private float lookResetSpeed = 5f;

        [Header("避免穿進地形或建築")]
        [Tooltip("是否檢查攝影機與載具之間有沒有障礙物，有的話把攝影機往前拉到障礙物前面")]
        [SerializeField] private bool avoidObstacles = true;

        [Tooltip("攝影機與障礙物之間至少保留的間距（公尺）")]
        [SerializeField] private float obstacleClearance = 1.2f;

        [Tooltip("碰撞檢查用的球體半徑（公尺）。比近裁切面大一點，避免貼著牆面時看穿")]
        [SerializeField] private float obstacleProbeRadius = 0.8f;

        // SmoothDamp 需要的內部速度暫存變數
        private Vector3 _currentVelocity;

        // 目前的自由環顧視角偏移（相對於預設的跟車視角）
        private float _lookYawOffset;
        private float _lookPitchOffset;

        private void Reset()
        {
            // 方便在 Inspector 掛上元件時自動帶入常用預設值
            heightOffset = 8f;
            distanceOffset = 12f;
            pitchAngle = 45f;
            positionSmoothTime = 0.15f;
            rotationSmoothSpeed = 5f;
        }

        private void OnValidate()
        {
            // 避免在 Inspector 誤填負值或極端值造成攝影機穿模
            heightOffset = Mathf.Max(0.1f, heightOffset);
            distanceOffset = Mathf.Max(0.1f, distanceOffset);
            pitchAngle = Mathf.Clamp(pitchAngle, 0f, 89f);
            positionSmoothTime = Mathf.Max(0.01f, positionSmoothTime);
            rotationSmoothSpeed = Mathf.Max(0.01f, rotationSmoothSpeed);
        }

        /// <summary>
        /// 使用 LateUpdate 是因為要確保載具（含物理/動畫）已完成本影格的移動後，
        /// 攝影機才根據最新位置做跟隨，避免畫面抖動或延遲一影格。
        /// </summary>
        private void LateUpdate()
        {
            if (target == null)
            {
                Debug.LogWarning("[GoogleMapCamera] 尚未指定 Target（跟隨目標），請在 Inspector 拖入載具 Transform。");
                return;
            }

            if (Input.GetKeyDown(toggleViewModeKey))
            {
                viewMode = viewMode == CameraViewMode.ThirdPerson ? CameraViewMode.FirstPerson : CameraViewMode.ThirdPerson;
            }

            UpdateFreeLookOffset();

            if (viewMode == CameraViewMode.FirstPerson)
            {
                UpdateFirstPersonView();
            }
            else
            {
                UpdateCameraPosition();
                UpdateCameraRotation();
            }
        }

        /// <summary>
        /// 第一人稱模式：攝影機直接放在駕駛座視角高度（不做位置平滑，緊貼車身，
        /// 避免車身跟攝影機之間的延遲造成穿模或暈車感），朝車頭方向看出去，
        /// 一樣支援按住滑鼠右鍵環顧四周。
        /// </summary>
        private void UpdateFirstPersonView()
        {
            Vector3 desiredPosition = target.position
                                       + target.forward * firstPersonForwardOffset
                                       + Vector3.up * firstPersonHeightOffset;
            transform.position = desiredPosition;

            Vector3 flatForward = GetFlatForward();
            Quaternion yawRotation = Quaternion.LookRotation(flatForward, Vector3.up);
            Quaternion desiredRotation = yawRotation * Quaternion.Euler(firstPersonPitchAngle, 0f, 0f);

            Quaternion lookOffset = Quaternion.Euler(_lookPitchOffset, _lookYawOffset, 0f);
            desiredRotation *= lookOffset;

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                desiredRotation,
                rotationSmoothSpeed * Time.deltaTime);
        }

        /// <summary>
        /// 處理「按住滑鼠右鍵環顧四周」的輸入：按住時滑鼠移動會累積視角偏移，
        /// 放開右鍵後偏移會平滑地衰減回 0（自動歸位回車後方的預設跟隨視角）。
        /// </summary>
        private void UpdateFreeLookOffset()
        {
            if (!enableFreeLook)
            {
                return;
            }

            if (Input.GetMouseButton(1))
            {
                _lookYawOffset += Input.GetAxis("Mouse X") * lookSensitivity;
                _lookPitchOffset -= Input.GetAxis("Mouse Y") * lookSensitivity;
                _lookPitchOffset = Mathf.Clamp(_lookPitchOffset, -maxLookPitch, maxLookPitch);
            }
            else
            {
                _lookYawOffset = Mathf.Lerp(_lookYawOffset, 0f, lookResetSpeed * Time.deltaTime);
                _lookPitchOffset = Mathf.Lerp(_lookPitchOffset, 0f, lookResetSpeed * Time.deltaTime);
            }
        }

        /// <summary>
        /// 計算並平滑移動攝影機到「載具正後方、固定高度」的位置。
        /// 這裡刻意使用「攤平後的載具前方向量」而非 target.forward 本身，
        /// 是為了避免載具在上下坡時 forward 向量帶有俯仰角，導致攝影機跟著上下浮動（暈車感）。
        /// </summary>
        private void UpdateCameraPosition()
        {
            Vector3 flatForward = GetFlatForward();

            Vector3 desiredPosition = target.position
                                       - flatForward * distanceOffset
                                       + Vector3.up * heightOffset;

            if (avoidObstacles)
            {
                desiredPosition = PullInFrontOfObstacles(desiredPosition);
            }

            transform.position = Vector3.SmoothDamp(
                transform.position,
                desiredPosition,
                ref _currentVelocity,
                positionSmoothTime);
        }

        /// <summary>
        /// 從載具往預定的攝影機位置做球體投射，中途撞到東西就把攝影機拉到障礙物前面。
        ///
        /// 沒有這一步的話，攝影機會直接穿進地形或建築裡——木柵路沿著山壁走，
        /// 攝影機在車後 32 公尺、上方 27 公尺的位置經常正好落在山壁內部，
        /// 畫面就變成貼著一片糊掉的材質，什麼都看不到。
        /// </summary>
        private Vector3 PullInFrontOfObstacles(Vector3 desiredPosition)
        {
            // 從載具上方一點起算，避免球體一開始就卡在車身或路面裡。
            Vector3 origin = target.position + Vector3.up * 1.5f;
            Vector3 direction = desiredPosition - origin;
            float distance = direction.magnitude;

            if (distance < 0.01f)
            {
                return desiredPosition;
            }

            direction /= distance;

            if (!Physics.SphereCast(origin, obstacleProbeRadius, direction,
                    out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                return desiredPosition;
            }

            // 撞到載具自己就不算。
            if (hit.transform.IsChildOf(target))
            {
                return desiredPosition;
            }

            float allowed = Mathf.Max(0.5f, hit.distance - obstacleClearance);
            return origin + direction * allowed;
        }

        /// <summary>
        /// 計算並平滑旋轉攝影機朝向，維持「跟隨載具航向（Yaw）+ 固定俯視角（Pitch）」。
        /// </summary>
        private void UpdateCameraRotation()
        {
            Vector3 flatForward = GetFlatForward();

            // 先算出對齊載具水平朝向的旋轉，再疊加固定俯角
            Quaternion yawRotation = Quaternion.LookRotation(flatForward, Vector3.up);
            Quaternion desiredRotation = yawRotation * Quaternion.Euler(pitchAngle, 0f, 0f);

            // 疊加自由環顧視角的偏移（在跟車視角的基礎上左右/上下轉頭），
            // 攝影機位置仍固定在車後方，只有「看的方向」會改變，方便瀏覽周邊路景。
            Quaternion lookOffset = Quaternion.Euler(_lookPitchOffset, _lookYawOffset, 0f);
            desiredRotation *= lookOffset;

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                desiredRotation,
                rotationSmoothSpeed * Time.deltaTime);
        }

        /// <summary>
        /// 取得載具「攤平在水平面上」的前方向量（忽略上下坡造成的俯仰）。
        /// </summary>
        private Vector3 GetFlatForward()
        {
            Vector3 forward = Vector3.ProjectOnPlane(target.forward, Vector3.up);

            // 當載具前方向量幾乎垂直於地面（極端情況）時，改用上一影格的攝影機朝向避免除零
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            }

            // 保險：如果連攝影機自己的朝向投影後也幾乎是零向量（例如剛啟動、或極端邊界情況），
            // 一律退回世界座標的正前方，確保絕對不會回傳退化的零向量給 Quaternion.LookRotation，
            // 避免攝影機出現顛倒或未定義的旋轉結果。
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            return forward.normalized;
        }

        /// <summary>
        /// 允許在執行期間（例如切換載具）動態更換跟隨目標。
        /// </summary>
        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
        }

        /// <summary>
        /// 執行期間套用一組第三人稱跟車參數（MapStyleSwitcher 切換地圖風格時會呼叫）。
        /// 兩種圖資適合的觀看距離差很多：照片級圖磚是空拍攝影測量資料，貼太近會看到糊掉的
        /// 材質，要拉高拉遠；簡化建築的幾何乾淨，可以貼近看。這裡沿用 OnValidate 的同一組
        /// 夾限規則，避免外部傳進不合理的值造成攝影機穿模。
        /// </summary>
        public void ApplyThirdPersonPreset(float height, float distance, float pitch)
        {
            heightOffset = Mathf.Max(0.1f, height);
            distanceOffset = Mathf.Max(0.1f, distance);
            pitchAngle = Mathf.Clamp(pitch, 0f, 89f);
        }
    }
}
