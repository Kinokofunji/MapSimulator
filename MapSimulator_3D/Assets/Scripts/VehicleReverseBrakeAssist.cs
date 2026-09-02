using UnityEngine;

/// <summary>
/// 修正一個操控上的怪異手感：正在前進時立刻按下倒車鍵，Navigation.VehiclePhysicsController
/// 原本的邏輯會直接對輪子下「反向馬達扭力」——輪子明明還在往前轉，卻被要求瞬間反向出力，
/// 中間會經過一段扭力歸零、引擎煞車介入的過渡窗口，開起來的感覺是「怪怪地先有動靜一下
/// 才減速」。真實車輛/大多數賽車遊戲的做法是：車輛還在往原方向前進時按下反方向鍵，
/// 視為「煞車」，等真的停下來後才切換成反向出力，不會讓輪子在還在轉的狀態下被硬拗成
/// 反向出力。
///
/// 做法：不直接改 Navigation.VehiclePhysicsController 的原始碼，而是用她自己就設計好的
/// 外部輸入注入介面 SetExternalDriveInput（她的註解寫明用途就包含「未來的手機虛擬搖桿
/// 或 UI 按鈕」，本來就是給外部系統覆寫輸入用的）。這裡讀跟她一樣的鍵盤輸入，判斷
/// 「玩家想要的方向」跟「車輛目前朝車頭方向的實際速度」是否相反，相反就把這一幀的輸入
/// 改成煞車，方向一致或車輛已經接近靜止就原樣傳遞，不影響平常的加速/倒車手感。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class VehicleReverseBrakeAssist : MonoBehaviour
{
    public Navigation.VehiclePhysicsController physicsController;

    [Tooltip("車頭方向速度低於這個值（公尺/秒）就視為已經接近靜止，可以直接切換方向，不需要再煞車")]
    public float stoppedSpeedThreshold = 0.5f;

    private Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        if (physicsController == null || !physicsController.enabled) return;

        float verticalInput = Input.GetAxis("Vertical");
        float horizontalInput = Input.GetAxis("Horizontal");
        bool isBraking = Input.GetKey(KeyCode.Space);

        float forwardSpeed = Vector3.Dot(transform.forward, rb.velocity);
        bool hasVerticalInput = Mathf.Abs(verticalInput) > 0.01f;

        // 玩家想要的方向跟車輛目前實際朝車頭方向的速度正負號相反、而且還沒接近靜止，
        // 視為「想煞車」而不是「想反向出力」。
        bool wantsOppositeDirection =
            hasVerticalInput &&
            Mathf.Abs(forwardSpeed) > stoppedSpeedThreshold &&
            Mathf.Sign(verticalInput) != Mathf.Sign(forwardSpeed);

        if (wantsOppositeDirection)
        {
            isBraking = true;
            verticalInput = 0f;
        }

        physicsController.SetExternalDriveInput(new Vector3(verticalInput, horizontalInput, isBraking ? 1f : 0f));
    }
}
