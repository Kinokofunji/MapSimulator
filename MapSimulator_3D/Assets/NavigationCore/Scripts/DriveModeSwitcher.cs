using System.Collections;
using UnityEngine;

namespace Navigation
{
    /// <summary>
    /// 在「手動物理駕駛」與「自動沿路線行進」兩種模式之間切換。
    /// 預設為手動模式（符合畢業專題報告的核心需求：WASD 操控的擬真物理車輛），
    /// 按下切換鍵可以改成自動模式，讓車輛自己開一遍路線，方便展示「行前路線預演」的用途。
    /// 兩種模式共用同一個 Rigidbody：自動模式時會把 Rigidbody 設為 Kinematic，
    /// 避免 AutoDriveController 直接搬移座標的方式跟物理引擎互相打架。
    ///
    /// 遊戲一開始時，會先把 Rigidbody 凍結（Kinematic）並持續向下打射線等待真實地形/建築物的
    /// 碰撞網格準備就緒（Cesium 的碰撞網格是非同步串流產生的，可能還沒生成好），
    /// 找到地面後才把車輛貼齊地面高度、恢復正常物理模擬，避免車輛一開始就掉進還沒生成好的
    /// 地形、或卡進地下而被物理引擎瞬間彈飛。
    /// </summary>
    public class DriveModeSwitcher : MonoBehaviour
    {
        [Header("兩種駕駛模式的控制器")]
        [SerializeField] private VehiclePhysicsController manualController;
        [SerializeField] private AutoDriveController autoController;
        [SerializeField] private Rigidbody vehicleRigidbody;

        [Header("切換設定")]
        [Tooltip("按下這個按鍵可以切換手動/自動模式")]
        [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

        [Tooltip("一開始是否為自動模式（預設關閉，即一開始為手動物理駕駛）")]
        [SerializeField] private bool startInAutoMode = false;

        [Header("等待地面就緒設定")]
        [Tooltip(
            "向下打射線尋找地面時，射線起點比目前位置高出多少公尺。" +
            "刻意設定成較小的範圍（而不是從很高的地方往下打），是因為車輛的起始高度" +
            "已經先用 Google Elevation API 校正過、本來就是合理值；範圍抓太大在密集的" +
            "照片級 3D 建築場景中，射線很容易先打到附近建築物的屋頂/雨遮，把車輛誤貼到" +
            "屋頂上而不是實際的路面，之後恢復物理模擬時就會從屋頂摔落或翻覆。")]
        [SerializeField] private float groundRaycastHeight = 15f;

        [Tooltip("向下打射線的最大偵測距離（公尺）")]
        [SerializeField] private float groundRaycastMaxDistance = 40f;

        [Tooltip("貼齊地面後，車身要留多少緩衝高度")]
        [SerializeField] private float groundSnapOffset = 0.5f;

        [Tooltip("最多等待幾秒；若超時仍找不到地面，就直接恢復物理模擬（讓安全地板接住）")]
        [SerializeField] private float groundWaitTimeoutSeconds = 8f;

        [Tooltip("開場是否自己找地面貼齊。場景若已有 VehicleTerrainSnap 就該關閉——兩套邏輯會互相打架，" +
                 "而且這裡用的射線是從上方 15 公尺打下來，在山洞或山壁旁會把車貼到山上去")]
        [SerializeField] private bool snapToGroundOnStart = true;

        private bool _isAutoMode;

        private void Start()
        {
            _isAutoMode = startInAutoMode;
            StartCoroutine(WaitForGroundThenApplyMode());
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                _isAutoMode = !_isAutoMode;
                ApplyMode();
            }
        }

        /// <summary>
        /// 先凍結 Rigidbody，持續向下打射線找地面；找到後把車輛貼齊地面高度再恢復正常模式，
        /// 避免地形碰撞網格還沒串流載入完成時就開始物理模擬。
        /// </summary>
        private IEnumerator WaitForGroundThenApplyMode()
        {
            if (vehicleRigidbody != null && snapToGroundOnStart)
            {
                vehicleRigidbody.isKinematic = true;

                float elapsed = 0f;
                while (elapsed < groundWaitTimeoutSeconds)
                {
                    Vector3 origin = transform.position + Vector3.up * groundRaycastHeight;
                    if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundRaycastMaxDistance))
                    {
                        Vector3 pos = transform.position;
                        pos.y = hit.point.y + groundSnapOffset;
                        transform.position = pos;
                        break;
                    }

                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }

            ApplyMode();
        }

        /// <summary>
        /// 讓外部（VehicleTerrainSnap 貼地完成後）重新套用目前的駕駛模式。
        ///
        /// 需要這個是因為兩個元件都會動 Rigidbody 的 isKinematic：自動模式必須是 kinematic
        /// （AutoDriveController 直接搬 Transform，物理會跟它打架），而貼地流程會先凍結、
        /// 完成後還原。如果貼地元件用「記下的原始值」還原，就會把自動模式需要的 kinematic
        /// 覆蓋掉——車子照樣會動，但速度被物理拖掉四分之三。
        /// </summary>
        public void ReapplyMode()
        {
            ApplyMode();
        }

        /// <summary>
        /// 從外部切換駕駛模式（自動化測試與 UI 按鈕用）。
        /// 參數用 float 是為了能透過 SendMessage 呼叫：大於 0.5 代表自動駕駛。
        /// </summary>
        public void SetAutoMode(float autoMode)
        {
            _isAutoMode = autoMode > 0.5f;
            ApplyMode();
        }

        private void ApplyMode()
        {
            if (manualController != null)
            {
                manualController.enabled = !_isAutoMode;
            }

            if (autoController != null)
            {
                autoController.enabled = _isAutoMode;
            }

            if (vehicleRigidbody != null)
            {
                // 自動模式下由 AutoDriveController 直接搬移 Transform，
                // 必須把 Rigidbody 切成 Kinematic，否則物理引擎的重力/碰撞會跟搬移動作互相干擾。
                vehicleRigidbody.isKinematic = _isAutoMode;
            }
        }
    }
}
