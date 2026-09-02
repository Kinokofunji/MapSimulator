using System.Collections;
using UnityEngine;

namespace Navigation
{
    /// <summary>
    /// 確保載具永遠站在「真實已載入的地形表面」上。
    ///
    /// 這個元件存在的理由是 Cesium 的圖磚（含碰撞網格）是非同步串流的：進 Play 模式的頭幾秒
    /// 到幾十秒，車子腳下根本沒有任何碰撞體。而場景裡存的車輛高度是用 Google Elevation API
    /// 算出來的「真實世界海拔」，跟 Cesium World Terrain 實際生成的網格高度會差幾公尺——
    /// 差在哪一邊都很糟：差太高會從半空掉下來，差太低則是一開場就埋在地底，
    /// 畫面會變成從地形背面往外看的碎裂感。
    ///
    /// 做法分三個階段：
    ///  1. 開場先把車鎖成 kinematic 並抬到高處，避免在沒有地面的期間穿過去，
    ///     同時讓攝影機停在空中俯視（看得到圖磚正在載入，而不是一片黑）。
    ///  2. 持續往下打射線，直到真的打到地形為止——不設放棄時限，網路再慢也會等到。
    ///  3. 之後每隔一段時間檢查一次，只要車子掉到地面下超過門檻（圖磚在行進中被卸載
    ///     又還沒載回來時會發生），就立刻重新貼地。
    /// </summary>
    [DisallowMultipleComponent]
    public class VehicleTerrainSnap : MonoBehaviour
    {
        [Header("要照顧的載具（啟用中的那一台才會被處理）")]
        [SerializeField] private Transform[] vehicles;

        [Header("貼地參數")]
        [Tooltip("車輛原點高於地面的距離（公尺）。太小車輪會陷進地面，太大會從空中落下")]
        [SerializeField] private float groundBuffer = 1.2f;

        [Tooltip("等待地形載入時，先把車抬高多少公尺。0 = 不抬高（車輛初始座標已經是 Elevation API 算出的正確地面高度）")]
        [SerializeField] private float preLiftHeight;

        [Tooltip("開場定位時，射線起點在車輛上方多少公尺")]
        [SerializeField] private float probeHeight = 6f;

        [Tooltip("開場定位的射線長度（公尺）")]
        [SerializeField] private float probeDistance = 40f;

        [Tooltip("行進中偵測腳下地面時，射線起點在車輛上方多少公尺。要很小，否則會抓到旁邊的山壁")]
        [SerializeField] private float contactProbeAbove = 2f;

        [Tooltip("行進中偵測腳下地面的射線長度（公尺）")]
        [SerializeField] private float contactProbeDistance = 8f;

        [Header("圖磚細化期的持續修正")]
        [Tooltip("初次貼地後，還要持續重新校正多久（秒）。Google 攝影測量是漸進式串流，" +
                 "先來的粗糙圖磚可能比真實地表低好幾公尺")]
        [SerializeField] private float settleSeconds = 20f;

        [Tooltip("細化期間每隔幾秒重新校正一次")]
        [SerializeField] private float settleInterval = 1f;

        [Tooltip("重新校正時，表面高度差超過這個值才動車（公尺），避免每次都微調造成抖動")]
        [SerializeField] private float settleThreshold = 0.6f;

        [Tooltip("連續幾次量到的地表都沒變化，才認定圖磚細化完成、放行車輛行駛")]
        [SerializeField] private int requiredStableChecks = 2;

        [Header("行進中的防護")]
        [Tooltip("車子低於地面超過這個距離就重新貼地（公尺）")]
        [SerializeField] private float fallThreshold = 2.5f;

        [Tooltip("行進中每隔幾秒檢查一次。時速 60 時每 0.25 秒會移動約 4 公尺，再慢就來不及救")]
        [SerializeField] private float monitorInterval = 0.25f;

        [Tooltip("這個名稱的物件會被射線忽略（防墜用的隱形地板，量到它等於沒量到真正的地面）")]
        [SerializeField] private string safetyFloorName = "NavigationSafetyFloor";

        [Tooltip("建築圖磚物件。它的碰撞網格會被貼地射線忽略——屋頂不是地面")]
        [SerializeField] private Transform buildingsTileset;

        [Tooltip("往上偵測「被埋在地形裡」的射線長度（公尺）。太長會把天橋、高架橋誤判成被埋")]
        [SerializeField] private float buriedProbeDistance = 6f;

        private Coroutine _routine;

        /// <summary>
        /// 車子目前是否處於「已抬高、等待地形」的狀態。
        /// 需要這個旗標是因為 RequestSnap 會被連續呼叫兩次（OnEnable 一次、MapStyleSwitcher
        /// 在 Start 套用風格時又一次），沒有擋的話車子會被抬高兩次、直接飛到 90 公尺高空。
        /// </summary>
        private bool _isLifted;

        /// <summary>
        /// 車輛原本的 isKinematic 狀態，以及目前是否正處於「被我們凍結」的狀態。
        ///
        /// 這兩個必須是欄位而不是區域變數：RequestSnap 開場會被呼叫兩次
        /// （OnEnable 一次、MapStyleSwitcher 套用風格時又一次），如果每次都重新讀取
        /// 當下的 isKinematic 當作「原本的狀態」，第二次讀到的就是第一次剛設下去的 true，
        /// 貼地完成後便會把車「還原」成 kinematic——物理整個失效，手動駕駛完全按不動。
        /// </summary>
        private bool _isFrozen;
        private bool _originalKinematic;

        /// <summary>
        /// 最後一次確認「車子確實踩在地面上」時的位置。
        ///
        /// 掉落偵測不能用「射線量到的最高碰撞點」當基準——木柵路沿著山壁走，山壁就在
        /// 車子上方十幾公尺，取最高點會把山壁當成地面，然後判定車子掉到地下、
        /// 硬把它拉到山壁上去。改成記住最後一次的有效接觸點，掉太深就送回那裡，
        /// 完全不需要去猜「上方的東西是不是地面」。
        /// </summary>
        private Vector3 _lastGroundedPosition;
        private float _lastGroundedY;
        private bool _hasGrounded;

        private void OnEnable()
        {
            RequestSnap();
        }

        /// <summary>要求重新貼地。切換地圖圖資之後一定要呼叫一次（兩套圖資的地面高度不同）。</summary>
        public void RequestSnap()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (_routine != null)
            {
                StopCoroutine(_routine);
            }

            _routine = StartCoroutine(SnapThenMonitor());
        }

        private IEnumerator SnapThenMonitor()
        {
            Transform vehicle = GetActiveVehicle();
            if (vehicle == null)
            {
                Debug.LogWarning("[VehicleTerrainSnap] 找不到啟用中的載具，貼地流程中止。");
                _routine = null;
                yield break;
            }

            Rigidbody body = vehicle.GetComponent<Rigidbody>();

            // ── 階段 1：鎖住並抬高 ──
            if (body != null)
            {
                // 只有在還沒凍結過的時候才記錄原本的狀態，否則會把自己設下去的 true 當成原值。
                if (!_isFrozen)
                {
                    _originalKinematic = body.isKinematic;
                    _isFrozen = true;
                }

                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

            if (!_isLifted)
            {
                Vector3 lifted = vehicle.position;
                lifted.y += preLiftHeight;
                vehicle.position = lifted;
                _isLifted = true;
            }

            // ── 階段 2：等到真的打到地形為止 ──
            float waited = 0f;
            float nextLogAt = 5f;

            while (true)
            {
                if (TryFindNearestSurface(vehicle, out float groundY))
                {
                    Vector3 landed = vehicle.position;
                    landed.y = groundY + groundBuffer;
                    vehicle.position = landed;
                    _isLifted = false;

                    _lastGroundedPosition = landed;
                    _lastGroundedY = groundY;
                    _hasGrounded = true;

                    // ★ 這裡刻意「還不」解除凍結。
                    // Google 的攝影測量是漸進式串流，第一個找到的表面往往是粗糙圖磚，
                    // 可能比真實地表低好幾公尺。這時候就放車子開，它會在地形內部行駛一段，
                    // 直到細化完成才被拉上來——實測有一次高達 17% 的取樣點腳下沒有地面。
                    // 解除凍結延後到下面的細化期收斂之後。

                    Debug.Log($"[VehicleTerrainSnap] 地形已載入，「{vehicle.name}」貼地完成：" +
                              $"地面 Y={groundY:F2}、車輛 Y={landed.y:F2}（等待 {waited:F1} 秒）、" +
                              $"isKinematic={(body != null ? body.isKinematic.ToString() : "無 Rigidbody")}" +
                              "（自動駕駛模式下應為 True，手動模式下應為 False）。");
                    break;
                }

                yield return new WaitForSeconds(0.2f);
                waited += 0.2f;

                if (waited >= nextLogAt)
                {
                    Debug.Log($"[VehicleTerrainSnap] 已等待 {waited:F0} 秒，車輛下方仍未出現地形碰撞網格，繼續等待中……");
                    nextLogAt += 10f;
                }
            }

            // ── 階段 2.5：圖磚細化期的持續修正 ──
            //
            // 這一段是必要的，不是保險。Google 的攝影測量圖磚是漸進式串流：先送一塊粗糙的
            // 低精細度圖磚，再逐步細化。如果貼地在「找到第一個表面」的瞬間就定案，很可能
            // 抓到的是那塊粗糙圖磚——實測同一個場景連跑兩次，起始高度可以差 8.7 公尺，
            // 差的那次車子整台埋在地表下 9 公尺，而且因為監看用的是「腳下 2 公尺」的短射線，
            // 探不到地面、救援也不會觸發，車子就永遠卡在裡面。
            float settleUntil = Time.time + settleSeconds;
            int stableChecks = 0;

            while (Time.time < settleUntil)
            {
                yield return new WaitForSeconds(settleInterval);

                vehicle = GetActiveVehicle();
                if (vehicle == null)
                {
                    continue;
                }

                if (!TryFindNearestSurface(vehicle, out float refinedY))
                {
                    continue;
                }

                float targetY = refinedY + groundBuffer;
                if (Mathf.Abs(vehicle.position.y - targetY) < settleThreshold)
                {
                    // 連續兩次量到的表面都跟目前高度一致，就認定圖磚已經細化完成。
                    if (++stableChecks >= requiredStableChecks)
                    {
                        break;
                    }

                    continue;
                }

                stableChecks = 0;

                Vector3 corrected = vehicle.position;
                float previousY = corrected.y;
                corrected.y = targetY;
                vehicle.position = corrected;

                _lastGroundedPosition = corrected;
                _lastGroundedY = refinedY;

                Rigidbody settleBody = vehicle.GetComponent<Rigidbody>();
                if (settleBody != null && !settleBody.isKinematic)
                {
                    settleBody.velocity = Vector3.zero;
                    settleBody.angularVelocity = Vector3.zero;
                }

                Debug.Log($"[VehicleTerrainSnap] 圖磚細化，「{vehicle.name}」高度由 {previousY:F2} 修正為 {targetY:F2}" +
                          $"（差 {targetY - previousY:F2} 公尺）。");
            }

            // ── 階段 2.9：確認地表穩定後才放行 ──
            Rigidbody releaseBody = vehicle != null ? vehicle.GetComponent<Rigidbody>() : null;
            if (releaseBody != null)
            {
                DriveModeSwitcher modeSwitcher = vehicle.GetComponent<DriveModeSwitcher>();
                if (modeSwitcher != null)
                {
                    // kinematic 狀態由駕駛模式決定：自動駕駛必須維持 kinematic
                    // （AutoDriveController 直接搬 Transform），用記下的「原始值」還原會蓋掉它。
                    modeSwitcher.ReapplyMode();
                }
                else
                {
                    releaseBody.isKinematic = _originalKinematic;
                }

                if (!releaseBody.isKinematic)
                {
                    releaseBody.velocity = Vector3.zero;
                    releaseBody.angularVelocity = Vector3.zero;
                }

                _isFrozen = false;
            }

            Debug.Log($"[VehicleTerrainSnap] 地表已穩定，「{(vehicle != null ? vehicle.name : "?")}」放行行駛" +
                      $"（Y={(vehicle != null ? vehicle.position.y : 0f):F2}）。");

            // ── 階段 3：行進中持續監看 ──
            int consecutiveNoGround = 0;
            while (true)
            {
                yield return new WaitForSeconds(monitorInterval);

                vehicle = GetActiveVehicle();
                if (vehicle == null)
                {
                    continue;
                }

                // ★ 先偵測「被埋在地形裡」。
                //
                // 這是先前一直漏掉的情況：原本只判斷「腳下探不到地面」，但車子陷進一塊
                // 有厚度的網格時，它腳下其實有東西（網格的底面），偵測根本不會觸發。
                // 攝影測量的地形正是這種厚網格。直接往上打射線，頭頂有地形就是被埋了。
                if (TryFindSurfaceAbove(vehicle, out float ceilingY))
                {
                    Vector3 dug = vehicle.position;
                    float beforeY = dug.y;
                    dug.y = ceilingY + groundBuffer;
                    vehicle.position = dug;

                    _lastGroundedPosition = dug;
                    _lastGroundedY = ceilingY;
                    _hasGrounded = true;
                    consecutiveNoGround = 0;

                    Rigidbody digBody = vehicle.GetComponent<Rigidbody>();
                    if (digBody != null && !digBody.isKinematic)
                    {
                        digBody.velocity = Vector3.zero;
                        digBody.angularVelocity = Vector3.zero;
                    }

                    Debug.LogWarning($"[VehicleTerrainSnap] 「{vehicle.name}」被埋在地形裡" +
                                     $"（頭頂 {ceilingY - beforeY:F1} 公尺處有地面），已拉出到 Y={dug.y:F2}。");
                    continue;
                }

                // 只看車子正下方很短的一段距離。踩到地面就更新「最後有效位置」。
                if (TryFindGroundBelow(vehicle, contactProbeAbove, contactProbeDistance, out float contactY))
                {
                    _lastGroundedPosition = vehicle.position;
                    _lastGroundedY = contactY;
                    _hasGrounded = true;
                    consecutiveNoGround = 0;
                    continue;
                }

                // 腳下連續探不到地面，代表車子已經不在路面上了——最常見的情況是它被埋在
                // 地形內部（短射線從車上方 2 公尺往下打，探不到位在更上方的真實地表）。
                // 這時要用寬範圍的「最近表面」重新定位，而不是等掉落判定，因為埋在地裡的車
                // 高度沒有變化，掉落判定永遠不會成立。
                if (++consecutiveNoGround >= 4)
                {
                    consecutiveNoGround = 0;

                    if (TryFindNearestSurface(vehicle, out float recoveredY))
                    {
                        Vector3 recovered = vehicle.position;
                        float previousY = recovered.y;
                        recovered.y = recoveredY + groundBuffer;
                        vehicle.position = recovered;

                        _lastGroundedPosition = recovered;
                        _lastGroundedY = recoveredY;
                        _hasGrounded = true;

                        Rigidbody recoverBody = vehicle.GetComponent<Rigidbody>();
                        if (recoverBody != null && !recoverBody.isKinematic)
                        {
                            recoverBody.velocity = Vector3.zero;
                            recoverBody.angularVelocity = Vector3.zero;
                        }

                        Debug.LogWarning($"[VehicleTerrainSnap] 「{vehicle.name}」腳下連續探不到地面，" +
                                         $"已用最近表面重新定位：{previousY:F2} → {recovered.y:F2}。");
                    }

                    continue;
                }

                // 腳下沒有地面：可能是騰空（正常，例如過坡頂）或已經掉進地形裡。
                if (!_hasGrounded || vehicle.position.y >= _lastGroundedY - fallThreshold)
                {
                    continue;
                }

                vehicle.position = _lastGroundedPosition;

                Rigidbody rescueBody = vehicle.GetComponent<Rigidbody>();
                if (rescueBody != null)
                {
                    rescueBody.velocity = Vector3.zero;
                    rescueBody.angularVelocity = Vector3.zero;
                }

                Debug.LogWarning($"[VehicleTerrainSnap] 「{vehicle.name}」掉到最後有效地面下方 " +
                                 $"{_lastGroundedY - vehicle.position.y:F1} 公尺，已送回 {_lastGroundedPosition}。");
            }
        }

        /// <summary>
        /// 只找「車輛正下方」的地面：射線從車子稍上方起算，取最高的碰撞點。
        /// 起點刻意壓得很低，這樣旁邊的山壁、頭頂的樓板都不會被誤認成地面——
        /// 那正是先前車子被拉到山上的原因。
        /// </summary>
        /// <summary>
        /// 開場定位用：取「離車輛目前高度最近」的那個表面，而不是最高或最低的。
        ///
        /// 為什麼不能用「從上方 N 公尺往下打、取最高點」：
        ///  • N 設大（800m）→ 山洞口正上方是山體，車子被放到山頂
        ///  • N 設小（6m）→ 照片級圖磚的地表跟 Elevation API 的高程可以差好幾公尺，
        ///    只要真實地表比車子高出 6 公尺，射線起點就已經在地表底下，往下打永遠打不到，
        ///    車子就一直卡在地形內部、保持 kinematic、完全不會動
        ///
        /// 車輛的初始座標是 Elevation API 算出來的，誤差是「幾公尺」等級而不是「幾十公尺」。
        /// 所以掃一個寬範圍、再挑高度最接近的那個表面，兩種失敗模式都能避開：
        /// 山頂離車子幾十公尺，永遠不會被選中；路面只差幾公尺，一定會中。
        /// </summary>
        private bool TryFindNearestSurface(Transform vehicle, out float groundY)
        {
            groundY = 0f;

            Vector3 origin = vehicle.position + Vector3.up * 120f;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 300f, ~0, QueryTriggerInteraction.Ignore);

            bool found = false;
            float bestDistance = float.MaxValue;

            foreach (RaycastHit hit in hits)
            {
                if (hit.transform.IsChildOf(vehicle) ||
                    hit.transform.name.Trim() == safetyFloorName ||
                    (buildingsTileset != null && hit.transform.IsChildOf(buildingsTileset)))
                {
                    continue;
                }

                float distance = Mathf.Abs(hit.point.y - vehicle.position.y);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    groundY = hit.point.y;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// 偵測車輛是否被埋在地形裡：從車輛稍上方往上打射線，打到地形就代表車在它下面。
        ///
        /// 這個判斷比「腳下探不到地面」可靠得多。攝影測量與地形圖磚的網格是有厚度的實體，
        /// 車子陷進去時腳下仍然有碰撞面（網格的底面），用「探不到地面」永遠偵測不到。
        ///
        /// 建築要排除：正常在騎樓、天橋、高架下方行駛時頭頂本來就有結構，那不是被埋。
        /// </summary>
        private bool TryFindSurfaceAbove(Transform vehicle, out float surfaceY)
        {
            surfaceY = 0f;

            // 從車頂稍上方起算，避免打到車身自己。
            Vector3 origin = vehicle.position + Vector3.up * 0.6f;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.up, buriedProbeDistance, ~0, QueryTriggerInteraction.Ignore);

            bool found = false;
            float lowest = float.MaxValue;

            foreach (RaycastHit hit in hits)
            {
                if (hit.transform.IsChildOf(vehicle) ||
                    hit.transform.name.Trim() == safetyFloorName ||
                    (buildingsTileset != null && hit.transform.IsChildOf(buildingsTileset)))
                {
                    continue;
                }

                // 取最低的那個，也就是「正上方最近的一層地面」。
                if (hit.point.y < lowest)
                {
                    lowest = hit.point.y;
                    found = true;
                }
            }

            if (found)
            {
                surfaceY = lowest;
            }

            return found;
        }

        private bool TryFindGroundBelow(Transform vehicle, float startAbove, float maxDistance, out float groundY)
        {
            groundY = 0f;

            Vector3 origin = vehicle.position + Vector3.up * startAbove;
            RaycastHit[] hits = Physics.RaycastAll(
                origin, Vector3.down, startAbove + maxDistance, ~0, QueryTriggerInteraction.Ignore);

            bool found = false;
            float highest = float.MinValue;

            foreach (RaycastHit hit in hits)
            {
                if (hit.transform.IsChildOf(vehicle))
                {
                    continue;
                }

                if (hit.transform.name.Trim() == safetyFloorName)
                {
                    continue;
                }

                if (buildingsTileset != null && hit.transform.IsChildOf(buildingsTileset))
                {
                    continue;
                }

                if (hit.point.y > highest)
                {
                    highest = hit.point.y;
                    found = true;
                }
            }

            if (found)
            {
                groundY = highest;
            }

            return found;
        }

        private Transform GetActiveVehicle()
        {
            if (vehicles == null)
            {
                return null;
            }

            foreach (Transform vehicle in vehicles)
            {
                if (vehicle != null && vehicle.gameObject.activeInHierarchy)
                {
                    return vehicle;
                }
            }

            return null;
        }
    }
}
