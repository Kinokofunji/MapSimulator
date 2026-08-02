using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Google 風格「轉彎指引卡片」UI 管理。
/// 依據 NavigationLineManager 提供的目前節點資訊，動態計算距離、顯示/隱藏卡片，
/// 並在玩家通過路口節點時自動切換到下一個轉彎圖示與路名提示。
/// </summary>
public class NavigationUIManager : MonoBehaviour
{
    [Header("資料來源")]
    [Tooltip("負責管理路徑節點與判斷玩家目前走到哪個節點的腳本")]
    public NavigationLineManager lineManager;

    [Tooltip("玩家/載具的 Transform，用來計算與下一個節點的即時距離")]
    public Transform player;

    [Header("UI 卡片元件 (Inspector 拉線綁定)")]
    [Tooltip("整張指引卡片的容器物件，用來控制顯示/隱藏")]
    public GameObject turnCardPanel;

    [Tooltip("卡片上的轉彎圖示")]
    public Image turnIconImage;

    [Tooltip("顯示剩餘距離的文字，例如：100 m")]
    public TMP_Text distanceText;

    [Tooltip("顯示轉彎動作 + 路名的文字，例如：右轉 基隆路一段")]
    public TMP_Text roadNameText;

    [Header("轉彎圖示 Sprite 對應")]
    public Sprite iconStraight; // 直行圖示
    public Sprite iconTurnLeft; // 左轉圖示
    public Sprite iconTurnRight; // 右轉圖示
    public Sprite iconUTurn; // 迴轉圖示

    [Header("距離門檻設定")]
    [Tooltip("玩家與下一個路口距離小於此值時，才顯示指引卡片")]
    public float displayDistance = 150f;

    // 記錄上一次顯示的節點索引，避免每一影格都重複設定圖示/文字
    private int lastDisplayedIndex = -1;
    private bool loggedMissingReference = false;

    void Update()
    {
        if (lineManager == null || player == null)
        {
            if (!loggedMissingReference)
            {
                loggedMissingReference = true;
                Debug.LogWarning(
                    $"NavigationUIManager：Line Manager 或 Player 欄位是空的 (lineManager={(lineManager == null ? "null" : "OK")}, " +
                    $"player={(player == null ? "null" : "OK")})，卡片邏輯完全不會執行。");
            }
            return;
        }

        // 已經抵達終點，或沒有任何節點資料，就直接隱藏卡片
        if (lineManager.IsDestinationReached || lineManager.CurrentWaypoint == null)
        {
            SetCardVisible(false, lineManager.IsDestinationReached ? "已抵達終點" : "目前沒有任何路線節點 (waypoints 是空的)");
            return;
        }

        NavWaypoint currentWaypoint = lineManager.CurrentWaypoint;
        float distance = Vector3.Distance(player.position, currentWaypoint.position);

        if (distance <= displayDistance)
        {
            SetCardVisible(true, $"距離目前節點 {distance:0.0}m，在顯示門檻 {displayDistance}m 內");
            UpdateCardContent(currentWaypoint, distance);
        }
        else
        {
            SetCardVisible(false, $"距離目前節點 {distance:0.0}m，超過顯示門檻 {displayDistance}m");
            lastDisplayedIndex = -1; // 離開顯示範圍後重置，下次進入範圍會重新刷新內容
        }
    }

    /// <summary>
    /// 除錯用：不需要進入 Play 模式、也不必等車輛靠近，直接強制卡片顯示目前節點內容。
    /// 在 Inspector 裡對 NavigationUIManager 元件按右鍵，選擇這個選項即可測試。
    /// 若按下後圖示仍然空白，代表問題出在 Icon Straight / Icon Turn Left / Icon Turn Right / Icon U Turn 欄位沒有指定 Sprite。
    /// </summary>
    [ContextMenu("立即預覽卡片內容 (不需要 Play 模式)")]
    private void PreviewCardInEditor()
    {
        if (lineManager == null || lineManager.CurrentWaypoint == null)
        {
            Debug.LogWarning("NavigationUIManager：尚未指定 Line Manager，或 Waypoints 清單是空的，無法預覽。");
            return;
        }

        lastDisplayedIndex = -1; // 強制刷新，忽略「節點沒變就不更新」的判斷
        SetCardVisible(true);
        UpdateCardContent(lineManager.CurrentWaypoint, 0f);
    }

    /// <summary>
    /// 更新卡片上的距離文字，以及 (節點切換時) 圖示與路名文字。
    /// </summary>
    private void UpdateCardContent(NavWaypoint waypoint, float distance)
    {
        // 距離文字每影格都更新，才能即時反映玩家接近的過程
        if (distanceText != null)
        {
            distanceText.text = FormatDistance(distance);
        }

        // 圖示與路名只在「目前節點改變」時更新，避免不必要的重複賦值
        if (lineManager.CurrentWaypointIndex != lastDisplayedIndex)
        {
            lastDisplayedIndex = lineManager.CurrentWaypointIndex;

            // 找不到對應圖示 (Inspector 上的 Icon 欄位還沒指定 Sprite) 時，保留原本的圖，避免被清成空白
            Sprite icon = GetIconForTurnType(waypoint.turnType);
            if (turnIconImage != null && icon != null)
            {
                turnIconImage.sprite = icon;
            }

            if (roadNameText != null)
            {
                roadNameText.text = BuildInstructionText(waypoint);
            }
        }
    }

    /// <summary>
    /// 組合「轉彎動作 + 路名」的提示文字，例如：右轉 基隆路一段。
    /// </summary>
    private string BuildInstructionText(NavWaypoint waypoint)
    {
        string action = GetActionLabel(waypoint.turnType);

        if (string.IsNullOrEmpty(waypoint.roadName))
        {
            return action;
        }

        return $"{action} {waypoint.roadName}";
    }

    private string GetActionLabel(TurnType turnType)
    {
        switch (turnType)
        {
            case TurnType.TurnLeft: return "左轉";
            case TurnType.TurnRight: return "右轉";
            case TurnType.UTurn: return "迴轉";
            case TurnType.Straight:
            default: return "直行";
        }
    }

    private Sprite GetIconForTurnType(TurnType turnType)
    {
        switch (turnType)
        {
            case TurnType.TurnLeft: return iconTurnLeft;
            case TurnType.TurnRight: return iconTurnRight;
            case TurnType.UTurn: return iconUTurn;
            case TurnType.Straight:
            default: return iconStraight;
        }
    }

    /// <summary>
    /// 將公尺距離格式化為畫面顯示用的文字，例如：100 m。
    /// </summary>
    private string FormatDistance(float distanceInMeters)
    {
        int rounded = Mathf.Max(0, Mathf.RoundToInt(distanceInMeters));
        return $"{rounded} m";
    }

    private void SetCardVisible(bool visible, string reason = null)
    {
        if (turnCardPanel == null)
        {
            Debug.LogWarning("NavigationUIManager：Turn Card Panel 欄位是空的，卡片無法顯示/隱藏。");
            return;
        }

        if (turnCardPanel.activeSelf != visible)
        {
            // 用 LogWarning 而不是 Log，確保就算 Console 的 Info 訊息顯示開關被關掉，這則診斷訊息也一定看得到
            Debug.LogWarning($"NavigationUIManager：卡片切換為 {(visible ? "顯示" : "隱藏")}" + (reason != null ? $"（{reason}）" : ""));
            turnCardPanel.SetActive(visible);
        }
    }
}
