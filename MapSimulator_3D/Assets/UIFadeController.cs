using UnityEngine;
using System.Collections;

[RequireComponent(typeof(CanvasGroup))]
public class UIFadeController : MonoBehaviour
{
    [Header("淡出設定")]
    [Tooltip("經過幾秒後自動開始淡出")]
    public float delayTime = 5f;
    [Tooltip("淡出過程花費的時間(秒)")]
    public float fadeDuration = 1f;

    private CanvasGroup canvasGroup;
    private bool isFading = false;

    void Start()
    {
        // 自動獲取掛載在同一物件上的 CanvasGroup
        canvasGroup = GetComponent<CanvasGroup>();

        // 遊戲開始時，啟動倒數計時器
        StartCoroutine(AutoFadeTimer());
    }

    void Update()
    {
        // 如果已經在淡出或已經隱藏，就跳過檢查以節省效能
        if (isFading) return;

        // 偵測玩家是否按下任何移動鍵 (W, A, S, D 或方向鍵)
        if (Input.GetAxisRaw("Horizontal") != 0 || Input.GetAxisRaw("Vertical") != 0)
        {
            // 玩家有輸入，強制開始淡出
            StartCoroutine(FadeOutUI());
        }
    }

    // 處理時間倒數
    private IEnumerator AutoFadeTimer()
    {
        yield return new WaitForSeconds(delayTime);

        // 時間到且尚未開始淡出時，執行淡出
        if (!isFading)
        {
            StartCoroutine(FadeOutUI());
        }
    }

    // 處理透明度漸變的核心邏輯
    private IEnumerator FadeOutUI()
    {
        isFading = true;
        float elapsedTime = 0f;

        // 透過 Lerp (線性插值) 平滑降低 alpha 值
        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsedTime / fadeDuration);
            yield return null; // 等待下一個 frame 繼續執行
        }

        // 確保最終透明度歸零
        canvasGroup.alpha = 0f;

        // 將 UI 物件徹底關閉，停止渲染與 Update 消耗
        gameObject.SetActive(false);
    }
}
