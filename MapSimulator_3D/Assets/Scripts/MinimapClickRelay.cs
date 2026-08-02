using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 掛在「全景地圖」面板上，把點擊事件轉發給 MinimapController 處理。
/// 小地圖跟全景地圖尺寸不同，但選目的地的邏輯是共用的，所以由 MinimapController 統一計算，
/// 這裡只負責告訴它「是哪個面板被點了」。
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class MinimapClickRelay : MonoBehaviour, IPointerClickHandler
{
    public MinimapController minimapController;

    private RectTransform rectTransform;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (minimapController != null)
        {
            minimapController.HandleMapClick(rectTransform, eventData);
        }
    }
}
