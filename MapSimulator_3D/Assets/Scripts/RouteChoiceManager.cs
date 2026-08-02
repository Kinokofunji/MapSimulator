using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 玩家在地圖上選定目的地後，用 RoadGridPathfinder 規劃出多條候選路線，
/// 分別用不同顏色的 LineRenderer 同時預覽，並提供 UI 按鈕讓玩家挑選其中一條，
/// 選好之後才正式套用到 NavigationLineManager、開始逐項導航。
/// </summary>
public class RouteChoiceManager : MonoBehaviour
{
    [Header("依賴元件")]
    public RoadGridPathfinder pathfinder;
    public NavigationLineManager lineManager;
    public Transform player;

    [Header("候選路線視覺化")]
    [Tooltip("最多同時規劃/顯示幾條候選路線")]
    public int maxRouteChoices = 3;

    [Tooltip("候選路線的顏色，依序對應路線 1、2、3...（路線 1 通常是最短路線）")]
    public Color[] routeColors =
    {
        new Color(0.15f, 0.45f, 0.95f, 1f),
        new Color(0.6f, 0.6f, 0.6f, 0.9f),
        new Color(0.6f, 0.35f, 0.8f, 0.9f)
    };

    [Tooltip("候選路線抬高的高度")]
    public float previewHeightOffset = 1.5f;

    [Header("候選路線點選 UI")]
    [Tooltip("包住路線選擇按鈕的面板，規劃出路線時顯示、選好後自動隱藏")]
    public GameObject routeChoicePanel;

    [Tooltip("每條候選路線各自的可點擊按鈕，數量要跟 maxRouteChoices 一致，依序對應路線 1、2、3...")]
    public List<Button> routeButtons = new List<Button>();

    private readonly List<LineRenderer> previewLines = new List<LineRenderer>();
    private List<List<Vector3>> currentRoutes = new List<List<Vector3>>();

    void Awake()
    {
        for (int i = 0; i < maxRouteChoices; i++)
        {
            previewLines.Add(CreatePreviewLine(i));
        }

        for (int i = 0; i < routeButtons.Count; i++)
        {
            int routeIndex = i; // 閉包要複製一份區域變數，避免所有按鈕都指向同一個 i
            if (routeButtons[i] != null)
            {
                routeButtons[i].onClick.AddListener(() => SelectRoute(routeIndex));
            }
        }

        HideChoices();
    }

    private LineRenderer CreatePreviewLine(int index)
    {
        GameObject obj = new GameObject($"RoutePreview_{index}");
        obj.transform.SetParent(transform, false);

        LineRenderer lr = obj.AddComponent<LineRenderer>();
        lr.startWidth = index == 0 ? 1.2f : 0.8f;
        lr.endWidth = lr.startWidth;
        lr.useWorldSpace = true;
        lr.positionCount = 0;
        lr.numCapVertices = 4;

        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = index < routeColors.Length ? routeColors[index] : Color.gray;
        lr.material = mat;

        return lr;
    }

    /// <summary>規劃到某個目的地的多條候選路線，並顯示出來讓玩家挑選。</summary>
    public void RequestRoutes(Vector3 destination)
    {
        if (pathfinder == null || player == null)
        {
            Debug.LogWarning("RouteChoiceManager：尚未指定 Pathfinder 或 Player，無法規劃路線。");
            return;
        }

        currentRoutes = pathfinder.FindMultipleRoutes(player.position, destination, maxRouteChoices);

        if (currentRoutes.Count == 0)
        {
            Debug.LogWarning("RouteChoiceManager：附近找不到可通行的道路方磚，無法規劃路線。");
            HideChoices();
            return;
        }

        for (int i = 0; i < previewLines.Count; i++)
        {
            if (i < currentRoutes.Count)
            {
                DrawPreview(previewLines[i], currentRoutes[i]);
            }
            else
            {
                previewLines[i].positionCount = 0;
            }
        }

        // 只有一條路線可選時，不需要打擾玩家選擇，直接套用
        if (currentRoutes.Count == 1)
        {
            SelectRoute(0);
            return;
        }

        ShowChoices(currentRoutes.Count);
    }

    private void DrawPreview(LineRenderer lr, List<Vector3> points)
    {
        lr.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 p = points[i];
            p.y += previewHeightOffset;
            lr.SetPosition(i, p);
        }
    }

    private void ShowChoices(int routeCount)
    {
        if (routeChoicePanel != null) routeChoicePanel.SetActive(true);

        for (int i = 0; i < routeButtons.Count; i++)
        {
            if (routeButtons[i] != null)
            {
                routeButtons[i].gameObject.SetActive(i < routeCount);
            }
        }
    }

    private void HideChoices()
    {
        if (routeChoicePanel != null) routeChoicePanel.SetActive(false);

        foreach (LineRenderer line in previewLines)
        {
            line.positionCount = 0;
        }
    }

    /// <summary>玩家選了第 routeIndex 條候選路線，正式套用成導航路線。</summary>
    public void SelectRoute(int routeIndex)
    {
        if (routeIndex < 0 || routeIndex >= currentRoutes.Count) return;
        if (lineManager == null) return;

        List<NavWaypoint> waypoints = ConvertToWaypoints(currentRoutes[routeIndex]);
        lineManager.SetRoute(waypoints);

        HideChoices();
    }

    /// <summary>
    /// 把網格路徑的座標序列轉成 NavWaypoint 清單，並依前後兩段方向的夾角自動判斷
    /// 每個節點該顯示左轉/右轉/直行/迴轉的圖示。第一個點是起點本身，不需要轉成節點。
    /// </summary>
    private List<NavWaypoint> ConvertToWaypoints(List<Vector3> points)
    {
        var result = new List<NavWaypoint>();

        for (int i = 1; i < points.Count; i++)
        {
            TurnType turn = TurnType.Straight;

            if (i < points.Count - 1)
            {
                Vector3 incoming = (points[i] - points[i - 1]).normalized;
                Vector3 outgoing = (points[i + 1] - points[i]).normalized;
                turn = ClassifyTurn(incoming, outgoing);
            }

            bool isLast = i == points.Count - 1;

            result.Add(new NavWaypoint
            {
                position = points[i],
                turnType = turn,
                roadName = isLast ? "目的地" : "路口"
            });
        }

        // 起點跟終點剛好落在同一個道路格子時，points 只有 1 個點，上面的迴圈不會跑，
        // 這裡保底把目的地本身加成一個節點，避免路線選了卻沒有任何節點、導致轉彎卡片永遠不會出現
        if (result.Count == 0 && points.Count > 0)
        {
            result.Add(new NavWaypoint
            {
                position = points[points.Count - 1],
                turnType = TurnType.Straight,
                roadName = "目的地"
            });
        }

        return result;
    }

    /// <summary>依前後兩段方向的夾角判斷轉彎類型（正負角度對應可能會因場景座標習慣而相反，實測後可自行調整）。</summary>
    private TurnType ClassifyTurn(Vector3 incoming, Vector3 outgoing)
    {
        float angle = Vector3.SignedAngle(incoming, outgoing, Vector3.up);

        if (angle > 135f || angle < -135f) return TurnType.UTurn;
        if (angle > 30f) return TurnType.TurnRight;
        if (angle < -30f) return TurnType.TurnLeft;
        return TurnType.Straight;
    }
}
