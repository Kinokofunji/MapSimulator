using TMPro;
using UnityEngine;

namespace Navigation
{
    /// <summary>
    /// 簡易儀表板：即時顯示目前啟用中載具的車速（km/h）。
    /// 對應報告系統邏輯架構「展示層（User Interface Layer）...呈現 3D 渲染畫面、儀表板與選單介面」的需求。
    /// </summary>
    public class SpeedometerUI : MonoBehaviour
    {
        [Tooltip("目前啟用中的載具 Rigidbody，多車種切換時可用 SetTarget() 重新指定")]
        [SerializeField] private Rigidbody targetRigidbody;

        [Tooltip("顯示車速的文字元件，例如：42 km/h")]
        [SerializeField] private TextMeshProUGUI speedText;

        private void Update()
        {
            if (targetRigidbody == null || speedText == null)
            {
                return;
            }

            float speedKmh = targetRigidbody.velocity.magnitude * 3.6f;
            speedText.text = $"{Mathf.RoundToInt(speedKmh)} km/h";
        }

        /// <summary>多車種切換時，重新指定目前要顯示車速的載具 Rigidbody。</summary>
        public void SetTarget(Rigidbody newTarget)
        {
            targetRigidbody = newTarget;
        }
    }
}
