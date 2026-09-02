using System;
using TMPro;
using UnityEngine;

namespace Navigation
{
    /// <summary>
    /// Google Maps 風格的「預估到達時間」資訊列：顯示預計抵達時間、剩餘時間、剩餘距離，
    /// 對應報告與 Google Maps 導航畫面底部常見的行程摘要列。
    /// </summary>
    public class ETADisplayUI : MonoBehaviour
    {
        [Header("資料來源")]
        [SerializeField] private NavigationLineManager lineManager;
        [SerializeField] private Transform player;
        [SerializeField] private Rigidbody playerRigidbody;

        [Header("UI 元件")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TextMeshProUGUI arrivalTimeText;
        [SerializeField] private TextMeshProUGUI durationText;
        [SerializeField] private TextMeshProUGUI distanceText;

        [Tooltip("車輛靜止或速度過低時，估算 ETA 用的預設速度（km/h），避免除以零或估算時間過長")]
        [SerializeField] private float minimumSpeedForEta = 20f;

        private void Update()
        {
            if (lineManager == null || player == null || panelRoot == null)
            {
                return;
            }

            if (lineManager.HasReachedDestination)
            {
                panelRoot.SetActive(false);
                return;
            }

            panelRoot.SetActive(true);

            float remainingDistance = lineManager.GetRemainingRouteDistance(player.position);
            float speedKmh = playerRigidbody != null ? playerRigidbody.velocity.magnitude * 3.6f : 0f;
            float effectiveSpeedKmh = Mathf.Max(speedKmh, minimumSpeedForEta);

            float hours = (remainingDistance / 1000f) / effectiveSpeedKmh;
            TimeSpan duration = TimeSpan.FromHours(hours);
            DateTime arrival = DateTime.Now + duration;

            if (arrivalTimeText != null)
            {
                arrivalTimeText.text = arrival.ToString("HH:mm");
            }

            if (durationText != null)
            {
                durationText.text = FormatDuration(duration);
            }

            if (distanceText != null)
            {
                distanceText.text = FormatDistance(remainingDistance);
            }
        }

        /// <summary>切換目前要追蹤距離/速度的載具，供多車種切換時重新指定使用。</summary>
        public void SetTarget(Transform newPlayer, Rigidbody newRigidbody)
        {
            player = newPlayer;
            playerRigidbody = newRigidbody;
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
            {
                return $"{(int)duration.TotalHours} hr {duration.Minutes} min";
            }

            return $"{Mathf.Max(1, duration.Minutes)} min";
        }

        private static string FormatDistance(float meters)
        {
            if (meters >= 1000f)
            {
                return $"{(meters / 1000f):F1} km";
            }

            return $"{Mathf.RoundToInt(meters)} m";
        }
    }
}
