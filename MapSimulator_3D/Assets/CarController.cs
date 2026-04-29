using UnityEngine;

public class CarController : MonoBehaviour
{
    // 開放變數，讓你在 Unity 介面上可以隨時微調速度
    public float speed = 15f;      // 前進後退的速度
    public float turnSpeed = 80f;  // 左右轉彎的靈敏度

    void Update()
    {
        // 抓取玩家的鍵盤輸入 (W/S/上/下 = Vertical, A/D/左/右 = Horizontal)
        float moveInput = Input.GetAxis("Vertical");
        float turnInput = Input.GetAxis("Horizontal");

        // 控制前後移動 (Translate)
        transform.Translate(Vector3.forward * moveInput * speed * Time.deltaTime);

        // 控制左右旋轉 (Rotate)
        transform.Rotate(Vector3.up * turnInput * turnSpeed * Time.deltaTime);
    }
}
