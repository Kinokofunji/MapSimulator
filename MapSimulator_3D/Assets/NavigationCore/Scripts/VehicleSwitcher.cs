using UnityEngine;

namespace Navigation
{
    /// <summary>
    /// 管理多種載具（例如汽車／機車）之間的切換，對應畢業專題報告「多車種視角切換：
    /// 內建汽車、機車等多種交通工具，一鍵切換對應的駕駛視角與物理特性」的需求。
    ///
    /// 同一時間只會有一台載具是啟用中的（其餘停用，物理與腳本都不會運算）。
    /// 切換時會把新載具移動到舊載具目前的位置/朝向與速度，並重新指定攝影機、
    /// 導航路徑管理器、導航 UI 要跟隨/追蹤的目標，讓切換看起來是無縫接續。
    /// </summary>
    public class VehicleSwitcher : MonoBehaviour
    {
        [Header("可切換的載具（依序排列，例如：汽車、機車）")]
        [SerializeField] private Transform[] vehicles;

        [Header("需要一併更新跟隨/追蹤目標的系統")]
        [SerializeField] private GoogleMapCamera mapCamera;
        [SerializeField] private NavigationLineManager lineManager;
        [SerializeField] private NavigationUIManager uiManager;
        [SerializeField] private SpeedometerUI speedometer;
        [SerializeField] private ETADisplayUI etaDisplay;

        [Tooltip("切換載具用的按鍵")]
        [SerializeField] private KeyCode switchKey = KeyCode.V;

        private int _currentIndex;

        private void Start()
        {
            if (vehicles == null || vehicles.Length == 0)
            {
                Debug.LogWarning("[VehicleSwitcher] 尚未設定任何載具，請在 Inspector 的 Vehicles 清單裡加入至少一台。");
                return;
            }

            for (int i = 0; i < vehicles.Length; i++)
            {
                if (vehicles[i] != null)
                {
                    vehicles[i].gameObject.SetActive(i == _currentIndex);
                }
            }

            ApplyTargets();
        }

        private void Update()
        {
            if (vehicles == null || vehicles.Length < 2)
            {
                return;
            }

            if (Input.GetKeyDown(switchKey))
            {
                SwitchToNextVehicle();
            }
        }

        /// <summary>切換到清單中的下一台載具（循環）。</summary>
        private void SwitchToNextVehicle()
        {
            Transform current = vehicles[_currentIndex];
            int nextIndex = (_currentIndex + 1) % vehicles.Length;
            Transform next = vehicles[nextIndex];

            if (current == null || next == null)
            {
                return;
            }

            // 把新載具放到舊載具目前的位置/朝向，讓切換看起來是無縫接續
            next.position = current.position;
            next.rotation = current.rotation;

            Rigidbody currentRigidbody = current.GetComponent<Rigidbody>();
            Rigidbody nextRigidbody = next.GetComponent<Rigidbody>();
            if (currentRigidbody != null && nextRigidbody != null)
            {
                nextRigidbody.velocity = currentRigidbody.velocity;
                nextRigidbody.angularVelocity = currentRigidbody.angularVelocity;
            }

            current.gameObject.SetActive(false);
            next.gameObject.SetActive(true);

            _currentIndex = nextIndex;
            ApplyTargets();
        }

        /// <summary>把攝影機、導航路徑管理器、導航 UI 的跟隨/追蹤目標指向目前啟用中的載具。</summary>
        private void ApplyTargets()
        {
            Transform activeVehicle = vehicles[_currentIndex];
            if (activeVehicle == null)
            {
                return;
            }

            if (mapCamera != null)
            {
                mapCamera.SetTarget(activeVehicle);
            }

            if (lineManager != null)
            {
                lineManager.SetPlayer(activeVehicle);
            }

            if (uiManager != null)
            {
                uiManager.SetPlayer(activeVehicle);
            }

            Rigidbody activeRigidbody = activeVehicle.GetComponent<Rigidbody>();

            if (speedometer != null)
            {
                speedometer.SetTarget(activeRigidbody);
            }

            if (etaDisplay != null)
            {
                etaDisplay.SetTarget(activeVehicle, activeRigidbody);
            }
        }
    }
}
