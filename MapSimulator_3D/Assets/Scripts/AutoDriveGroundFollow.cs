using UnityEngine;

/// <summary>
/// 接手 Navigation.AutoDriveController 的貼地判斷（安裝時會關閉它自己的 followGround，
/// 不改她的原始碼）。
///
/// 她原本 TryFindGroundY 的邏輯是「射線打到的最高點當地面」——這是針對 Cesium 圖磚場景
/// 寫的（路徑常常沿著山壁走，需要忽略山壁選路面），但套到我們這種平面城市地圖時，
/// 只要路徑正上方剛好有任何裝飾物、招牌、拱門、柵欄，車輛就會被拉去貼合那個高度，
/// 變成飛越障礙物的怪異畫面。
///
/// 高度緩衝值不用猜：一開始沿用她的 AutoDriveController.groundBuffer 預設值 1.2 公尺，
/// 結果一進自動駕駛車輛就浮空——那個數字是針對她自己的車型（懸吊、輪徑都不同）調的，
/// 對我們這台車完全不適用。改成在「切換進自動駕駛的當下」直接量出車身目前高度跟正下方
/// 路面高度的差，記下來當這次自動駕駛全程要維持的高度差——那一刻車輛是手動物理模式下
/// 被 WheelCollider 懸吊撐在正確高度的，不需要猜任何車型相關的數字。
/// </summary>
public class AutoDriveGroundFollow : MonoBehaviour
{
    public Navigation.AutoDriveController autoDriveController;

    [Tooltip("往下打射線的起點在車輛上方多少公尺")]
    public float probeAbove = 3f;

    [Tooltip("往下打射線的長度（公尺）")]
    public float probeDistance = 15f;

    [Tooltip("高度變化的平滑速度。太小爬不上坡，太大會抖")]
    public float heightSmoothSpeed = 8f;

    [Tooltip("「離參考高度最近」的候選如果還是差了這麼多公尺，代表參考高度本身可能已經" +
             "不可靠，改用範圍內最低的表面，避免卡在錯誤高度回不去")]
    public float maxDeviationBeforeFallback = 1f;

    private bool _wasEnabled;
    private float _groundOffset;
    private bool _hasGroundOffset;

    void LateUpdate()
    {
        bool isEnabled = autoDriveController != null && autoDriveController.enabled;

        // 剛切換進自動駕駛的那一刻，補量一次「車身高度 - 正下方路面高度」，記下來當
        // 這次自動駕駛全程要維持的緩衝值。
        if (isEnabled && !_wasEnabled)
        {
            if (TryFindNearestGroundY(transform.position.y, out float initialGroundY))
            {
                _groundOffset = transform.position.y - initialGroundY;
                _hasGroundOffset = true;
            }
            else
            {
                _hasGroundOffset = false;
            }
        }
        _wasEnabled = isEnabled;

        if (!isEnabled || !_hasGroundOffset)
        {
            return;
        }

        float referenceY = transform.position.y - _groundOffset;
        if (TryFindNearestGroundY(referenceY, out float groundY))
        {
            Vector3 position = transform.position;
            float desiredY = groundY + _groundOffset;
            position.y = Mathf.Lerp(position.y, desiredY, 1f - Mathf.Exp(-heightSmoothSpeed * Time.deltaTime));
            transform.position = position;
        }
    }

    /// <summary>
    /// 從車輛目前位置往下打射線，找「跟 referenceY 最接近」的表面（不是最高點）；
    /// 如果最接近的候選還是差太多，代表 referenceY 本身可能已經不可靠（例如路徑正上方
    /// 剛好有裝飾物、公車等障礙物把它帶偏了），改用範圍內最低的表面收斂回真正的路面。
    /// </summary>
    private bool TryFindNearestGroundY(float referenceY, out float groundY)
    {
        groundY = referenceY;

        Vector3 origin = transform.position + Vector3.up * probeAbove;
        RaycastHit[] hits = Physics.RaycastAll(
            origin, Vector3.down, probeAbove + probeDistance, ~0, QueryTriggerInteraction.Ignore);

        bool found = false;
        float bestDistance = float.MaxValue;
        float lowestY = float.MaxValue;

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform.IsChildOf(transform))
            {
                continue;
            }

            if (hit.point.y < lowestY)
            {
                lowestY = hit.point.y;
            }

            float distance = Mathf.Abs(hit.point.y - referenceY);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                groundY = hit.point.y;
                found = true;
            }
        }

        if (found && bestDistance > maxDeviationBeforeFallback)
        {
            groundY = lowestY;
        }

        return found;
    }
}
