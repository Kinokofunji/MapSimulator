using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 轉彎方向類型，對應 Google 地圖導航常見的指引圖示。
/// </summary>
public enum TurnType
{
    Straight,   // 直行
    TurnLeft,   // 左轉
    TurnRight,  // 右轉
    UTurn       // 迴轉
}

/// <summary>
/// 單一導航路口節點的資料。
/// position 用來畫導航線與判斷玩家是否抵達，turnType / roadName 供 UI 卡片顯示使用。
/// </summary>
[Serializable]
public class NavWaypoint
{
    [Tooltip("路口在世界座標中的 3D 位置")]
    public Vector3 position;

    [Tooltip("到達這個路口時應該怎麼轉")]
    public TurnType turnType = TurnType.Straight;

    [Tooltip("路口/道路名稱，例如：基隆路一段")]
    public string roadName;
}

/// <summary>
/// 導航指引線與路徑管理。
/// 用 LineRenderer 在地面上畫出導航路線，並持續追蹤玩家目前走到哪一個路口節點。
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class NavigationLineManager : MonoBehaviour
{
    [Header("路徑節點設定")]
    [Tooltip("依序設定每個轉彎路口的 3D 座標與轉向資訊")]
    public List<NavWaypoint> waypoints = new List<NavWaypoint>();

    [Header("玩家/載具")]
    public Transform player;

    [Header("導航線外觀")]
    [Tooltip("導航線抬高的高度（公尺）。數值越大，線會浮在路面上方越高，視覺上越明顯")]
    public float lineHeightOffset = 1.5f;

    [Tooltip("勾選後，導航線只會顯示「目前節點到終點」的剩餘路徑；取消則顯示完整路徑")]
    public bool onlyShowRemainingPath = true;

    [Tooltip("勾選後，導航線的起點會固定接在玩家/車輛目前位置，並隨車輛移動即時延伸（Google 地圖風格）")]
    public bool startFromPlayer = true;

    [Header("到達判定")]
    [Tooltip("玩家與節點距離小於此值，視為已經抵達該路口節點")]
    public float arrivalThreshold = 5f;

    [Header("錯過路口自動重新導航")]
    [Tooltip("勾選後，如果玩家開過頭錯過目前的轉彎路口，會自動跳到最接近的後續路口，模擬 Google 導航即時重新規劃")]
    public bool autoRerouteOnMissedTurn = true;

    [Tooltip("玩家與目前路口距離超過這個值，且後面剛好有更近的路口時，視為已經錯過這個路口")]
    public float missedTurnDistance = 40f;

    // 目前玩家已經走到的路口節點索引 (0 = 第一個節點)
    public int CurrentWaypointIndex { get; private set; } = 0;

    // 是否已經抵達最後一個節點 (終點)
    public bool IsDestinationReached { get; private set; } = false;

    // 目前應該前往的節點；抵達終點後仍回傳最後一個節點
    public NavWaypoint CurrentWaypoint => waypoints.Count > 0 ? waypoints[CurrentWaypointIndex] : null;

    /// <summary>當玩家通過某個路口節點時觸發，參數為「新的」目前節點索引</summary>
    public event Action<int> OnWaypointReached;

    /// <summary>當玩家抵達最後一個節點 (終點) 時觸發一次</summary>
    public event Action OnDestinationReached;

    /// <summary>當偵測到玩家錯過路口、自動跳到新的節點時觸發，參數為「新的」目前節點索引</summary>
    public event Action<int> OnRouteRecalculated;

    private LineRenderer lineRenderer;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
    }

    void Start()
    {
        DrawLine();
    }

    void Update()
    {
        if (waypoints.Count == 0) return;

        if (player != null && !IsDestinationReached)
        {
            CheckWaypointProgress();

            if (autoRerouteOnMissedTurn && !IsDestinationReached)
            {
                CheckForMissedTurn();
            }
        }

        // 已經抵達終點，導航線就不再需要顯示
        if (IsDestinationReached)
        {
            HideLine();
            return;
        }

        // 車輛每影格都在移動，導航線起點需要跟著即時延伸，所以每影格都重畫
        if (startFromPlayer && player != null)
        {
            DrawLine();
        }
    }

    /// <summary>
    /// Inspector 修改 waypoints 或相關參數時（即使不在 Play 模式）也立刻重畫導航線，方便設計路線時預覽。
    /// </summary>
    void OnValidate()
    {
        if (Application.isPlaying) return;
        DrawLine();
    }

    /// <summary>
    /// 隱藏導航線（清空 LineRenderer 的頂點）。
    /// </summary>
    private void HideLine()
    {
        if (lineRenderer == null) lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer.positionCount != 0)
        {
            lineRenderer.positionCount = 0;
        }
    }

    /// <summary>
    /// 檢查玩家與目前節點的距離，若已抵達則推進到下一個節點。
    /// </summary>
    private void CheckWaypointProgress()
    {
        float distance = Vector3.Distance(player.position, CurrentWaypoint.position);

        if (distance <= arrivalThreshold)
        {
            bool isLastWaypoint = CurrentWaypointIndex >= waypoints.Count - 1;

            if (isLastWaypoint)
            {
                IsDestinationReached = true;
                OnDestinationReached?.Invoke();
            }
            else
            {
                CurrentWaypointIndex++;
                OnWaypointReached?.Invoke(CurrentWaypointIndex);

                // 動態模式下，剩餘路徑縮短了，需要重畫導航線
                if (onlyShowRemainingPath)
                {
                    DrawLine();
                }
            }
        }
    }

    /// <summary>
    /// 沒有真正的道路網資料可以判斷「走錯路」，所以用一個簡單但實用的近似法：
    /// 如果玩家離目前路口已經明顯變遠 (超過 missedTurnDistance)，
    /// 而後續某個路口反而比目前路口更近，就視為已經開過頭、錯過轉彎，
    /// 直接把目前節點跳到那個「玩家離它最近」的後續路口，並重新畫線，模擬即時重新導航。
    /// </summary>
    private void CheckForMissedTurn()
    {
        if (CurrentWaypointIndex >= waypoints.Count - 1) return; // 已經是最後一個節點，沒有下一個可以跳

        float distanceToCurrent = Vector3.Distance(player.position, CurrentWaypoint.position);
        if (distanceToCurrent <= missedTurnDistance) return; // 還沒明顯開過頭，不用處理

        int closestIndex = CurrentWaypointIndex;
        float closestDistance = distanceToCurrent;

        for (int i = CurrentWaypointIndex + 1; i < waypoints.Count; i++)
        {
            float distance = Vector3.Distance(player.position, waypoints[i].position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = i;
            }
        }

        if (closestIndex > CurrentWaypointIndex)
        {
            CurrentWaypointIndex = closestIndex;
            OnRouteRecalculated?.Invoke(CurrentWaypointIndex);
            DrawLine();
        }
    }

    /// <summary>
    /// 用一組新的路線節點取代目前的導航路線，並重新從第一個節點開始追蹤（例如玩家在地圖上點選了新的目的地）。
    /// </summary>
    public void SetRoute(List<NavWaypoint> newWaypoints)
    {
        waypoints = newWaypoints ?? new List<NavWaypoint>();
        CurrentWaypointIndex = 0;
        IsDestinationReached = false;
        DrawLine();
    }

    /// <summary>
    /// 保留目前的 waypoints 路線內容，只把「玩家已經走到哪個節點」的進度重置回第一個節點。
    /// 用於車輛被傳送回起點時（例如卡死重置、掉出地圖重置），避免導航進度跟車輛實際位置對不上。
    /// </summary>
    public void ResetProgress()
    {
        CurrentWaypointIndex = 0;
        IsDestinationReached = false;
        DrawLine();
    }

    /// <summary>
    /// 依目前設定重新繪製 LineRenderer 的頂點。
    /// 可在 Inspector 手動修改 waypoints 後於外部呼叫此方法即時更新導航線 (動態更新)。
    /// </summary>
    public void DrawLine()
    {
        if (lineRenderer == null) lineRenderer = GetComponent<LineRenderer>();
        if (waypoints.Count == 0)
        {
            lineRenderer.positionCount = 0;
            return;
        }

        int startIndex = onlyShowRemainingPath ? CurrentWaypointIndex : 0;
        int waypointCount = waypoints.Count - startIndex;

        // 是否要把玩家目前位置當作導航線的第一個頂點（讓線從車輛所在處開始延伸）
        bool prependPlayer = startFromPlayer && player != null;
        int totalCount = waypointCount + (prependPlayer ? 1 : 0);

        lineRenderer.positionCount = totalCount;

        int cursor = 0;

        if (prependPlayer)
        {
            Vector3 playerPoint = player.position;
            playerPoint.y += lineHeightOffset;
            lineRenderer.SetPosition(cursor, playerPoint);
            cursor++;
        }

        for (int i = 0; i < waypointCount; i++)
        {
            Vector3 point = waypoints[startIndex + i].position;
            point.y += lineHeightOffset;
            lineRenderer.SetPosition(cursor, point);
            cursor++;
        }
    }
}
