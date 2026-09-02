using UnityEngine;

namespace Navigation
{
    /// <summary>
    /// 讓地圖標籤永遠正對攝影機，並在太遠時自動隱藏。
    ///
    /// 世界空間的文字如果不做這件事，從導航視角看過去會是斜的、甚至完全側面看不到。
    /// 距離隱藏則是為了避免遠處幾十個標籤糊成一團——真正的導航地圖也只顯示附近的 POI。
    /// </summary>
    [DisallowMultipleComponent]
    public class MapLabelBillboard : MonoBehaviour
    {
        [Tooltip("超過這個距離就隱藏標籤（公尺）")]
        [SerializeField] private float maxVisibleDistance = 220f;

        [Tooltip("小於這個距離也隱藏，避免車子開到標籤正下方時它糊在畫面上")]
        [SerializeField] private float minVisibleDistance = 8f;

        [Tooltip("是否隨距離放大，讓遠處的標籤維持大致相同的螢幕大小")]
        [SerializeField] private bool scaleWithDistance = true;

        [Tooltip("距離縮放的基準距離（公尺）。在這個距離時標籤是原始大小")]
        [SerializeField] private float referenceDistance = 90f;

        [Tooltip("縮放倍率的上下限")]
        [SerializeField] private Vector2 scaleClamp = new Vector2(0.55f, 2.2f);

        private Renderer[] _renderers;
        private Vector3 _baseScale;
        private Camera _camera;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _baseScale = transform.localScale;
        }

        private void LateUpdate()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                {
                    return;
                }
            }

            float distance = Vector3.Distance(_camera.transform.position, transform.position);
            bool visible = distance <= maxVisibleDistance && distance >= minVisibleDistance;

            foreach (Renderer renderer in _renderers)
            {
                if (renderer != null && renderer.enabled != visible)
                {
                    renderer.enabled = visible;
                }
            }

            if (!visible)
            {
                return;
            }

            // 只繞 Y 軸轉向攝影機。完全對齊攝影機的話，俯視時標籤會躺平變成一條線。
            Vector3 toCamera = _camera.transform.position - transform.position;
            toCamera.y = 0f;

            if (toCamera.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }

            if (scaleWithDistance)
            {
                float factor = Mathf.Clamp(distance / referenceDistance, scaleClamp.x, scaleClamp.y);
                transform.localScale = _baseScale * factor;
            }
        }
    }
}
