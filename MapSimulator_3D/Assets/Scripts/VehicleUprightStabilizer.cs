using UnityEngine;

/// <summary>
/// 強制車身隨時保持水平，只允許 Y 軸方向的車頭朝向自由旋轉。
///
/// 為什麼不能只靠 Rigidbody 的 Freeze Rotation X/Z：那個約束是物理引擎在單一步驟內
/// 迭代求解出來的結果，不是絕對不可能違反的鐵律。實測發現兩種情況都能讓它漏一點：
/// 1. 高速甩尾撞上路緣、燈柱等障礙物的瞬間強烈碰撞衝量，單一步驟來不及完全收斂。
/// 2. 長時間轉彎時持續的側向力（過彎的向心力、防打轉修正力矩），累積很多個物理
///    步驟後，每步一點點的殘留誤差疊加起來，車身會慢慢傾向一邊，變成只用單側
///    輪子貼地在跑。
///
/// 做法：每個 FixedUpdate 都直接把角速度的 X/Z 分量歸零、姿態強制拉回「只保留 Y 軸
/// 朝向」，在物理約束之外再加一層不會累積誤差的強制修正，讓車身邏輯上、視覺上
/// 隨時都是水平的，不管物理引擎內部實際模擬出什麼結果。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class VehicleUprightStabilizer : MonoBehaviour
{
    private Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        // 自動駕駛模式下 Rigidbody 是 Kinematic（AutoDriveController／避障腳本直接搬
        // Transform），角速度、MoveRotation 這些一般物理概念在 Kinematic 物體上沒有
        // 意義，Unity 也不支援對 Kinematic 物體設定角速度（會跳警告）。這個穩定器
        // 本來就只是為了修正一般動態物理模擬下的殘留旋轉誤差，Kinematic 時完全不需要
        // 它插手，直接跳過。
        if (rb.isKinematic)
        {
            return;
        }

        Vector3 angularVelocity = rb.angularVelocity;
        rb.angularVelocity = new Vector3(0f, angularVelocity.y, 0f);

        float currentYaw = transform.eulerAngles.y;
        rb.MoveRotation(Quaternion.Euler(0f, currentYaw, 0f));
    }
}
