using UnityEngine;

/// <summary>
/// 保護原有 HUD（WASD 提示、轉向文字、速度表...）不被 UIFadeController 自動淡出關閉。
///
/// 安裝腳本會在 Editor 裡把場景中的 UIFadeController 停用，但如果那次操作是在 Play 模式底下執行的，
/// Unity 離開 Play 模式時會自動把所有 Play 模式中的變更復原，停用效果就會消失，
/// 下次進 Play 模式時 UIFadeController 又會恢復成啟用狀態、繼續把 HUD 淡出關閉。
///
/// 這支腳本在 Awake() 階段（早於所有物件的 Start()，包含 UIFadeController 自己的 Start()）
/// 主動把場景裡所有的 UIFadeController 停用一次，確保不管 Editor 那邊的狀態如何，
/// Runtime 一開始就不會讓 HUD 被淡出。
/// </summary>
public class HudVisibilityGuard : MonoBehaviour
{
    void Awake()
    {
        UIFadeController[] faders = FindObjectsOfType<UIFadeController>(true);

        foreach (UIFadeController fader in faders)
        {
            fader.enabled = false;

            CanvasGroup group = fader.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;
            }

            if (!fader.gameObject.activeSelf)
            {
                fader.gameObject.SetActive(true);
            }
        }
    }
}
