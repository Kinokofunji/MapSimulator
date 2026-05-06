using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    // 開放變數，讓你在 Unity 介面拖曳綁定目標
    public Transform target;

    // 攝影機與車子的相對位置（預設：向後 8，向上 4）
    public Vector3 offset = new Vector3(0, 4f, -8f);

    // 運鏡的平滑度（數字越大跟得越緊）
    public float smoothSpeed = 5f;

    // 攝影機通常會寫在 LateUpdate，確保車子移動完才換攝影機動，畫面才不會抖
    void LateUpdate()
    {
        // 如果沒有綁定車子，就什麼都不做
        if (target == null) return;

        // 計算攝影機「應該」要去的位置 (會跟著車子旋轉)
        Vector3 desiredPosition = target.position + target.TransformDirection(offset);

        // 使用 Lerp 讓攝影機「平滑地」飛向目標位置
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);

        // 讓攝影機永遠注視著車子
        transform.LookAt(target);
    }
}