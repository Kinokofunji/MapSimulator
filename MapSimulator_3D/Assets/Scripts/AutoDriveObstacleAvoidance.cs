using UnityEngine;

/// <summary>
/// 自動駕駛模式下的避障。Navigation.AutoDriveController 只會直直朝路口目標點前進、
/// 轉向，完全不知道路上有沒有障礙物；車輛在自動駕駛下又是 Kinematic（她直接搬
/// Transform，物理碰撞完全擋不住），純粹疊加側向位移只會讓車頭朝向不變、貼著障礙物
/// 邊緣蹭過去，不是真的「轉彎繞開」。
///
/// 做法改成真正接管轉向：平常完全不動 AutoDriveController，讓它正常運作；一偵測到
/// 正前方有障礙物，就暫時關掉它的 enabled，改由這裡自己接手，一次性算好一個固定的
/// 閃避目標點（往旁邊閃開 + 往車頭方向前面一點），全程朝這個固定點「轉向 + 往車頭
/// 方向前進」（跟她原本的移動邏輯是同一套模式，只是目標點換成閃避點），模擬真實車輛
/// 會左右轉、前進閃避的樣子；到達閃避目標點（或超過最長避障時間）後，才把控制權還給
/// AutoDriveController，讓它從車輛目前的位置/朝向重新對準路線繼續走。
///
/// 另外保留一層「反應式」保底：用車身自己的 Box Collider 每一幀檢查有沒有真的跟什麼
/// 東西重疊，重疊就用 Physics.ComputePenetration 算出的方向強制推開——但只處理明顯
/// 偏水平方向的推擠（真正的側向障礙物），忽略偏垂直方向的推擠（那多半是車身跟路面
/// 網格重疊，方向理論上該是垂直向上，但路面是很多三角形拼成的，量到不同三角形邊緣時
/// 方向會帶一點水平雜訊，之前就是這個雜訊在無障礙平面上也會被誤判成要閃避，
/// 造成規律的左右抖動）。
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class AutoDriveObstacleAvoidance : MonoBehaviour
{
    public Navigation.AutoDriveController autoDriveController;

    [Tooltip("有指定的話，閃避方向會優先挑選離真實道路網格比較近的那一側，避免車輛" +
             "轉向閃避時開上人行道、草地或建築物之間的空地；沒有指定就退回原本" +
             "「障礙物在哪一側就往另一側閃」的簡單判斷")]
    public RoadGridPathfinder pathfinder;

    [Tooltip("候選閃避點離最近道路格子的距離要在這個範圍內，才視為「落在道路上」")]
    public float roadProximityThreshold = 12f;

    [Header("偵測")]
    [Tooltip("往正前方偵測障礙物的距離（公尺）")]
    public float detectionDistance = 8f;

    [Tooltip("偵測用球體的半徑（公尺），約等於車寬的一半再留一點餘裕")]
    public float detectionRadius = 0.8f;

    [Tooltip("偵測球心的高度（相對車身原點，公尺）。車輛原點到路面大約 0.2~0.3 公尺，" +
             "球體半徑再加上這個高度，底部務必要留在路面上方，不然球體會整顆貼在路面上，" +
             "每一幀都把路面本身誤判成正前方的障礙物")]
    public float detectionHeight = 1f;

    [Tooltip("偵測到的表面法向量跟正上方的夾角小於這個角度，視為地面/路面，直接忽略")]
    public float groundNormalAngleThreshold = 30f;

    [Header("避障時的行駛參數")]
    [Tooltip("避障時的前進速度（公尺/秒），建議跟 AutoDriveController 的 Move Speed 對齊")]
    public float avoidMoveSpeed = 8f;

    [Tooltip("避障時的轉向速度（度/秒）")]
    public float avoidTurnSpeed = 120f;

    [Tooltip("閃避目標點抓多遠：往旁邊閃的距離基準（會再加上偵測半徑），以及往車頭方向多看多遠")]
    public float avoidLateralClearance = 3.5f;

    [Tooltip("閃避目標點往車頭方向延伸多少公尺")]
    public float avoidForwardDistance = 5f;

    [Tooltip("車身跟閃避目標點的水平距離小於這個值，視為已經到達，才重新交還控制權")]
    public float avoidArrivalDistance = 1.5f;

    [Tooltip("避障最多維持幾秒的下限——實際上限會依閃避目標點的距離動態計算（距離/速度*安全係數），" +
             "這裡只是避免距離太短時算出離譜的短時間；超過就強制交還控制權，避免因為任何意外情況卡在避障狀態出不來")]
    public float minAvoidSeconds = 5f;

    [Tooltip("動態計算避障時間時的安全係數：抵達目標理論所需時間的幾倍，當作允許的上限")]
    public float avoidTimeoutSafetyFactor = 2.5f;

    [Header("卡住時倒車")]
    [Tooltip("偵測到的障礙物距離小於這個值，代表已經卡住（轉向也閃不掉，例如兩側都貼著" +
             "建築物的窄縫），這時不嘗試轉向繞過去，改成先倒車拉開距離")]
    public float stuckDistanceThreshold = 1.5f;

    [Tooltip("倒車速度（公尺/秒）")]
    public float reverseSpeed = 5f;

    [Tooltip("每次倒車持續幾秒，時間到了會重新偵測前方，決定要繼續倒車還是恢復正常避障")]
    public float reverseDuration = 1.5f;

    [Tooltip("連續倒車最多維持幾秒（可能倒好幾次），超過就強制交還控制權，避免無止盡倒車")]
    public float maxTotalReverseSeconds = 6f;

    [Header("反應式重疊保底")]
    [Tooltip("Physics.ComputePenetration 算出的推開方向，跟水平面的夾角要小於這個角度" +
             "（越接近 0 代表越水平）才會採用；接近垂直的推擠視為貼地，忽略")]
    public float maxPushAngleFromHorizontal = 60f;

    private BoxCollider _bodyCollider;
    private Rigidbody _rigidbody;

    private bool _isAvoiding;
    private Vector3 _avoidTargetPoint;
    private float _avoidElapsed;
    private float _avoidTimeoutSeconds;
    private Transform _currentObstacle; // 目前這次避障是為了躲哪一個物件

    private bool _isReversing;
    private float _reverseElapsed;
    private float _totalReverseElapsed; // 連續倒車（可能好幾次）累計的總時間，防止無止盡倒車

    void Awake()
    {
        _bodyCollider = GetComponent<BoxCollider>();
        _rigidbody = GetComponent<Rigidbody>();
    }

    void Update()
    {
        if (autoDriveController == null)
        {
            return;
        }

        // Rigidbody 是不是 Kinematic，是 DriveModeSwitcher 唯一會去改的旗標，能可靠
        // 反映玩家目前實際選的是手動還是自動駕駛——不能只看 autoDriveController.enabled，
        // 因為這裡自己在避障時也會暫時把它關掉，兩種「關閉」原因不一樣。玩家按 Tab
        // 切回手動時，如果沒有這道檢查，_isAvoiding 不會自動清除，會繼續搶著改
        // Transform，跟玩家的 WASD 手動操控打架，感覺像是「切不出自動駕駛」。
        if (_rigidbody != null && !_rigidbody.isKinematic)
        {
            if (_isAvoiding || _isReversing)
            {
                Debug.Log("[AutoDriveObstacleAvoidance] 偵測到已切換成手動模式，中止避障、交還控制權。");
            }
            _isAvoiding = false;
            _isReversing = false;
            return;
        }

        bool inAutoMode = autoDriveController.enabled || _isAvoiding || _isReversing;
        if (!inAutoMode)
        {
            return;
        }

        bool obstacleDetected = TryDetectObstacleAhead(out RaycastHit hit, out float rightAmount);

        if (_isReversing)
        {
            UpdateReversing();
            return;
        }

        if (!_isAvoiding)
        {
            if (obstacleDetected)
            {
                BeginHandlingObstacle(hit, rightAmount);
            }
            return;
        }

        // 避障途中如果偵測到的是「不同於目前正在躲避的」新障礙物，代表冒出了新的
        // 危險（密集障礙物區域很常見），重新鎖定目標；偵測到的還是同一個物件就
        // 維持原目標不變，不要每幀重新計算——那正是之前造成原地打轉的原因。
        if (obstacleDetected && hit.transform != _currentObstacle)
        {
            BeginHandlingObstacle(hit, rightAmount);
            return;
        }

        _avoidElapsed += Time.deltaTime;

        Vector3 flatPosition = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatTarget = new Vector3(_avoidTargetPoint.x, 0f, _avoidTargetPoint.z);
        bool reachedTarget = Vector3.Distance(flatPosition, flatTarget) < avoidArrivalDistance;
        bool timedOut = _avoidElapsed >= _avoidTimeoutSeconds;

        if (reachedTarget || timedOut)
        {
            Debug.Log($"[AutoDriveObstacleAvoidance] 結束避障（{(reachedTarget ? "已到達閃避點" : "超過最長避障時間")}），" +
                      $"耗時 {_avoidElapsed:F1} 秒，交還控制權給 AutoDriveController。");
            _isAvoiding = false;
            _currentObstacle = null;
            _totalReverseElapsed = 0f; // 已經順利脫困，重置累計倒車時間
            autoDriveController.enabled = true; // 交還控制權，她會從車輛目前的位置/朝向重新對準路線
            return;
        }

        DriveTowardAvoidTarget();
    }

    /// <summary>
    /// 偵測到障礙物時的第一道判斷：距離已經非常近（代表已經卡住，例如兩側都貼著
    /// 建築物的窄縫，不管往哪邊轉都還是會撞到另一邊），就不嘗試轉向繞過去——
    /// 真實駕駛遇到這種情況也是先倒車拉開距離，而不是硬轉方向盤。距離還夠的話
    /// 才用原本的「轉向 + 前進」閃避。
    /// </summary>
    private void BeginHandlingObstacle(RaycastHit hit, float rightAmount)
    {
        if (hit.distance < stuckDistanceThreshold)
        {
            StartReversing();
        }
        else
        {
            StartAvoiding(hit, rightAmount);
        }
    }

    /// <summary>
    /// 直線倒車拉開距離。時間到了會重新偵測前方：如果還是太近（例如窄縫本身就很短，
    /// 倒一次不夠），就再倒一次；距離夠了才恢復正常的轉向閃避；前方已經淨空就直接
    /// 交還控制權。連續倒車的總時間有上限，避免任何意外情況導致無止盡倒車。
    /// </summary>
    private void StartReversing()
    {
        bool wasAlreadyReversing = _isReversing;

        _isAvoiding = false;
        _isReversing = true;
        _currentObstacle = null;
        autoDriveController.enabled = false;
        _reverseElapsed = 0f;

        if (!wasAlreadyReversing)
        {
            Debug.Log("[AutoDriveObstacleAvoidance] 偵測到障礙物距離過近（已經卡住，轉向也閃不掉），開始倒車拉開距離。");
        }
    }

    private void UpdateReversing()
    {
        _reverseElapsed += Time.deltaTime;
        _totalReverseElapsed += Time.deltaTime;

        transform.position -= transform.forward * (reverseSpeed * Time.deltaTime);

        if (_totalReverseElapsed >= maxTotalReverseSeconds)
        {
            Debug.Log($"[AutoDriveObstacleAvoidance] 連續倒車已達 {maxTotalReverseSeconds:F1} 秒上限，強制交還控制權。");
            _isReversing = false;
            _totalReverseElapsed = 0f;
            autoDriveController.enabled = true;
            return;
        }

        if (_reverseElapsed < reverseDuration)
        {
            return;
        }

        // 倒車一段時間後重新偵測前方，決定下一步。
        if (TryDetectObstacleAhead(out RaycastHit hit, out float rightAmount))
        {
            if (hit.distance < stuckDistanceThreshold)
            {
                StartReversing(); // 還是太近，再倒一次
            }
            else
            {
                _isReversing = false;
                StartAvoiding(hit, rightAmount); // 拉開距離夠了，改用轉向閃避
            }
        }
        else
        {
            Debug.Log("[AutoDriveObstacleAvoidance] 倒車後前方已淨空，交還控制權給 AutoDriveController。");
            _isReversing = false;
            _totalReverseElapsed = 0f;
            autoDriveController.enabled = true;
        }
    }

    /// <summary>
    /// 偵測到障礙物時，一次性算好一個固定的閃避目標點（往旁邊閃開 + 往車頭方向前面
    /// 一點），全程朝這個固定點開，不要每一幀都根據當下射線打到哪裡重新計算。
    /// 之前的做法是每幀重新鎖定目標，遇到形狀狹長的障礙物（例如公車）時，車輛稍微
    /// 轉向後射線可能還是持續掃到同一個障礙物的不同部位，閃避目標就一直被重新拉回，
    /// 車輛因此繞著障礙物原地打轉出不去。固定目標點可以避免這個問題。
    /// </summary>
    private void StartAvoiding(RaycastHit hit, float rightAmount)
    {
        bool wasAlreadyAvoiding = _isAvoiding;

        _isAvoiding = true;
        _currentObstacle = hit.transform;
        autoDriveController.enabled = false;
        _avoidElapsed = 0f;

        // 固定的側移/前進距離對小型障礙物（停放車輛，寬度約 2 公尺）夠用，但對建築物
        // 這種龐然大物（實測寬度/深度可以到十幾公尺）完全不夠——閃避目標點還是會落在
        // 建築物範圍內，車輛只是稍微挪動一下，很快又貼著同一棟建築物被重新偵測到。
        // 改成依碰撞體實際邊界大小動態放大：取邊界裡最大的尺寸當作「這個障礙物大概
        // 多大」的估計值，用它的一半再加上基本安全距離，確保閃避目標點真的能落在
        // 障礙物範圍之外，不管是小車還是一整棟樓都適用。
        Bounds obstacleBounds = hit.collider != null ? hit.collider.bounds : new Bounds(hit.point, Vector3.one * 2f);
        float obstacleExtent = Mathf.Max(obstacleBounds.size.x, obstacleBounds.size.y, obstacleBounds.size.z);
        float scaledClearance = Mathf.Max(avoidLateralClearance, obstacleExtent * 0.5f + avoidLateralClearance);
        float scaledForwardDistance = Mathf.Max(avoidForwardDistance, obstacleExtent * 0.5f + avoidForwardDistance);

        Vector3 candidateLeft = ComputeAvoidTarget(hit, -1f, scaledClearance, scaledForwardDistance);
        Vector3 candidateRight = ComputeAvoidTarget(hit, 1f, scaledClearance, scaledForwardDistance);

        // 分別評估「往左閃」「往右閃」這兩個候選點離最近道路格子有多近——太遠代表落在
        // 人行道、草地或建築物之間的空地，不是真的能開車通過的地方。兩邊都在合理範圍內
        // 時，選離道路格子更近的那一側，比較像是真正好走、寬敞的馬路，而不是隨便挑邊；
        // 只有一邊在範圍內就選那一邊；兩邊都不在範圍內（場景資料本身有缺口的罕見情況）
        // 才退回原本「障礙物在哪一側就往另一側閃」的簡單判斷。
        bool leftOnRoad = IsNearRoad(candidateLeft, out float leftRoadDistance);
        bool rightOnRoad = IsNearRoad(candidateRight, out float rightRoadDistance);

        Vector3 chosenTarget;
        if (leftOnRoad && rightOnRoad)
        {
            chosenTarget = leftRoadDistance <= rightRoadDistance ? candidateLeft : candidateRight;
        }
        else if (leftOnRoad)
        {
            chosenTarget = candidateLeft;
        }
        else if (rightOnRoad)
        {
            chosenTarget = candidateRight;
        }
        else
        {
            // 障礙物中心偏車身右邊（rightAmount 為正）就往左閃（-1），反之亦然。
            chosenTarget = rightAmount >= 0f ? candidateLeft : candidateRight;
        }

        _avoidTargetPoint = chosenTarget;

        // 閃避距離現在會依障礙物大小放大，固定 5 秒的逾時對大型障礙物可能不夠走到——
        // 改成依「理論所需時間 x 安全係數」動態計算，短距離仍保底用 minAvoidSeconds。
        float targetDistance = Vector3.Distance(
            new Vector3(transform.position.x, 0f, transform.position.z),
            new Vector3(_avoidTargetPoint.x, 0f, _avoidTargetPoint.z));
        float estimatedSeconds = (targetDistance / Mathf.Max(0.1f, avoidMoveSpeed)) * avoidTimeoutSafetyFactor;
        _avoidTimeoutSeconds = Mathf.Max(minAvoidSeconds, estimatedSeconds);

        Collider hitCollider = hit.collider;
        string colliderInfo = hitCollider != null
            ? $"{hitCollider.GetType().Name}（邊界 {hitCollider.bounds.size:F2}）"
            : "無 Collider";

        string directionInfo = leftOnRoad && rightOnRoad
            ? $"兩側都在道路上，選{(chosenTarget == candidateLeft ? "左" : "右")}（較近道路）"
            : leftOnRoad ? "選左（右側不在道路上）"
            : rightOnRoad ? "選右（左側不在道路上）"
            : "兩側都不在道路上，退回簡單判斷";

        Debug.Log($"[AutoDriveObstacleAvoidance] {(wasAlreadyAvoiding ? "避障中偵測到新的障礙物，重新鎖定目標" : "偵測到障礙物，開始避障")}" +
                  $"「{hit.transform.name}」（距離 {hit.distance:F1} 公尺，碰撞體 {colliderInfo}），{directionInfo}，" +
                  $"閃避目標點 {_avoidTargetPoint:F2}，本次逾時上限 {_avoidTimeoutSeconds:F1} 秒。");
    }

    /// <summary>
    /// 算出往某一側（-1 為左、+1 為右）閃避時的候選目標點：往旁邊閃開一段依障礙物大小
    /// 放大過的距離，再往車頭方向前面一點。
    /// </summary>
    private Vector3 ComputeAvoidTarget(RaycastHit hit, float directionSign, float lateralClearance, float forwardDistance)
    {
        Vector3 lateralDir = transform.right * directionSign;
        return hit.point + lateralDir * (detectionRadius + lateralClearance) + transform.forward * forwardDistance;
    }

    /// <summary>
    /// 檢查某個位置是不是落在合理靠近道路網格的範圍內——太遠代表落在人行道、草地或
    /// 建築物之間的空地，不是真的能開車通過的地方。沒有指定 pathfinder 時一律視為
    /// 「不在道路上」，呼叫端會因此退回原本的簡單方向判斷，行為維持不變。
    /// </summary>
    private bool IsNearRoad(Vector3 position, out float distanceToRoad)
    {
        distanceToRoad = float.PositiveInfinity;

        if (pathfinder == null || !pathfinder.SnapToNearestRoadCell(position, out Vector2Int cell))
        {
            return false;
        }

        Vector3 roadWorldPos = pathfinder.GetCellWorldPosition(cell);
        float dx = roadWorldPos.x - position.x;
        float dz = roadWorldPos.z - position.z;
        distanceToRoad = Mathf.Sqrt(dx * dx + dz * dz);

        return distanceToRoad <= roadProximityThreshold;
    }

    /// <summary>
    /// 朝固定的閃避目標點轉向、往車頭方向前進——跟 AutoDriveController 原本的移動邏輯
    /// 是同一套模式（算方向 → LookRotation 轉向 → 往前走），只是目標點換成閃避點，
    /// 不是路口座標。
    /// </summary>
    private void DriveTowardAvoidTarget()
    {
        Vector3 direction = _avoidTargetPoint - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.0001f)
        {
            Quaternion desiredRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, desiredRotation, avoidTurnSpeed * Time.deltaTime);
        }

        transform.position += transform.forward * (avoidMoveSpeed * Time.deltaTime);
    }

    void LateUpdate()
    {
        // 跟 Update() 同樣的理由：手動模式下這裡完全不該插手 Transform，
        // 讓 WheelCollider 物理正常運作。
        if (_rigidbody != null && !_rigidbody.isKinematic)
        {
            return;
        }

        bool inAutoMode = (autoDriveController != null && autoDriveController.enabled) || _isAvoiding || _isReversing;
        if (!inAutoMode)
        {
            return;
        }

        ResolveActualOverlap();
    }

    /// <summary>
    /// 往車頭正前方打一顆球形射線，偵測到障礙物回傳 true，並算出障礙物相對車身
    /// 「右方向」的投影量，供呼叫端判斷該往哪邊閃。
    /// </summary>
    private bool TryDetectObstacleAhead(out RaycastHit hit, out float rightAmount)
    {
        rightAmount = 0f;

        Vector3 origin = transform.position + Vector3.up * detectionHeight;

        if (!Physics.SphereCast(
            origin, detectionRadius, transform.forward, out hit, detectionDistance,
            ~0, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        if (hit.transform.IsChildOf(transform))
        {
            return false; // 打到自己的輪子/車身，忽略
        }

        // 名稱以「Road 」開頭的物件是道路本身（跟 RoadGridPathfinder 辨識道路的規則
        // 一致，包含路緣、人行道邊緣等）——路緣側面幾乎是垂直的，法向量角度檢查
        // 沒辦法跟真正的側向障礙物區分，實測就是這個原因被誤判成障礙物，車輛被帶偏
        // 到路邊，才接著撞見建築物、路燈等一連串不該遇到的「障礙物」。直接用名稱排除。
        if (hit.transform.name.StartsWith("Road "))
        {
            return false;
        }

        // 額外保險：不管偵測球的高度/半徑怎麼調，只要打到的表面法向量幾乎朝正上方，
        // 就是地面/路面（或任何水平面），不是真正擋在前面的障礙物，直接忽略。
        if (Vector3.Angle(hit.normal, Vector3.up) < groundNormalAngleThreshold)
        {
            return false;
        }

        Vector3 toHit = hit.point - transform.position;
        rightAmount = Vector3.Dot(toHit, transform.right);
        return true;
    }

    /// <summary>
    /// 用車身自己的 Box Collider 檢查目前是否真的跟其他碰撞體重疊，重疊就用
    /// Physics.ComputePenetration 算出的方向/距離強制推開。只採用明顯偏水平方向的推擠
    /// （真正的側向障礙物）；偏垂直方向的推擠視為車身跟路面網格重疊的正常貼地情況，
    /// 忽略掉，避免路面網格三角形邊緣的方向雜訊被誤判成側向障礙物、造成規律抖動。
    /// </summary>
    private void ResolveActualOverlap()
    {
        Vector3 worldCenter = transform.TransformPoint(_bodyCollider.center);
        Vector3 halfExtents = Vector3.Scale(_bodyCollider.size, transform.lossyScale) * 0.5f;

        Collider[] overlaps = Physics.OverlapBox(
            worldCenter, halfExtents, transform.rotation, ~0, QueryTriggerInteraction.Ignore);

        foreach (Collider other in overlaps)
        {
            if (other == _bodyCollider || other.transform.IsChildOf(transform))
            {
                continue;
            }

            // 跟 TryDetectObstacleAhead 同樣的理由：道路本身（含路緣）不該被當成
            // 要推開的障礙物。
            if (other.transform.name.StartsWith("Road "))
            {
                continue;
            }

            bool penetrated = Physics.ComputePenetration(
                _bodyCollider, transform.position, transform.rotation,
                other, other.transform.position, other.transform.rotation,
                out Vector3 direction, out float distance);

            if (!penetrated)
            {
                continue;
            }

            // Vector3.Angle 對 up 的夾角：0/180 = 正上方或正下方（貼地/被壓），90 = 正水平
            // （側向障礙物）。換算成「偏離水平面多少度」：0 = 水平，90 = 垂直，
            // 這樣才能直接跟「最大允許偏離水平幾度」的門檻比較。
            float angleFromUp = Vector3.Angle(direction, Vector3.up);
            float deviationFromHorizontal = Mathf.Abs(90f - angleFromUp);

            if (deviationFromHorizontal > maxPushAngleFromHorizontal)
            {
                continue; // 太接近垂直方向，視為貼地，不處理
            }

            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.position += direction.normalized * distance;
            }
        }
    }
}
