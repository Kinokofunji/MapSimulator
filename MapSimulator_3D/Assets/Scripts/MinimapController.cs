using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 小地圖控制器：
/// 1. 讓一台俯視 (Orthographic) 攝影機即時跟隨車輛，畫面呈現在畫面角落的 RawImage 上。
/// 2. 在小地圖上顯示玩家朝向箭頭、以及目前導航目的地的位置標記。
/// 3. 允許玩家點擊小地圖上的任一位置，把該處設為新的導航目的地。
///
/// 注意：目前專案裡沒有建置實際的道路節點圖 (road graph)，
/// 所以點選目的地後產生的是「車輛目前位置 -> 目的地」的直線導航，
/// 不會沿著實際道路自動避開建築物繞路。若要做到真正沿路網規劃路線，
/// 需要額外建置道路節點資料（例如專案裡已匯入但尚未使用的 Barmetler RoadSystem 套件）。
/// </summary>
[RequireComponent(typeof(RawImage))]
public class MinimapController : MonoBehaviour, IPointerClickHandler
{
    [Header("跟隨目標")]
    public Transform player;

    [Header("小地圖攝影機")]
    public Camera minimapCamera;

    [Header("玩家方向箭頭 (小地圖中心)")]
    public RectTransform playerArrow;

    [Header("導航目的地標記")]
    public RectTransform destinationMarker;

    [Header("導航資料來源")]
    public NavigationLineManager lineManager;

    [Header("小地圖攝影機高度")]
    [Tooltip("小地圖攝影機在車輛正上方多高的地方俯視")]
    public float cameraHeight = 300f;

    [Header("點擊地面偵測")]
    [Tooltip("點擊小地圖時，用來偵測該處實際地面高度的 Layer")]
    public LayerMask groundLayerMask = ~0;

    [Tooltip("往下打地面偵測射線的最大距離；找不到地面時退回使用玩家目前高度")]
    public float raycastDistance = 1000f;

    private RawImage rawImage;
    private RectTransform rawImageRect;

    void Awake()
    {
        rawImage = GetComponent<RawImage>();
        rawImageRect = GetComponent<RectTransform>();

        if (minimapCamera != null)
        {
            // 小地圖固定「北方朝上」，只跟著車輛平移，不隨車輛旋轉
            minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        if (destinationMarker != null)
        {
            destinationMarker.gameObject.SetActive(false);
        }
    }

    void LateUpdate()
    {
        if (player == null || minimapCamera == null) return;

        Vector3 camPos = player.position;
        camPos.y = player.position.y + cameraHeight;
        minimapCamera.transform.position = camPos;

        if (playerArrow != null)
        {
            playerArrow.localEulerAngles = new Vector3(0f, 0f, -player.eulerAngles.y);
        }

        UpdateDestinationMarker();
    }

    /// <summary>
    /// 把目前導航路線的終點投影到小地圖上；超出小地圖範圍就隱藏標記。
    /// </summary>
    private void UpdateDestinationMarker()
    {
        if (destinationMarker == null || lineManager == null) return;

        if (lineManager.waypoints == null || lineManager.waypoints.Count == 0 || lineManager.IsDestinationReached)
        {
            destinationMarker.gameObject.SetActive(false);
            return;
        }

        Vector3 finalWaypoint = lineManager.waypoints[lineManager.waypoints.Count - 1].position;
        Vector2 localPos = WorldToMinimapLocal(finalWaypoint);

        float halfWidth = rawImageRect.rect.width / 2f;
        float halfHeight = rawImageRect.rect.height / 2f;
        bool withinRange = Mathf.Abs(localPos.x) <= halfWidth && Mathf.Abs(localPos.y) <= halfHeight;

        destinationMarker.gameObject.SetActive(withinRange);
        if (withinRange)
        {
            destinationMarker.anchoredPosition = localPos;
        }
    }

    /// <summary>
    /// 世界座標 -> 小地圖 RawImage 上的本地座標（以玩家目前位置為中心）。
    /// </summary>
    private Vector2 WorldToMinimapLocal(Vector3 worldPosition)
    {
        Vector3 offset = worldPosition - player.position;
        float pixelsPerUnit = rawImageRect.rect.width / (minimapCamera.orthographicSize * 2f);

        // 攝影機是 (90,0,0) 的俯視角度：世界 X 對應小地圖本地 X，世界 Z 對應小地圖本地 Y
        return new Vector2(offset.x, offset.z) * pixelsPerUnit;
    }

    /// <summary>
    /// 點擊小地圖時，把螢幕座標換算回世界座標，並設定成新的導航目的地。
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (player == null || minimapCamera == null || lineManager == null) return;

        bool inside = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rawImageRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);

        if (!inside) return;

        float pixelsPerUnit = rawImageRect.rect.width / (minimapCamera.orthographicSize * 2f);
        Vector3 worldOffset = new Vector3(localPoint.x / pixelsPerUnit, 0f, localPoint.y / pixelsPerUnit);
        Vector3 clickedWorldXZ = player.position + worldOffset;

        float groundY = FindGroundHeight(clickedWorldXZ, player.position.y);
        Vector3 destination = new Vector3(clickedWorldXZ.x, groundY, clickedWorldXZ.z);

        SetDestination(destination);
    }

    /// <summary>
    /// 從高空往下打一條射線，找出該 XZ 位置的實際地面高度；找不到就退回玩家目前高度。
    /// </summary>
    private float FindGroundHeight(Vector3 worldXZ, float fallbackY)
    {
        Vector3 rayStart = new Vector3(worldXZ.x, fallbackY + cameraHeight, worldXZ.z);
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, raycastDistance, groundLayerMask))
        {
            return hit.point.y;
        }
        return fallbackY;
    }

    /// <summary>
    /// 設定新的導航目的地。目前只會產生「車輛 -> 目的地」的單一節點直線路徑。
    /// </summary>
    public void SetDestination(Vector3 destination)
    {
        if (lineManager == null) return;

        var route = new List<NavWaypoint>
        {
            new NavWaypoint
            {
                position = destination,
                turnType = TurnType.Straight,
                roadName = "目的地"
            }
        };

        lineManager.SetRoute(route);
    }
}
