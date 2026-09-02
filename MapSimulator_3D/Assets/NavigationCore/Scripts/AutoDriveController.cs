using UnityEngine;

namespace Navigation
{
    /// <summary>
    /// 自動沿導航路線行進的控制器，適用於「行前熟悉導航」這類不需要玩家手動操作、
    /// 讓車輛自己開過一遍路線的沉浸式預覽情境。
    ///
    /// 設計重點：NavigationLineManager 本身就會依「車輛（player）目前位置」持續判斷
    /// 是否已經抵達目前目標路口、並自動切換到下一個路口，所以這裡完全不需要重複維護
    /// 路口索引，只要每影格把車輛移動、轉向朝目前目標路口前進即可。
    /// </summary>
    public class AutoDriveController : MonoBehaviour
    {
        [Header("路徑資料來源")]
        [Tooltip("提供路口座標與目前導航目標的路徑管理器")]
        [SerializeField] private NavigationLineManager lineManager;

        [Header("行進參數")]
        [Tooltip("行進速度（公尺/秒）")]
        [SerializeField] private float moveSpeed = 8f;

        [Tooltip("轉向平滑速度，數值越大車頭轉向跟上目標方向的速度越快")]
        [SerializeField] private float rotationSmoothSpeed = 3f;

        [Header("貼合地形")]
        [Tooltip("是否讓高度跟著地形走。關掉的話車子會朝路口做 3D 直線內插，直接穿過中間的山坡")]
        [SerializeField] private bool followGround = true;

        [Tooltip("車輛原點高於地面的距離（公尺）")]
        [SerializeField] private float groundBuffer = 1.2f;

        [Tooltip("往下打射線的起點高度（相對車輛，公尺）")]
        [SerializeField] private float probeAbove = 8f;

        [Tooltip("往下打射線的長度（公尺）")]
        [SerializeField] private float probeDistance = 60f;

        [Tooltip("高度變化的平滑速度。太小會爬不上陡坡，太大過坎時會抖")]
        [SerializeField] private float heightSmoothSpeed = 8f;

        [Tooltip("建築圖磚物件。射線會忽略它——屋頂不是路面")]
        [SerializeField] private Transform buildingsTileset;

        [Tooltip("防墜用隱形地板的名稱，射線一律忽略")]
        [SerializeField] private string safetyFloorName = "NavigationSafetyFloor";

        private void Update()
        {
            if (lineManager == null || lineManager.HasReachedDestination)
            {
                return;
            }

            Vector3 targetPosition = lineManager.GetCurrentWaypointPosition();

            // ★ 只在水平面上朝目標移動，高度另外交給地形決定。
            //
            // 原本是直接對目標做 3D 內插（含高度）。那在平地沒問題，但這條路線沿著
            // 木柵路的山壁走，路口之間相距約 200 公尺、中間地形起伏好幾公尺——
            // 車子等於沿著兩個路口之間的直線飛過去，遇到隆起的地形就整台埋進去。
            // 實測跑完全程會被埋 12～22 次，最深 4.9 公尺。
            Vector3 position = transform.position;
            Vector3 flatTarget = new Vector3(targetPosition.x, position.y, targetPosition.z);
            position = Vector3.MoveTowards(position, flatTarget, moveSpeed * Time.deltaTime);

            if (followGround && TryFindGroundY(position, out float groundY))
            {
                // 平滑趨近而不是直接貼上，避免壓到路面接縫時上下彈跳。
                float desiredY = groundY + groundBuffer;
                position.y = Mathf.Lerp(position.y, desiredY, 1f - Mathf.Exp(-heightSmoothSpeed * Time.deltaTime));
            }

            transform.position = position;

            // 轉向只依水平方向（忽略高度差），避免爬坡/下坡時座標高度差讓車身出現不自然的抬頭或低頭。
            Vector3 flatDirection = targetPosition - transform.position;
            flatDirection.y = 0f;

            if (flatDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion desiredRotation = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationSmoothSpeed * Time.deltaTime);
            }
        }

        /// <summary>
        /// 量出指定位置下方的地面高度。起點只抬高 8 公尺是刻意的：抬太高會打到山洞上方的
        /// 山體或天橋，車子就被拉到那上面去（這個坑在 VehicleTerrainSnap 已經踩過一次）。
        /// </summary>
        private bool TryFindGroundY(Vector3 position, out float groundY)
        {
            groundY = 0f;

            Vector3 origin = position + Vector3.up * probeAbove;
            RaycastHit[] hits = Physics.RaycastAll(
                origin, Vector3.down, probeAbove + probeDistance, ~0, QueryTriggerInteraction.Ignore);

            bool found = false;
            float highest = float.MinValue;

            foreach (RaycastHit hit in hits)
            {
                if (hit.transform.IsChildOf(transform) ||
                    hit.transform.name.Trim() == safetyFloorName ||
                    (buildingsTileset != null && hit.transform.IsChildOf(buildingsTileset)))
                {
                    continue;
                }

                if (hit.point.y > highest)
                {
                    highest = hit.point.y;
                    found = true;
                }
            }

            if (found)
            {
                groundY = highest;
            }

            return found;
        }
    }
}
