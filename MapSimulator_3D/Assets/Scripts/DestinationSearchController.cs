using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 目的地搜尋：讓玩家直接輸入地點名稱（例如「餐廳」「禮品店」），從清單裡挑選後
/// 直接開始導航，不需要在地圖上手動點選。
///
/// 搜尋範圍鎖定在名稱以「Building_」開頭的場景物件（跟 RoadGridPathfinder 認路面、
/// AutoDriveObstacleAvoidance 排除路面用的「Road 」前綴是同一種命名慣例的做法）——
/// 這批物件對應畫面上看得到招牌的商店/住宅（Fruits Shop、Restaurant...），是玩家
/// 唯一有理由「搜尋地點名稱」想去的地方，其餘裝飾物、車輛、路燈都不是有意義的目的地。
///
/// 選到結果後直接呼叫 MinimapController.SetDestination()，跟點地圖選目的地走
/// 完全相同的路徑規劃流程（如果有指定 RouteChoiceManager，一樣會规劃多條候選路線）。
/// </summary>
public class DestinationSearchController : MonoBehaviour
{
    [Header("依賴元件")]
    public MinimapController minimapController;

    [Header("UI 元件")]
    public TMP_InputField searchInput;
    public GameObject resultsPanel;
    public List<Button> resultButtons = new List<Button>();
    public List<TextMeshProUGUI> resultLabels = new List<TextMeshProUGUI>();

    [Tooltip("最多同時顯示幾筆搜尋結果，跟 resultButtons 的數量要一致")]
    public int maxResults = 5;

    private struct PointOfInterest
    {
        public string displayName;
        public Vector3 position;
        public Bounds bounds;
    }

    private readonly List<PointOfInterest> allPois = new List<PointOfInterest>();
    private readonly List<PointOfInterest> currentMatches = new List<PointOfInterest>();

    void Awake()
    {
        RebuildPoiList();

        if (searchInput != null)
        {
            searchInput.onValueChanged.AddListener(OnSearchTextChanged);
            searchInput.onEndEdit.AddListener(OnSearchSubmit);
        }

        for (int i = 0; i < resultButtons.Count; i++)
        {
            int index = i; // 閉包要複製一份區域變數，避免所有按鈕都指向同一個 i
            if (resultButtons[i] != null)
            {
                resultButtons[i].onClick.AddListener(() => SelectMatch(index));
            }
        }

        ShowResults(false);
    }

    /// <summary>
    /// 掃描場景，收集所有「Building_」開頭的物件當作可搜尋的目的地清單。
    /// 只在啟動時建立一次——場景裡的建築物是靜態擺放好的，不會在遊玩過程中變動，
    /// 沒必要每次搜尋都重新掃描整個場景。
    /// </summary>
    private void RebuildPoiList()
    {
        allPois.Clear();

        foreach (Transform t in Object.FindObjectsOfType<Transform>())
        {
            if (!t.name.StartsWith("Building_"))
            {
                continue;
            }

            // 有些建築物是拆成好幾個子物件建模的（牆面、屋頂等各自也叫 Building_ 開頭），
            // 只算最外層的物件，父物件如果也是 Building_ 開頭就跳過，避免同一棟建築物
            // 被拆成好幾筆搜尋結果。
            if (t.parent != null && t.parent.name.StartsWith("Building_"))
            {
                continue;
            }

            if (!TryComputeDestination(t, out Vector3 position, out Bounds bounds))
            {
                continue;
            }

            allPois.Add(new PointOfInterest
            {
                displayName = CleanDisplayName(t.name),
                position = position,
                bounds = bounds
            });

            Debug.Log($"[DestinationSearchController] 收錄地點：「{CleanDisplayName(t.name)}」" +
                      $"（原始物件名稱「{t.name}」，路徑 {GetHierarchyPath(t)}）座標 {position:F2}，" +
                      $"邊界大小 {bounds.size:F2}");
        }
    }

    private static string GetHierarchyPath(Transform t)
    {
        string path = t.name;
        Transform cursor = t.parent;
        while (cursor != null)
        {
            path = cursor.name + "/" + path;
            cursor = cursor.parent;
        }
        return path;
    }

    /// <summary>
    /// 把物件原始名稱轉成人類看得懂的顯示名稱：去掉「Building_」前綴、去掉 Unity
    /// 自動加的重複物件編號（例如「(2)」），底線換成空白。
    /// </summary>
    private static string CleanDisplayName(string rawName)
    {
        string name = rawName;
        const string prefix = "Building_";
        if (name.StartsWith(prefix))
        {
            name = name.Substring(prefix.Length);
        }

        int parenIndex = name.LastIndexOf('(');
        if (parenIndex > 0)
        {
            name = name.Substring(0, parenIndex).TrimEnd();
        }

        return name.Replace('_', ' ').Trim();
    }

    /// <summary>
    /// XZ 取建築物外觀邊界框的中心（比較接近視覺上的「這棟建築物在哪裡」），
    /// Y 用物件本身的 Transform 高度（場景裡的建築物本來就是擺在地面上的靜態物件，
    /// 不像地圖點擊那種任意座標需要另外打射線找地面高度）。
    ///
    /// 用「底下所有子物件外觀的合併邊界」而不是只抓第一個找到的 Renderer——有些建築物
    /// 是拆成好幾個子網格組成的（牆面、屋頂、招牌各自分開），只抓第一個找到的子物件，
    /// 算出來的中心點可能只是其中一小塊，跟整棟建築物實際的視覺位置會有落差，導航
    /// 過去對不到建築本體。
    ///
    /// 也把合併邊界框一起回傳：「該從建築物哪一側停下來」交給 RoadGridPathfinder 在
    /// 真正算完路徑、知道車輛實際會從哪個方向接近之後再決定——這裡如果自己先猜一個
    /// 方向，猜錯邊（例如建築物兩側剛好都有路）反而會把車導到對面去。
    /// </summary>
    private bool TryComputeDestination(Transform t, out Vector3 position, out Bounds bounds)
    {
        Renderer[] renderers = t.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            position = t.position;
            bounds = new Bounds(t.position, Vector3.zero);
            return true;
        }

        Bounds combinedBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combinedBounds.Encapsulate(renderers[i].bounds);
        }

        position = new Vector3(combinedBounds.center.x, t.position.y, combinedBounds.center.z);
        bounds = combinedBounds;
        return true;
    }

    private void OnSearchTextChanged(string query)
    {
        currentMatches.Clear();

        string trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            ShowResults(false);
            return;
        }

        foreach (PointOfInterest poi in allPois)
        {
            if (poi.displayName.IndexOf(trimmed, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                currentMatches.Add(poi);
                if (currentMatches.Count >= maxResults)
                {
                    break;
                }
            }
        }

        UpdateResultButtons();
        ShowResults(currentMatches.Count > 0);
    }

    private void UpdateResultButtons()
    {
        for (int i = 0; i < resultButtons.Count; i++)
        {
            if (resultButtons[i] == null)
            {
                continue;
            }

            bool hasMatch = i < currentMatches.Count;
            resultButtons[i].gameObject.SetActive(hasMatch);

            if (hasMatch && i < resultLabels.Count && resultLabels[i] != null)
            {
                resultLabels[i].text = currentMatches[i].displayName;
            }
        }
    }

    private void OnSearchSubmit(string query)
    {
        // TMP_InputField 的 onEndEdit 不是只有按 Enter 才會觸發——點擊搜尋結果按鈕時，
        // 輸入框會先失去焦點，這個「失去焦點」本身就會觸發 onEndEdit。如果不分辨兩種
        // 情況，點擊任何一筆結果都會先被這裡搶著選中第 0 筆，蓋掉玩家實際點擊的按鈕
        // （不管點哪個都變成同一個結果的成因）。只有真的偵測到按下 Enter 鍵，
        // 才視為「送出搜尋」；純粹因為點擊別的東西而失焦，不做任何事。
        bool pressedEnter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        if (!pressedEnter)
        {
            return;
        }

        if (currentMatches.Count > 0 && !string.IsNullOrEmpty(query.Trim()))
        {
            SelectMatch(0);
        }
    }

    private void SelectMatch(int index)
    {
        if (index < 0 || index >= currentMatches.Count || minimapController == null)
        {
            return;
        }

        Debug.Log($"[DestinationSearchController] 選擇第 {index} 筆結果「{currentMatches[index].displayName}」，" +
                  $"送出座標 {currentMatches[index].position:F2}");

        minimapController.SetDestination(currentMatches[index].position, currentMatches[index].bounds);

        if (searchInput != null)
        {
            searchInput.text = "";
            searchInput.DeactivateInputField();
        }

        ShowResults(false);
    }

    private void ShowResults(bool visible)
    {
        if (resultsPanel != null)
        {
            resultsPanel.SetActive(visible);
        }
    }
}
