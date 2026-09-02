using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Navigation
{
    /// <summary>
    /// Google 風格的「轉彎指引卡片」UI 控制器。
    /// 負責：
    /// 1. 即時計算玩家與下一個路口的 3D 距離。
    /// 2. 距離小於門檻值時顯示卡片，並更新剩餘距離文字。
    /// 3. 依照 NavigationLineManager 目前的目標路口，切換轉彎圖示與路名文字。
    /// </summary>
    public class NavigationUIManager : MonoBehaviour
    {
        [Header("資料來源")]
        [Tooltip("導航路徑管理器，提供目前目標路口的座標與轉彎資訊")]
        [SerializeField] private NavigationLineManager lineManager;

        [Tooltip("玩家（載具）Transform，用於計算即時距離")]
        [SerializeField] private Transform player;

        [Header("UI 元件參照")]
        [Tooltip("整張指引卡片的根節點，用於控制顯示/隱藏")]
        [SerializeField] private GameObject navigationCard;

        [Tooltip("轉彎圖示 Image 元件")]
        [SerializeField] private Image turnIconImage;

        [Tooltip("剩餘距離文字，例如：100 m")]
        [SerializeField] private TextMeshProUGUI distanceText;

        [Tooltip("路名提示文字，例如：右轉 基隆路一段")]
        [SerializeField] private TextMeshProUGUI roadNameText;

        [Header("轉彎圖示 Sprite")]
        [SerializeField] private Sprite straightIcon;
        [SerializeField] private Sprite turnLeftIcon;
        [SerializeField] private Sprite turnRightIcon;
        [SerializeField] private Sprite uTurnIcon;

        [Header("顯示門檻")]
        [Tooltip("玩家與路口距離小於此值時，開始顯示導航卡片（公尺）")]
        [SerializeField] private float showCardDistance = 150f;

        /// <summary>切換目前要追蹤距離的玩家（載具）Transform，供多車種切換時重新指定使用。</summary>
        public void SetPlayer(Transform newPlayer)
        {
            player = newPlayer;
        }

        private void Update()
        {
            if (lineManager == null || player == null)
            {
                return;
            }

            if (lineManager.HasReachedDestination)
            {
                SetCardVisible(false);
                return;
            }

            float distance = lineManager.GetDistanceToCurrentWaypoint(player.position);

            if (distance <= showCardDistance)
            {
                SetCardVisible(true);
                UpdateCardContent(distance);
            }
            else
            {
                SetCardVisible(false);
            }
        }

        /// <summary>
        /// 依照目前距離與路口資訊更新卡片內容（圖示 / 距離 / 路名）。
        /// </summary>
        private void UpdateCardContent(float distance)
        {
            WaypointInfo info = lineManager.GetCurrentWaypointInfo();
            if (info == null)
            {
                return;
            }

            turnIconImage.sprite = GetIconForTurnType(info.turnType);
            distanceText.text = FormatDistance(distance);
            roadNameText.text = BuildRoadNameLabel(info.turnType, info.roadName);
        }

        /// <summary>依轉彎類型取得對應的圖示 Sprite。</summary>
        private Sprite GetIconForTurnType(TurnType turnType)
        {
            switch (turnType)
            {
                case TurnType.TurnLeft:
                    return turnLeftIcon;
                case TurnType.TurnRight:
                    return turnRightIcon;
                case TurnType.UTurn:
                    return uTurnIcon;
                case TurnType.Straight:
                default:
                    return straightIcon;
            }
        }

        /// <summary>將轉彎類型轉換為中文提示詞，組合成「右轉 基隆路一段」這類文字。</summary>
        private string BuildRoadNameLabel(TurnType turnType, string roadName)
        {
            string prefix;
            switch (turnType)
            {
                case TurnType.TurnLeft:
                    prefix = "左轉";
                    break;
                case TurnType.TurnRight:
                    prefix = "右轉";
                    break;
                case TurnType.UTurn:
                    prefix = "迴轉";
                    break;
                case TurnType.Straight:
                default:
                    prefix = "直行";
                    break;
            }

            return string.IsNullOrEmpty(roadName) ? prefix : $"{prefix} {roadName}";
        }

        /// <summary>
        /// 將公尺數格式化為 Google Maps 風格的距離文字。
        /// 超過 1000 公尺時改用公里並保留一位小數，例如「1.2 km」；否則四捨五入至整數公尺，例如「100 m」。
        /// </summary>
        private string FormatDistance(float distanceInMeters)
        {
            if (distanceInMeters >= 1000f)
            {
                float km = distanceInMeters / 1000f;
                return $"{km:F1} km";
            }

            int roundedMeters = Mathf.RoundToInt(distanceInMeters);
            return $"{roundedMeters} m";
        }

        /// <summary>
        /// 控制卡片顯示/隱藏，並避免每影格重複呼叫 SetActive 造成不必要的開銷。
        /// </summary>
        private void SetCardVisible(bool visible)
        {
            if (navigationCard != null && navigationCard.activeSelf != visible)
            {
                navigationCard.SetActive(visible);
            }
        }
    }
}
