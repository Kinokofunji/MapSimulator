using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 小地圖 / 全景地圖控制器：
/// 1. 讓一台俯視 (Orthographic) 攝影機即時跟隨車輛，畫面呈現在畫面角落的 RawImage 小地圖上。
/// 2. 在小地圖上顯示玩家朝向箭頭、以及目前導航目的地的位置標記。
/// 3. 允許玩家點擊小地圖，把該處設為新的導航目的地；也可以按鍵展開「全景地圖」(同一台攝影機放大範圍、
///    佔滿螢幕)，在全景地圖上更精準地挑選目的地，選完會自動收合回小地圖。
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

    [Tooltip("有指定的話，選目的地會改成規劃多條候選路線讓玩家挑選；沒指定則退回單一直線導航")]
    public RouteChoiceManager routeChoiceManager;

    [Tooltip("有指定的話，展開全景地圖時會自動縮放/置中到剛好framing整座城市的範圍；沒指定則使用下面固定的 Full Map Zoom")]
    public RoadGridPathfinder pathfinder;

    [Header("小地圖攝影機高度")]
    [Tooltip("小地圖攝影機在車輛正上方多高的地方俯視")]
    public float cameraHeight = 300f;

    [Header("全景地圖")]
    [Tooltip("全景地圖的 RawImage 面板（平常隱藏，展開時佔滿畫面）")]
    public RectTransform fullMapPanel;

    [Tooltip("小地圖(局部特寫)使用的正交攝影機大小，數字越小放得越大")]
    public float minimapZoom = 60f;

    [Tooltip("全景地圖(城市總覽)使用的正交攝影機大小，數字越大看到的範圍越廣")]
    public float fullMapZoom = 400f;

    [Tooltip("展開/收合全景地圖的按鍵")]
    public KeyCode toggleFullMapKey = KeyCode.M;

    [Header("點擊地面偵測")]
    [Tooltip("點擊地圖時，用來偵測該處實際地面高度的 Layer")]
    public LayerMask groundLayerMask = ~0;

    [Tooltip("往下打地面偵測射線的最大距離；找不到地面時退回使用玩家目前高度")]
    public float raycastDistance = 1000f;

    public bool IsFullMapOpen { get; private set; } = false;

    private RawImage rawImage;
    private RectTransform rawImageRect;

    void Awake()
    {
        rawImage = GetComponent<RawImage>();
        rawImageRect = GetComponent<RectTransform>();

        if (minimapCamera != null)
        {
            // 地圖固定「北方朝上」，只跟著車輛平移，不隨車輛旋轉
            minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            minimapCamera.orthographicSize = minimapZoom;
        }

        if (destinationMarker != null)
        {
            destinationMarker.gameObject.SetActive(false);
        }

        if (fullMapPanel != null)
        {
            fullMapPanel.gameObject.SetActive(false);
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleFullMapKey))
        {
            ToggleFullMap();
        }
    }

    void LateUpdate()
    {
        if (player == null || minimapCamera == null) return;

        // 全景地圖展開時，攝影機已經在 ToggleFullMap() 裡固定對準城市範圍，
        // 小地圖本身的跟隨/箭頭/目的地標記也都不需要更新（而且全景地圖開著時 rawImage 是關閉的，
        // 這個物件本身仍然要保持 Active，才能讓下面的 Update() 繼續偵測 M 鍵，所以只跳過視覺更新，不跳過整個腳本）
        if (IsFullMapOpen) return;

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
    /// 展開/收合全景地圖。
    /// 展開時：如果有指定 pathfinder，攝影機會自動置中並縮放到剛好框住整座城市的道路範圍；
    /// 沒有指定的話，退回使用固定的 fullMapZoom，並以車輛目前位置為中心。
    /// 收合時：攝影機縮放還原成 minimapZoom，位置交回 LateUpdate 繼續跟隨車輛。
    /// </summary>
    public void ToggleFullMap()
    {
        IsFullMapOpen = !IsFullMapOpen;

        // 注意：這裡絕對不能對 rawImage.gameObject 呼叫 SetActive(false)——
        // MinimapController 跟 RawImage 是掛在同一個物件上 ([RequireComponent(typeof(RawImage))])，
        // 把該物件關掉會連這個腳本自己的 Update() 都一起停掉，導致 M 鍵再也偵測不到、無法重新收合。
        // 改成只關閉 RawImage 的渲染 (enabled)，物件本身維持 Active。
        if (rawImage != null)
        {
            rawImage.enabled = !IsFullMapOpen;
        }

        if (playerArrow != null)
        {
            playerArrow.gameObject.SetActive(!IsFullMapOpen);
        }

        if (destinationMarker != null && IsFullMapOpen)
        {
            destinationMarker.gameObject.SetActive(false);
        }

        if (fullMapPanel != null)
        {
            fullMapPanel.gameObject.SetActive(IsFullMapOpen);
        }

        if (minimapCamera == null) return;

        if (IsFullMapOpen)
        {
            float size = fullMapZoom;
            Vector3 center = player != null ? player.position : minimapCamera.transform.position;

            if (pathfinder != null && pathfinder.TryGetBoundsFitOrthographicSize(out Vector3 boundsCenter, out float fitSize))
            {
                center = boundsCenter;
                size = fitSize;
            }

            minimapCamera.orthographicSize = size;

            Vector3 camPos = center;
            camPos.y = (player != null ? player.position.y : 0f) + cameraHeight;
            minimapCamera.transform.position = camPos;
        }
        else
        {
            minimapCamera.orthographicSize = minimapZoom;
        }
    }

    /// <summary>目前攝影機實際俯視的中心點 (XZ)，小地圖模式下等於車輛位置，全景地圖模式下可能是城市中心。</summary>
    private Vector3 CameraCenterXZ()
    {
        Vector3 p = minimapCamera.transform.position;
        p.y = 0f;
        return p;
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
    /// 世界座標 -> 小地圖 RawImage 上的本地座標（以攝影機目前實際俯視的中心點為準，
    /// 不能直接假設是玩家位置——全景地圖展開時攝影機中心可能是城市範圍中心）。
    /// </summary>
    private Vector2 WorldToMinimapLocal(Vector3 worldPosition)
    {
        Vector3 offset = worldPosition - CameraCenterXZ();
        float pixelsPerUnit = rawImageRect.rect.width / (minimapCamera.orthographicSize * 2f);

        // 攝影機是 (90,0,0) 的俯視角度：世界 X 對應小地圖本地 X，世界 Z 對應小地圖本地 Y
        return new Vector2(offset.x, offset.z) * pixelsPerUnit;
    }

    /// <summary>
    /// 點擊小地圖本身時觸發（小地圖自己就是 IPointerClickHandler）。
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        HandleMapClick(rawImageRect, eventData);
    }

    /// <summary>
    /// 小地圖與全景地圖共用的點擊處理：把螢幕座標換算回世界座標，設定成新的導航目的地。
    /// sourceRect 傳入「實際被點擊的那個面板」的 RectTransform（小地圖或全景地圖，兩者尺寸不同，換算比例要分開算）。
    /// 全景地圖上點擊後會自動收合回小地圖，模擬 Google 地圖選完目的地就收起地圖、開始導航的流程。
    /// </summary>
    public void HandleMapClick(RectTransform sourceRect, PointerEventData eventData)
    {
        if (player == null || minimapCamera == null || lineManager == null || sourceRect == null) return;

        bool inside = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            sourceRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);

        if (!inside) return;

        float pixelsPerUnit = sourceRect.rect.width / (minimapCamera.orthographicSize * 2f);
        Vector3 worldOffset = new Vector3(localPoint.x / pixelsPerUnit, 0f, localPoint.y / pixelsPerUnit);
        Vector3 clickedWorldXZ = CameraCenterXZ() + worldOffset;

        float groundY = FindGroundHeight(clickedWorldXZ, player.position.y);
        Vector3 destination = new Vector3(clickedWorldXZ.x, groundY, clickedWorldXZ.z);

        SetDestination(destination);

        if (IsFullMapOpen)
        {
            ToggleFullMap();
        }
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
    /// 設定新的導航目的地。
    /// 如果有指定 RouteChoiceManager，會規劃多條沿實際道路網格走的候選路線讓玩家挑選；
    /// 沒有指定的話，退回成「車輛 -> 目的地」的單一節點直線路徑。
    /// </summary>
    public void SetDestination(Vector3 destination)
    {
        if (routeChoiceManager != null)
        {
            routeChoiceManager.RequestRoutes(destination);
            return;
        }

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
