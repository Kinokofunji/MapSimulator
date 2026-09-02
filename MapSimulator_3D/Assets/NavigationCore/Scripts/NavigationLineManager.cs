using System;
using System.Collections.Generic;
using UnityEngine;

namespace Navigation
{
    /// <summary>
    /// 轉彎類型，對應 Google Maps 導航常見的指引圖示。
    /// </summary>
    public enum TurnType
    {
        Straight,   // 直行
        TurnLeft,   // 左轉
        TurnRight,  // 右轉
        UTurn       // 迴轉
    }

    /// <summary>
    /// 每一個路口（Waypoint）對應的導航資訊，供 UI 顯示轉彎圖示與路名使用。
    /// 此 List 的索引順序必須與 NavigationLineManager.waypoints 完全對應（一對一）。
    /// </summary>
    [Serializable]
    public class WaypointInfo
    {
        [Tooltip("抵達此路口時要顯示的轉彎方向")]
        public TurnType turnType = TurnType.Straight;

        [Tooltip("路名提示文字，例如：基隆路一段")]
        public string roadName = "";
    }

    /// <summary>
    /// 導航路徑管理器：
    /// 1. 使用 LineRenderer 在地面上繪製導航藍線。
    /// 2. 管理路口 Waypoint 清單（Inspector 可手動設定 3D 座標）。
    /// 3. 即時判斷玩家目前走到哪一個路口節點，並動態更新線段頂點。
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class NavigationLineManager : MonoBehaviour
    {
        [Header("玩家 / 載具")]
        [Tooltip("玩家（載具）Transform，用於計算距離與繪製線段起點")]
        [SerializeField] private Transform player;

        [Header("路徑資料")]
        [Tooltip("導航路口的 3D 座標點清單，依行進順序排列")]
        [SerializeField] private List<Vector3> waypoints = new List<Vector3>();

        [Tooltip("每個路口對應的轉彎資訊（索引需與 waypoints 一一對應）")]
        [SerializeField] private List<WaypointInfo> waypointInfos = new List<WaypointInfo>();

        [Header("到達判定")]
        [Tooltip("玩家與路口距離小於此值時，視為「已通過該路口」，自動切換到下一個目標")]
        [SerializeField] private float arrivalThreshold = 5f;

        [Header("線段顯示設定")]
        [Tooltip("線段離地高度偏移，避免與地面 Z-fighting 造成閃爍")]
        [SerializeField] private float lineHeightOffset = 0.3f;

        [Tooltip("是否隨玩家通過路口而裁切掉已走過的線段，只顯示剩餘路徑（模擬 Google Maps 效果）")]
        [SerializeField] private bool trimPassedWaypoints = true;

        [Header("完整路線折線（畫線用）")]
        [Tooltip("Google Directions 回傳的完整道路折線，點數遠多於 waypoints。有資料時會用它畫線")]
        [SerializeField] private List<Vector3> routePath = new List<Vector3>();

        [Tooltip("執行時把折線的每個點用射線貼合實際地形（Cesium 圖磚是串流載入的，需要等待與重試）")]
        [SerializeField] private bool snapRoutePathToGround = true;

        [Tooltip("玩家離折線上的點多近就算已通過，超過就往前推進")]
        [SerializeField] private float routeAdvanceDistance = 12f;

        private LineRenderer _lineRenderer;

        /// <summary>目前折線畫到哪一個點開始。玩家往前開就跟著推進，把走過的部分裁掉。</summary>
        private int _routeStartIndex;

        /// <summary>目前導航目標的路口索引。</summary>
        public int CurrentWaypointIndex { get; private set; }

        /// <summary>是否已經抵達最後一個路口（導航結束）。</summary>
        public bool HasReachedDestination => waypoints.Count == 0 || CurrentWaypointIndex >= waypoints.Count;

        /// <summary>每當玩家通過一個路口時觸發，參數為「剛通過的路口索引」。可供 UI 或音效訂閱。</summary>
        public event Action<int> OnWaypointReached;

        private void Awake()
        {
            _lineRenderer = GetComponent<LineRenderer>();
        }

        private void Start()
        {
            if (waypointInfos.Count != waypoints.Count)
            {
                Debug.LogWarning("[NavigationLineManager] waypointInfos 數量與 waypoints 數量不一致，請確認 Inspector 設定是否對應正確。");
            }

            RedrawLine();

            if (snapRoutePathToGround && routePath.Count > 0)
            {
                StartCoroutine(SnapRoutePathToGround());
            }
        }

        private void Update()
        {
            if (player == null || HasReachedDestination)
            {
                return;
            }

            AdvanceRoutePath();

            Vector3 currentTarget = waypoints[CurrentWaypointIndex];
            float distanceToTarget = Vector3.Distance(player.position, currentTarget);

            // 每影格更新線段起點為玩家目前位置，讓藍線視覺上「從車頭延伸出去」
            UpdateLineStartPoint();

            if (distanceToTarget <= arrivalThreshold)
            {
                int reachedIndex = CurrentWaypointIndex;
                CurrentWaypointIndex++;

                OnWaypointReached?.Invoke(reachedIndex);

                // 路口切換時才需要重建整條線的頂點（相對耗費效能，不必每影格執行）
                if (trimPassedWaypoints)
                {
                    RedrawLine();
                }
            }
        }

        /// <summary>
        /// 只更新線段的第一個頂點（玩家目前位置），避免每影格都重新配置整個頂點陣列。
        /// </summary>
        private void UpdateLineStartPoint()
        {
            if (_lineRenderer.positionCount == 0)
            {
                return;
            }

            _lineRenderer.SetPosition(0, player.position + Vector3.up * lineHeightOffset);
        }

        /// <summary>
        /// 依照「玩家目前位置 + 尚未走完的路口清單」重新建立 LineRenderer 的所有頂點。
        /// </summary>
        private void RedrawLine()
        {
            List<Vector3> remainingPoints = new List<Vector3>();

            if (player != null)
            {
                remainingPoints.Add(player.position + Vector3.up * lineHeightOffset);
            }

            if (routePath.Count >= 2)
            {
                // 有完整折線就用它。waypoints 只有每個轉彎路口一個點（整條路線大約 5 個），
                // 拿它畫線會變成直接橫跨街廓的折線，看起來像一塊藍色板子壓在地圖上；
                // 折線則是沿著真實道路中心走的，點數多好幾十倍。
                for (int i = _routeStartIndex; i < routePath.Count; i++)
                {
                    remainingPoints.Add(routePath[i] + Vector3.up * lineHeightOffset);
                }
            }
            else
            {
                for (int i = CurrentWaypointIndex; i < waypoints.Count; i++)
                {
                    remainingPoints.Add(waypoints[i] + Vector3.up * lineHeightOffset);
                }
            }

            _lineRenderer.positionCount = remainingPoints.Count;
            _lineRenderer.SetPositions(remainingPoints.ToArray());
        }

        /// <summary>
        /// 把折線的起點往前推進，讓已經開過的路段從畫面上消失（Google Maps 也是這樣）。
        /// 只往前找幾個點就好，不需要每影格掃整條路線。
        /// </summary>
        private void AdvanceRoutePath()
        {
            if (routePath.Count < 2 || !trimPassedWaypoints)
            {
                return;
            }

            bool advanced = false;

            while (_routeStartIndex < routePath.Count - 1 &&
                   Vector3.Distance(player.position, routePath[_routeStartIndex]) < routeAdvanceDistance)
            {
                _routeStartIndex++;
                advanced = true;
            }

            if (advanced)
            {
                RedrawLine();
            }
        }

        /// <summary>
        /// 把折線的每個點貼合實際地形。折線的高度來自 Google Elevation API，
        /// 跟 Cesium 實際生成的地形網格會差一兩公尺——差在下面線就埋進路面裡看不見了。
        /// Cesium 的圖磚是串流載入的，所以要多輪重試，車開到哪、哪一段才貼得上。
        /// </summary>
        private System.Collections.IEnumerator SnapRoutePathToGround()
        {
            bool[] snapped = new bool[routePath.Count];
            int remaining = routePath.Count;

            yield return new WaitForSeconds(3f);

            for (int round = 0; round < 120 && remaining > 0; round++)
            {
                for (int i = 0; i < routePath.Count; i++)
                {
                    if (snapped[i])
                    {
                        continue;
                    }

                    Vector3 origin = routePath[i] + Vector3.up * 300f;
                    if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 800f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }

                    if (hit.transform.name.Trim() == "NavigationSafetyFloor")
                    {
                        continue;
                    }

                    routePath[i] = new Vector3(routePath[i].x, hit.point.y, routePath[i].z);
                    snapped[i] = true;
                    remaining--;
                }

                RedrawLine();
                yield return new WaitForSeconds(2.5f);
            }

            Debug.Log($"[NavigationLineManager] 路線折線貼地完成：{routePath.Count - remaining}/{routePath.Count} 個點。");
        }

        /// <summary>供編輯器工具寫入完整折線。</summary>
        public void SetRoutePath(List<Vector3> path)
        {
            routePath = path;
            _routeStartIndex = 0;
        }

        /// <summary>
        /// 執行期（非 Editor）改變整條路口清單，並從第一個路口重新開始追蹤。
        /// waypoints/waypointInfos 兩個欄位是 private，Editor 工具用 SerializedObject 寫得進去，
        /// 但 Play 模式中沒有 SerializedObject 可用（例如外部系統即時規劃出新路線、想要餵給這個
        /// 元件時），所以額外開一個一般的公開方法，走跟 Inspector 拉線同樣的資料、只是換個入口。
        /// </summary>
        public void SetRoute(List<Vector3> newWaypoints, List<WaypointInfo> newWaypointInfos)
        {
            waypoints = newWaypoints ?? new List<Vector3>();
            waypointInfos = newWaypointInfos ?? new List<WaypointInfo>();
            CurrentWaypointIndex = 0;
            routePath = new List<Vector3>();
            _routeStartIndex = 0;
            RedrawLine();
        }

        /// <summary>取得目前目標路口的世界座標。</summary>
        public Vector3 GetCurrentWaypointPosition()
        {
            if (HasReachedDestination)
            {
                return player != null ? player.position : Vector3.zero;
            }

            return waypoints[CurrentWaypointIndex];
        }

        /// <summary>取得目前目標路口對應的轉彎資訊（轉彎圖示 + 路名）。</summary>
        public WaypointInfo GetCurrentWaypointInfo()
        {
            if (HasReachedDestination || CurrentWaypointIndex >= waypointInfos.Count)
            {
                return null;
            }

            return waypointInfos[CurrentWaypointIndex];
        }

        /// <summary>切換目前要跟隨/判斷距離的玩家（載具）Transform，供多車種切換時重新指定使用。</summary>
        public void SetPlayer(Transform newPlayer)
        {
            player = newPlayer;
        }

        /// <summary>計算指定位置與目前目標路口的即時 3D 距離。</summary>
        public float GetDistanceToCurrentWaypoint(Vector3 fromPosition)
        {
            if (HasReachedDestination)
            {
                return 0f;
            }

            return Vector3.Distance(fromPosition, waypoints[CurrentWaypointIndex]);
        }

        /// <summary>
        /// 計算從指定位置到終點的剩餘路線總距離：目前位置到下一個路口的距離，
        /// 加上剩餘所有路口之間依序累加的距離。用於 Google Maps 風格的預估到達時間顯示。
        /// </summary>
        public float GetRemainingRouteDistance(Vector3 fromPosition)
        {
            if (HasReachedDestination)
            {
                return 0f;
            }

            float total = Vector3.Distance(fromPosition, waypoints[CurrentWaypointIndex]);
            for (int i = CurrentWaypointIndex; i < waypoints.Count - 1; i++)
            {
                total += Vector3.Distance(waypoints[i], waypoints[i + 1]);
            }

            return total;
        }

        /// <summary>
        /// 在 Scene 視窗以視覺化方式顯示所有路口位置與連線，方便美術/關卡設計人員在編輯器內擺放座標。
        /// </summary>
        private void OnDrawGizmos()
        {
            if (waypoints == null || waypoints.Count == 0)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            for (int i = 0; i < waypoints.Count; i++)
            {
                Gizmos.DrawSphere(waypoints[i], 1f);

                if (i < waypoints.Count - 1)
                {
                    Gizmos.DrawLine(waypoints[i], waypoints[i + 1]);
                }
            }
        }
    }
}
