using UnityEngine;
using TMPro;

/// <summary>
/// 換成 VehiclePhysicsController 接手駕駛後，原本 CarController 被停用，
/// 它 Update() 裡順便更新時速表文字的那段邏輯也跟著停了——時速表看起來
/// 「壞掉」其實只是沒人接手更新，不是元件本身出問題。
/// 這裡單純讀 Rigidbody 目前的真實物理速度換算成 km/h 顯示，不管是誰在控制車輛
/// （VehiclePhysicsController 手動模式、或 AutoDriveController 自動模式），
/// 只要車身這顆 Rigidbody 真的在動，時速表就會動。
/// </summary>
public class SpeedometerBridge : MonoBehaviour
{
    public Rigidbody vehicleRigidbody;
    public TMP_Text speedText;

    void Update()
    {
        if (speedText == null || vehicleRigidbody == null) return;

        int speedKmh = Mathf.RoundToInt(vehicleRigidbody.velocity.magnitude * 3.6f);
        speedText.text = speedKmh.ToString("000") + " km/h";
    }
}
