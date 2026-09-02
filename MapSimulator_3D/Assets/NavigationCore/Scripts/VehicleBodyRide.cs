using UnityEngine;

namespace Navigation
{
    /// <summary>
    /// 讓車身視覺跟著四顆輪胎的懸吊起伏走：平均高度決定車身高度，前後高度差做出俯仰
    /// （煞車點頭、加速抬頭），左右高度差做出側傾（過彎壓車）。
    ///
    /// 除了好看之外，這個元件還解決一個很實際的問題：車身該擺在多高，取決於懸吊在
    /// 靜止時實際被壓縮多少（跟車重、彈簧係數、targetPosition 都有關），用算的很容易差個
    /// 幾公分，車子就會看起來浮空或陷進地裡。直接讀輪胎當下的實際位置就完全不用猜。
    ///
    /// 注意 PlayerVehicle 的 Rigidbody 鎖了 X/Z 軸旋轉（防止在密集建築中翻車），所以車體
    /// 本身永遠不會傾斜——這裡做的俯仰與側傾是畫面上唯一的動態，關掉會明顯變呆板。
    /// </summary>
    [DisallowMultipleComponent]
    public class VehicleBodyRide : MonoBehaviour
    {
        [Header("車身視覺（會被移動的物件）")]
        [SerializeField] private Transform body;

        [Header("四顆輪胎的視覺模型")]
        [SerializeField] private Transform frontLeftWheel;
        [SerializeField] private Transform frontRightWheel;
        [SerializeField] private Transform rearLeftWheel;
        [SerializeField] private Transform rearRightWheel;

        [Header("姿態參數")]
        [Tooltip("車身底盤高於輪軸中心的距離（公尺）")]
        [SerializeField] private float rideHeightAboveAxle = 0.06f;

        [Tooltip("俯仰角上限（度），避免懸吊瞬間壓到底時車身翻過頭")]
        [SerializeField] private float maxPitchDegrees = 3.5f;

        [Tooltip("側傾角上限（度）")]
        [SerializeField] private float maxRollDegrees = 3f;

        [Tooltip("姿態平滑速度，越大反應越即時")]
        [SerializeField] private float smoothSpeed = 10f;

        [Header("貼合地面坡度")]
        [Tooltip("是否讓車身跟著地面坡度傾斜。關掉的話車身在斜坡上會保持水平，看起來一頭埋進地裡")]
        [SerializeField] private bool alignToGroundSlope = true;

        [Tooltip("取樣地面坡度時，前後取樣點的間距（公尺），通常設成軸距")]
        [SerializeField] private float slopeSampleLength = 2.6f;

        [Tooltip("取樣地面坡度時，左右取樣點的間距（公尺），通常設成輪距")]
        [SerializeField] private float slopeSampleWidth = 1.6f;

        [Tooltip("地面坡度造成的傾斜角上限（度）")]
        [SerializeField] private float maxSlopeDegrees = 22f;

        [Tooltip("建築圖磚物件。射線會忽略它——屋頂不是路面")]
        [SerializeField] private Transform buildingsTileset;

        [Tooltip("防墜用隱形地板的名稱，射線一律忽略")]
        [SerializeField] private string safetyFloorName = "NavigationSafetyFloor";

        private float _currentY;
        private float _currentPitch;
        private float _currentRoll;
        private bool _initialised;

        private void LateUpdate()
        {
            if (body == null ||
                frontLeftWheel == null || frontRightWheel == null ||
                rearLeftWheel == null || rearRightWheel == null)
            {
                return;
            }

            float fl = frontLeftWheel.localPosition.y;
            float fr = frontRightWheel.localPosition.y;
            float rl = rearLeftWheel.localPosition.y;
            float rr = rearRightWheel.localPosition.y;

            float frontAverage = (fl + fr) * 0.5f;
            float rearAverage = (rl + rr) * 0.5f;
            float leftAverage = (fl + rl) * 0.5f;
            float rightAverage = (fr + rr) * 0.5f;

            float targetY = (frontAverage + rearAverage) * 0.5f + rideHeightAboveAxle;

            // 前輪比後輪低 = 車頭下沉 = 繞 +X 軸旋轉（Unity 的 +X 旋轉就是低頭）。
            float wheelBase = Mathf.Max(0.1f, Mathf.Abs(frontLeftWheel.localPosition.z - rearLeftWheel.localPosition.z));
            float targetPitch = Mathf.Clamp(
                Mathf.Atan2(rearAverage - frontAverage, wheelBase) * Mathf.Rad2Deg,
                -maxPitchDegrees, maxPitchDegrees);

            // 繞 +Z 軸旋轉會把車子的右側抬高，所以要用「右高於左」當作正向。
            float track = Mathf.Max(0.1f, Mathf.Abs(frontLeftWheel.localPosition.x - frontRightWheel.localPosition.x));
            float targetRoll = Mathf.Clamp(
                Mathf.Atan2(rightAverage - leftAverage, track) * Mathf.Rad2Deg,
                -maxRollDegrees, maxRollDegrees);

            // ★ 車體的 Rigidbody 鎖了 X/Z 軸旋轉（防止在密集建築中翻車），所以車體本身
            // 永遠是水平的。光靠懸吊行程（只有 30 公分）根本表現不出路面坡度——上坡時
            // 懸吊早就壓到底，車身還是平的，看起來就是一頭埋進地裡、一頭翹起來。
            // 所以坡度要直接去量地面，再疊加到懸吊造成的姿態上。
            if (alignToGroundSlope)
            {
                if (TrySampleGroundSlope(out float slopePitch, out float slopeRoll))
                {
                    targetPitch = Mathf.Clamp(targetPitch + slopePitch, -maxSlopeDegrees, maxSlopeDegrees);
                    targetRoll = Mathf.Clamp(targetRoll + slopeRoll, -maxSlopeDegrees, maxSlopeDegrees);
                }
            }

            if (!_initialised)
            {
                // 第一影格直接就位，不然車身會從原點「飛」到定位。
                _currentY = targetY;
                _currentPitch = targetPitch;
                _currentRoll = targetRoll;
                _initialised = true;
            }
            else
            {
                float t = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);
                _currentY = Mathf.Lerp(_currentY, targetY, t);
                _currentPitch = Mathf.Lerp(_currentPitch, targetPitch, t);
                _currentRoll = Mathf.Lerp(_currentRoll, targetRoll, t);
            }

            Vector3 localPosition = body.localPosition;
            localPosition.y = _currentY;
            body.localPosition = localPosition;
            body.localRotation = Quaternion.Euler(_currentPitch, 0f, _currentRoll);
        }

        /// <summary>
        /// 在車輛的前／後／左／右四個點往下打射線量地面高度，換算成路面的俯仰與側傾角。
        /// 直接量地面而不是讀懸吊，才能表現出真實的坡度——景美這一帶有不少上下坡。
        /// </summary>
        private bool TrySampleGroundSlope(out float pitchDegrees, out float rollDegrees)
        {
            pitchDegrees = 0f;
            rollDegrees = 0f;

            float halfLength = slopeSampleLength * 0.5f;
            float halfWidth = slopeSampleWidth * 0.5f;

            Vector3 forward = transform.forward;
            Vector3 right = transform.right;
            Vector3 origin = transform.position;

            if (!TryGroundHeight(origin + forward * halfLength, out float front) ||
                !TryGroundHeight(origin - forward * halfLength, out float rear) ||
                !TryGroundHeight(origin + right * halfWidth, out float rightHeight) ||
                !TryGroundHeight(origin - right * halfWidth, out float leftHeight))
            {
                return false;
            }

            pitchDegrees = Mathf.Atan2(rear - front, slopeSampleLength) * Mathf.Rad2Deg;
            rollDegrees = Mathf.Atan2(rightHeight - leftHeight, slopeSampleWidth) * Mathf.Rad2Deg;
            return true;
        }

        private bool TryGroundHeight(Vector3 worldPosition, out float height)
        {
            height = 0f;

            Vector3 origin = worldPosition + Vector3.up * 4f;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);

            bool found = false;
            float highest = float.MinValue;

            foreach (RaycastHit hit in hits)
            {
                if (hit.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (hit.transform.name.Trim() == safetyFloorName)
                {
                    continue;
                }

                if (buildingsTileset != null && hit.transform.IsChildOf(buildingsTileset))
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
                height = highest;
            }

            return found;
        }
    }
}
