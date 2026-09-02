using UnityEngine;

namespace Navigation
{
    /// <summary>
    /// 具備加速、煞車、轉向、碰撞的擬真車輛控制器，使用 Unity 內建的 WheelCollider 物理系統實現。
    /// 對應畢業專題報告中「擬真車輛駕駛」的核心需求：透過 WASD / 方向鍵操控，
    /// 讓車輛具有真實的懸吊、輪胎抓地、慣性與碰撞回饋，而不是單純搬移座標。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VehiclePhysicsController : MonoBehaviour
    {
        [Header("車輪 WheelCollider 參照（物理用，必填）")]
        [SerializeField] private WheelCollider frontLeftWheel;
        [SerializeField] private WheelCollider frontRightWheel;
        [SerializeField] private WheelCollider rearLeftWheel;
        [SerializeField] private WheelCollider rearRightWheel;

        [Header("車輪視覺模型（選填，若有指定會同步位置與轉向角度）")]
        [SerializeField] private Transform frontLeftMesh;
        [SerializeField] private Transform frontRightMesh;
        [SerializeField] private Transform rearLeftMesh;
        [SerializeField] private Transform rearRightMesh;

        [Header("動力參數")]
        [Tooltip("最大馬達扭力，數值越大加速越快")]
        [SerializeField] private float maxMotorTorque = 1500f;

        [Tooltip("最大轉向角度（度）")]
        [SerializeField] private float maxSteerAngle = 30f;

        [Tooltip("按下煞車鍵（空白鍵）時套用的煞車扭力")]
        [SerializeField] private float brakeTorque = 3000f;

        [Tooltip("是否為四輪驅動（取消勾選則僅後輪驅動）")]
        [SerializeField] private bool allWheelDrive = false;

        [Header("操控手感")]
        [Tooltip("車速達到這個值時，可用的轉向角會被壓到最小值（km/h）")]
        [SerializeField] private float steerReductionSpeed = 65f;

        [Tooltip("高速時的最小轉向角（度）。真實車輛的方向盤在高速時等效轉向角本來就很小")]
        [SerializeField] private float minSteerAngle = 7f;

        [Tooltip("轉向角每秒最多變化幾度。數值越小方向盤越「重」、越好控，越大越靈敏")]
        [SerializeField] private float steerSmoothSpeed = 90f;

        [Tooltip("最高時速（km/h）。超過就停止供給馬達扭力，避免車速無上限地一直往上衝")]
        [SerializeField] private float maxSpeedKph = 65f;

        [Tooltip("放開油門時自動施加的引擎煞車扭力，讓車子會自己緩緩減速而不是一直滑行")]
        [SerializeField] private float engineBrakeTorque = 350f;

        [Header("防打轉（穩定輔助）")]
        [Tooltip("允許的最大偏航角速度（度/秒）。超過就施加反向力矩把車拉回來")]
        [SerializeField] private float maxYawRateDegrees = 90f;

        [Tooltip("超速旋轉時的修正力道。太小擋不住打轉，太大轉向會變得遲鈍")]
        [SerializeField] private float yawDampStrength = 4f;

        [Tooltip("側滑角超過這個值就額外加強修正（度）")]
        [SerializeField] private float sideSlipLimitDegrees = 25f;

        [Tooltip("質心相對車輛原點的位置。車輪在 y = -0.5、輪胎半徑 0.35，所以地面在 y = -0.85。" +
                 "真實轎車的質心大約在離地 0.5 公尺處，換算過來是 y = -0.35")]
        [SerializeField] private Vector3 centerOfMassOffset = new Vector3(0f, -0.35f, 0f);

        private Rigidbody _rigidbody;
        private float _currentSteerAngle;

        /// <summary>
        /// 外部注入的操控輸入（油門、轉向、煞車），以及最後一次注入的時間。
        ///
        /// 存在的理由是自動化測試無法模擬鍵盤：手動駕駛讀的是 Input.GetAxis，
        /// 沒有這條路徑就永遠測不到真正的操控手感——實測結果是那些轉向、抓地、
        /// 懸吊的調校從頭到尾沒被驗證過，因為所有測試都跑在自動駕駛模式。
        /// 這個路徑也可以給未來的手機虛擬搖桿或 UI 按鈕使用。
        /// </summary>
        private Vector3 _externalInput;
        private float _externalInputTime = float.NegativeInfinity;

        /// <summary>外部輸入的有效時間。超過就自動退回鍵盤，避免測試結束後控制權被卡住。</summary>
        private const float ExternalInputTimeout = 0.5f;

        /// <summary>
        /// 注入一次操控輸入。x = 油門（-1~1）、y = 轉向（-1~1）、z = 煞車（>0.5 視為踩下）。
        /// 用 Vector3 是為了能透過 SendMessage 呼叫（測試組件無法參照 Assembly-CSharp）。
        /// </summary>
        public void SetExternalDriveInput(Vector3 input)
        {
            _externalInput = input;
            _externalInputTime = Time.time;
        }

        private bool HasFreshExternalInput => Time.time - _externalInputTime < ExternalInputTimeout;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();

            // 質心位置。原本硬寫成 y = -0.5，那正好落在輪軸高度上（車輪在 y = -0.5），
            // 等於質心跟輪軸同高——物理上不合理，過彎時的載重轉移會失真。
            // 改成可設定，預設 -0.35 對應「離地約 0.5 公尺」的真實轎車配置。
            _rigidbody.centerOfMass = centerOfMassOffset;
        }

        /// <summary>物理運算統一在 FixedUpdate 處理，確保與 Unity 物理引擎的更新頻率同步。</summary>
        private void FixedUpdate()
        {
            float verticalInput;
            float horizontalInput;
            bool isBraking;

            if (HasFreshExternalInput)
            {
                verticalInput = _externalInput.x;
                horizontalInput = _externalInput.y;
                isBraking = _externalInput.z > 0.5f;
            }
            else
            {
                verticalInput = Input.GetAxis("Vertical");
                horizontalInput = Input.GetAxis("Horizontal");
                isBraking = Input.GetKey(KeyCode.Space);
            }

            ApplySteering(horizontalInput);
            ApplyMotorAndBrake(verticalInput, isBraking);
            ApplySpinControl();
            UpdateAllWheelVisuals();
        }

        /// <summary>目前車速（km/h）。</summary>
        private float CurrentSpeedKph => _rigidbody != null ? _rigidbody.velocity.magnitude * 3.6f : 0f;

        /// <summary>
        /// 轉向做了兩件事讓車子好開：
        ///
        ///  1. 速度感應轉向：車速越高，可用的最大轉向角越小。原本無論時速 5 還是 60 都是
        ///     固定 30 度，高速時輕輕一撥就是一個大角度，車尾馬上甩出去——這是「很難控制」
        ///     最主要的原因。真實車輛因為轉向齒比與輪胎特性，高速時的等效轉向角本來就小得多。
        ///  2. 轉向平滑：方向盤不會瞬間打到底，而是以固定角速度轉過去。鍵盤輸入是 0 或 1 的
        ///     方波，沒有平滑的話等於每一格都在猛打方向盤。
        /// </summary>
        private void ApplySteering(float horizontalInput)
        {
            float speedFactor = Mathf.Clamp01(CurrentSpeedKph / Mathf.Max(1f, steerReductionSpeed));
            float allowedSteerAngle = Mathf.Lerp(maxSteerAngle, minSteerAngle, speedFactor);

            float targetSteerAngle = allowedSteerAngle * horizontalInput;
            _currentSteerAngle = Mathf.MoveTowards(
                _currentSteerAngle, targetSteerAngle, steerSmoothSpeed * Time.fixedDeltaTime);

            frontLeftWheel.steerAngle = _currentSteerAngle;
            frontRightWheel.steerAngle = _currentSteerAngle;
        }

        private void ApplyMotorAndBrake(float verticalInput, bool isBraking)
        {
            bool hasThrottleInput = Mathf.Abs(verticalInput) > 0.01f;

            // 到達極速就不再供給動力。原本 Rigidbody 的 drag 只有 0.05，等於沒有空氣阻力，
            // 只要一直按著油門車速就會無上限地往上疊，最後變成完全不可能轉彎的失控狀態。
            bool underSpeedLimit = CurrentSpeedKph < maxSpeedKph || verticalInput < 0f;
            float motorTorque = (!isBraking && hasThrottleInput && underSpeedLimit)
                ? maxMotorTorque * verticalInput
                : 0f;

            rearLeftWheel.motorTorque = motorTorque;
            rearRightWheel.motorTorque = motorTorque;

            if (allWheelDrive)
            {
                frontLeftWheel.motorTorque = motorTorque;
                frontRightWheel.motorTorque = motorTorque;
            }

            // 放開油門時給一點引擎煞車，車子會自己慢慢減速。沒有這個的話滑行距離長得離譜，
            // 到路口前得提早很久就放開油門，開起來完全沒有預期感。
            float appliedBrakeTorque = isBraking
                ? brakeTorque
                : (hasThrottleInput ? 0f : engineBrakeTorque);

            frontLeftWheel.brakeTorque = appliedBrakeTorque;
            frontRightWheel.brakeTorque = appliedBrakeTorque;
            rearLeftWheel.brakeTorque = appliedBrakeTorque;
            rearRightWheel.brakeTorque = appliedBrakeTorque;
        }

        /// <summary>
        /// 防打轉輔助。
        ///
        /// 為什麼需要它：這是後輪驅動 + 1200 公斤 + 全油門的組合，只要路面夠平能跑到
        /// 40 km/h，滿舵就會動力過度轉向直接甩出去——實測偏航角速度飆到 475 度/秒、
        /// 側滑角 179 度（整台車倒著滑）。單純加大輪胎抓地力治不好，因為問題出在
        /// 後輪的驅動力已經超過它的側向抓地上限。
        ///
        /// 真實車輛靠 ESC（電子穩定系統）處理，做法是偵測「車身轉太快」或「車頭方向與
        /// 實際行進方向差太多」，然後施加反向力矩。這裡用同樣的原理，是導航展示需要的
        /// 可控性，不是賽車遊戲的擬真度。
        /// </summary>
        private void ApplySpinControl()
        {
            if (_rigidbody == null || _rigidbody.isKinematic)
            {
                return;
            }

            float yawRate = _rigidbody.angularVelocity.y * Mathf.Rad2Deg;
            float excess = Mathf.Abs(yawRate) - maxYawRateDegrees;

            // 側滑角：車頭朝向與實際行進方向的夾角，超過門檻代表車已經在滑了。
            Vector3 velocity = _rigidbody.velocity;
            velocity.y = 0f;

            float sideSlip = 0f;
            if (velocity.magnitude > 2f)
            {
                Vector3 forward = transform.forward;
                forward.y = 0f;
                sideSlip = Vector3.Angle(forward, velocity);
            }

            if (excess <= 0f && sideSlip <= sideSlipLimitDegrees)
            {
                return;
            }

            // 兩個條件各自貢獻修正力道，取較大者。
            float yawFactor = Mathf.Max(0f, excess) / Mathf.Max(1f, maxYawRateDegrees);
            float slipFactor = Mathf.Max(0f, sideSlip - sideSlipLimitDegrees) / 45f;
            float correction = Mathf.Clamp01(Mathf.Max(yawFactor, slipFactor)) * yawDampStrength;

            _rigidbody.AddTorque(Vector3.up * (-yawRate * Mathf.Deg2Rad * correction),
                ForceMode.Acceleration);
        }

        private void UpdateAllWheelVisuals()
        {
            UpdateWheelVisual(frontLeftWheel, frontLeftMesh);
            UpdateWheelVisual(frontRightWheel, frontRightMesh);
            UpdateWheelVisual(rearLeftWheel, rearLeftMesh);
            UpdateWheelVisual(rearRightWheel, rearRightMesh);
        }

        /// <summary>把 WheelCollider 目前實際的物理姿態（含懸吊起伏、轉向角）同步到對應的視覺輪胎模型。</summary>
        private static void UpdateWheelVisual(WheelCollider wheelCollider, Transform visual)
        {
            if (visual == null)
            {
                return;
            }

            wheelCollider.GetWorldPose(out Vector3 position, out Quaternion rotation);
            visual.position = position;
            visual.rotation = rotation;
        }
    }
}
