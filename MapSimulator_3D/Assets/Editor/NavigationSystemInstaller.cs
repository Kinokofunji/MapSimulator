using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 一次性安裝工具：把 GoogleMapCamera / NavigationLineManager / NavigationUIManager
/// 自動掛載並串接到「目前開啟的場景」中，取代手動在 Hierarchy / Inspector 拉線的步驟。
/// 使用方式：在 Unity 上方選單 Tools -> 導航系統 -> 安裝到目前場景。
/// </summary>
public static class NavigationSystemInstaller
{
    // TextMeshPro 內建的 LiberationSans SDF 字體不含中文字，畫面上的中文全部會變成方框（Console 會看到
    // "The character with Unicode value ... was not found" 的警告）。Unity 沒辦法用純程式安全地產生 SDF 字體
    // 圖集（需要 Editor 的 Font Asset Creator 實際跑一次字體渲染），所以這裡改成「約定一個固定路徑」：
    // 只要你照下面說明在這個路徑放一個支援中文的 TMP Font Asset，安裝腳本之後每次執行都會自動套用到所有文字。
    private const string ChineseFontAssetPath = "Assets/Fonts/ChineseFont SDF.asset";

    [MenuItem("Tools/導航系統/安裝到目前場景")]
    public static void InstallIntoCurrentScene()
    {
        // 1. 找到掛有 CarController 的車輛
        CarController car = Object.FindObjectOfType<CarController>();
        if (car == null)
        {
            EditorUtility.DisplayDialog(
                "安裝失敗",
                "目前場景中找不到掛有 CarController 的車輛物件，請先確認車輛已放入場景中再執行安裝。",
                "確定");
            return;
        }

        // 2. 找到攝影機（優先用現有 CameraFollow 所在物件，其次用 Main Camera）
        Camera cam = FindTargetCamera();
        if (cam == null)
        {
            EditorUtility.DisplayDialog(
                "安裝失敗",
                "目前場景中找不到攝影機 (Camera)，請先建立場景攝影機再執行安裝。",
                "確定");
            return;
        }

        Canvas canvas = GetOrCreateCanvas();

        SetupCamera(cam, car.transform);
        NavigationLineManager lineManager = SetupNavigationLine(car.transform);
        SetupNavigationUI(canvas, lineManager, car.transform);
        SetupCarReset(car, lineManager);
        RoadGridPathfinder pathfinder = GetOrCreateRoadGridPathfinder();
        MinimapController minimapController = SetupMinimap(canvas, car.transform, lineManager, pathfinder);
        SetupRouteChoice(canvas, car.transform, lineManager, minimapController, pathfinder);
        SetupDestinationSearch(canvas, minimapController);

        // 自動停用 UIFadeController，避免既有 HUD（WASD 提示、轉向文字、速度表...）連同新裝的導航 UI
        // 一起在幾秒後或一開始移動就被淡出關閉
        int disabledFaderCount = DisableUIFadeControllersInternal();

        // 上面那行只在 Editor 端生效；如果是在 Play 模式底下跑這個安裝流程，離開 Play 模式後會被 Unity 復原。
        // 額外掛一個 Runtime 版本的保護，確保每次進 Play 模式一開始就會再次停用，不依賴「有沒有在正確時機執行安裝腳本」
        SetupHudVisibilityGuard();

        bool chineseFontApplied = ApplyChineseFontIfAvailable(canvas);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        string fontNote = chineseFontApplied
            ? "・已將所有導航文字套用中文字體\n"
            : $"・找不到中文字體資源，畫面上的中文會顯示成方框（Console 會有對應警告）。" +
              $"請用 Window → TextMeshPro → Font Asset Creator 產生一個支援中文的字體資源，存成 {ChineseFontAssetPath}，再重新執行這個安裝\n";

        EditorUtility.DisplayDialog(
            "安裝完成",
            "已在目前場景安裝：\n" +
            "・GoogleMapCamera（掛在 " + cam.name + "，並停用了原本的 CameraFollow）\n" +
            "・NavigationLineManager（NavigationManager 物件，預設沒有任何路口節點，等你點地圖選目的地後才會出現路線）\n" +
            "・NavigationUIManager（Canvas 底下的 TurnCardPanel）\n" +
            "・CarResetController（掛在車輛上，預設按 R 鍵重置回起點，掉出地圖也會自動重置，且會一併重置導航進度）\n" +
            "・MinimapController（Canvas 右上角的 MinimapPanel，可點擊小地圖設定導航目的地；按 M 鍵展開/收合全景地圖 FullMapPanel）\n" +
            "・NavigationLineManager 新增「錯過路口自動重新導航」，開過頭會自動跳到最近的後續路口\n" +
            "・RoadGridPathfinder + RouteChoiceManager：點地圖選目的地後，會沿著場景裡實際的道路方磚網格規劃最多 3 條候選路線讓你挑選\n" +
            $"・已順便停用 {disabledFaderCount} 個 UIFadeController，避免 HUD 被自動淡出關閉\n" +
            "・HudVisibilityGuard：每次進 Play 模式一開始就會再次強制停用 UIFadeController，就算之前是在 Play 模式底下跑安裝腳本、Editor 端的停用被 Unity 復原了也沒關係\n" +
            "・修正全景地圖按 M 鍵無法重複收合的 bug\n" +
            "・小地圖的玩家箭頭/目的地標記換成程式產生的箭頭/圖釘圖示（Assets/Textures/），不再是純色方塊\n" +
            "・轉彎卡片右上角新增「X」取消導航按鈕，按下會清空目前路線\n" +
            "・畫面左上角新增目的地搜尋欄：輸入地點名稱（例如「餐廳」），會列出符合的地點，點選或按 Enter 直接開始導航\n" +
            fontNote + "\n" +
            "還需要你手動完成：\n" +
            "1. 確認無誤後記得存檔 (Ctrl+S)\n\n" +
            "注意：道路網格路線是用場景裡道路方磚的排列位置反推出來的近似連通關係，不是真正理解每塊方磚開口方向的精準道路圖，" +
            "在少數轉角/T字路口可能出現不完全貼合的轉彎，屬於已知限制。\n\n" +
            "這次也在 NavigationUIManager 加了除錯訊息，Play 模式測試時 Console 會即時顯示卡片顯示/隱藏的原因（例如距離多遠），" +
            "如果選了路線還是看不到卡片，麻煩把那則訊息複製給我。",
            "了解");
    }

    /// <summary>
    /// 場景裡有些 UI（例如 WASD 操作提示）掛了 UIFadeController，
    /// 會在幾秒後或玩家一開始移動就自動淡出並把整個物件 SetActive(false)。
    /// 如果這個元件是掛在 Canvas 根物件上，會連同底下所有 UI（包含導航卡片）一起被關閉。
    /// 這個選單會找出場景中所有 UIFadeController 並停用它們，讓對應的 UI 維持顯示、不再消失。
    /// </summary>
    [MenuItem("Tools/導航系統/停用 UI 自動淡出提示 (UIFadeController)")]
    public static void DisableUIFadeControllers()
    {
        int disabledCount = DisableUIFadeControllersInternal();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog(
            "完成",
            $"已停用 {disabledCount} 個 UIFadeController。\n" +
            "對應的 CanvasGroup alpha 已還原為 1、物件已確保為 Active。\n" +
            "記得存檔 (Ctrl+S)。",
            "確定");
    }

    // 已經確認裝好整套導航系統、也實際測試過可以動的場景。
    // 專案裡另外還有一個 CitySimulator.unity，內容跟這個場景很像，但完全沒裝過導航系統，
    // 之前就是因為 Build Settings 把 CitySimulator.unity 排在第一個（開機優先載入），
    // 才會發生「WebGL 建置好好的，但畫面沒有小地圖」的狀況。
    private const string CanonicalScenePath =
        "Assets/SimplePoly City - Low Poly Assets/Demo/SimplePoly City - Low Poly Assets_Demo Scene.unity";

    /// <summary>
    /// 整理 Build Settings：把已經確認裝好導航系統的場景設成唯一啟用、且排第一個的場景，
    /// 其他場景保留在清單裡但停用（不會刪除任何場景檔案，之後隨時可以在 Build Settings 視窗裡重新勾選）。
    /// </summary>
    [MenuItem("Tools/導航系統/整理 Build Settings（只啟用已裝導航系統的場景）")]
    public static void ConsolidateBuildScenes()
    {
        EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;

        var newScenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(CanonicalScenePath, true)
        };

        foreach (EditorBuildSettingsScene scene in existing)
        {
            if (scene.path == CanonicalScenePath) continue;
            newScenes.Add(new EditorBuildSettingsScene(scene.path, false));
        }

        EditorBuildSettings.scenes = newScenes.ToArray();

        EditorUtility.DisplayDialog(
            "完成",
            $"Build Settings 已整理：\n\n" +
            $"・{CanonicalScenePath}\n  現在是第一個、且是唯一啟用的場景，WebGL 建置開機會載入它\n\n" +
            "・其他場景保留在清單中，但已停用（沒有刪除任何檔案，之後要用隨時可以在 Build Settings 視窗裡重新勾選）\n\n" +
            "這是 Editor 端的設定變更，不需要存場景，但下次 Build WebGL 前建議打開 File → Build Settings 眼睛看一下確認清單正確。",
            "了解");
    }

    /// <summary>
    /// 如果 ChineseFontAssetPath 那個路徑存在一個支援中文的 TMP Font Asset，
    /// 就把 Canvas 底下所有 TextMeshProUGUI 文字（不管是不是這次新建的）都換成那個字體。
    /// 回傳是否有找到並套用。
    /// </summary>
    private static bool ApplyChineseFontIfAvailable(Canvas canvas)
    {
        TMP_FontAsset chineseFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ChineseFontAssetPath);
        if (chineseFont == null) return false;

        TextMeshProUGUI[] texts = canvas.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (TextMeshProUGUI text in texts)
        {
            text.font = chineseFont;
        }

        return true;
    }

    private static void SetupHudVisibilityGuard()
    {
        GameObject guardObj = GameObject.Find("HudVisibilityGuard");
        if (guardObj == null)
        {
            guardObj = new GameObject("HudVisibilityGuard");
        }

        if (guardObj.GetComponent<HudVisibilityGuard>() == null)
        {
            guardObj.AddComponent<HudVisibilityGuard>();
        }
    }

    /// <summary>實際執行停用 UIFadeController 的邏輯，回傳停用的數量，不彈對話框（給安裝流程內部呼叫用）。</summary>
    private static int DisableUIFadeControllersInternal()
    {
        UIFadeController[] faders = Object.FindObjectsOfType<UIFadeController>(true);

        int disabledCount = 0;
        foreach (UIFadeController fader in faders)
        {
            if (fader.enabled)
            {
                fader.enabled = false;
                disabledCount++;
            }

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

        return disabledCount;
    }

    private static Camera FindTargetCamera()
    {
        CameraFollow existingFollow = Object.FindObjectOfType<CameraFollow>();
        if (existingFollow != null)
        {
            Camera camOnFollow = existingFollow.GetComponent<Camera>();
            if (camOnFollow != null) return camOnFollow;
        }

        if (Camera.main != null) return Camera.main;

        return Object.FindObjectOfType<Camera>();
    }

    private static void SetupCamera(Camera cam, Transform carTransform)
    {
        GameObject camObj = cam.gameObject;

        // 保留舊的 CameraFollow 元件（不刪除，只停用），避免破壞既有設定，方便日後比較/還原
        CameraFollow oldFollow = camObj.GetComponent<CameraFollow>();
        if (oldFollow != null)
        {
            oldFollow.enabled = false;
        }

        GoogleMapCamera gmCam = camObj.GetComponent<GoogleMapCamera>();
        if (gmCam == null)
        {
            gmCam = camObj.AddComponent<GoogleMapCamera>();
        }
        gmCam.target = carTransform;

        if (camObj.CompareTag("Untagged"))
        {
            camObj.tag = "MainCamera";
        }
    }

    private static NavigationLineManager SetupNavigationLine(Transform carTransform)
    {
        GameObject navObj = GameObject.Find("NavigationManager");
        if (navObj == null)
        {
            navObj = new GameObject("NavigationManager");
        }

        LineRenderer lr = navObj.GetComponent<LineRenderer>();
        if (lr == null)
        {
            lr = navObj.AddComponent<LineRenderer>();
            lr.startWidth = 1.2f;
            lr.endWidth = 1.2f;
            lr.useWorldSpace = true;
            lr.numCapVertices = 8;
            lr.numCornerVertices = 4;
        }
        if (lr.sharedMaterial == null)
        {
            lr.sharedMaterial = GetOrCreateLineMaterial();
        }

        NavigationLineManager lineManager = navObj.GetComponent<NavigationLineManager>();
        if (lineManager == null)
        {
            lineManager = navObj.AddComponent<NavigationLineManager>();
        }
        lineManager.player = carTransform;

        if (lineManager.waypoints == null)
        {
            lineManager.waypoints = new List<NavWaypoint>();
        }
        else
        {
            // 清掉舊版安裝腳本留下的示範路口佔位座標，預設不帶任何路線，等玩家點地圖選目的地才產生路線
            lineManager.waypoints.RemoveAll(w => w != null && w.roadName != null && w.roadName.Contains("示範座標"));
        }

        return lineManager;
    }

    private static Material GetOrCreateLineMaterial()
    {
        const string path = "Assets/Materials/NavigationLineMaterial.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }

        mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(0.15f, 0.45f, 0.95f, 1f); // 導航藍
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static Canvas GetOrCreateCanvas()
    {
        Canvas canvas = Object.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObj = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        EnsureEventSystem();
        return canvas;
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindObjectOfType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    private static void SetupCarReset(CarController car, NavigationLineManager lineManager)
    {
        CarResetController reset = car.GetComponent<CarResetController>();
        if (reset == null)
        {
            reset = car.gameObject.AddComponent<CarResetController>();
        }
        reset.lineManager = lineManager;
    }

    private static RoadGridPathfinder GetOrCreateRoadGridPathfinder()
    {
        GameObject pathfinderObj = GameObject.Find("RoadGridPathfinder");
        if (pathfinderObj == null)
        {
            pathfinderObj = new GameObject("RoadGridPathfinder");
        }

        RoadGridPathfinder pathfinder = pathfinderObj.GetComponent<RoadGridPathfinder>();
        if (pathfinder == null)
        {
            pathfinder = pathfinderObj.AddComponent<RoadGridPathfinder>();
        }

        return pathfinder;
    }

    private static MinimapController SetupMinimap(
        Canvas canvas, Transform carTransform, NavigationLineManager lineManager, RoadGridPathfinder pathfinder)
    {
        GameObject camObj = GameObject.Find("MinimapCamera");
        Camera minimapCam;
        if (camObj == null)
        {
            camObj = new GameObject("MinimapCamera", typeof(Camera));
            minimapCam = camObj.GetComponent<Camera>();
            minimapCam.orthographic = true;
            minimapCam.orthographicSize = 60f;
            minimapCam.nearClipPlane = 1f;
            minimapCam.farClipPlane = 2000f;
            minimapCam.clearFlags = CameraClearFlags.SolidColor;
            minimapCam.backgroundColor = Color.black;
            minimapCam.depth = -10; // 確保不會蓋過主攝影機成為 Main Camera
        }
        else
        {
            minimapCam = camObj.GetComponent<Camera>();
        }

        RenderTexture rt = GetOrCreateMinimapRenderTexture();
        minimapCam.targetTexture = rt;

        Transform existingPanel = canvas.transform.Find("MinimapPanel");
        GameObject panelObj = existingPanel != null ? existingPanel.gameObject : CreateMinimapPanel(canvas.transform, rt);

        RawImage rawImage = panelObj.GetComponent<RawImage>();
        rawImage.texture = rt;

        MinimapController controller = panelObj.GetComponent<MinimapController>();
        if (controller == null)
        {
            controller = panelObj.AddComponent<MinimapController>();
        }

        controller.player = carTransform;
        controller.minimapCamera = minimapCam;
        controller.lineManager = lineManager;
        controller.pathfinder = pathfinder;

        Transform arrowT = panelObj.transform.Find("PlayerArrow");
        Transform destT = panelObj.transform.Find("DestinationMarker");
        if (arrowT != null) controller.playerArrow = arrowT.GetComponent<RectTransform>();
        if (destT != null) controller.destinationMarker = destT.GetComponent<RectTransform>();
        ApplyMinimapIcons(arrowT, destT);

        // 全景地圖：跟小地圖共用同一台攝影機/RenderTexture，展開時佔滿畫面
        Transform existingFullMap = canvas.transform.Find("FullMapPanel");
        GameObject fullMapObj = existingFullMap != null ? existingFullMap.gameObject : CreateFullMapPanel(canvas.transform, rt);

        MinimapClickRelay relay = fullMapObj.GetComponent<MinimapClickRelay>();
        if (relay == null)
        {
            relay = fullMapObj.AddComponent<MinimapClickRelay>();
        }
        relay.minimapController = controller;

        controller.fullMapPanel = fullMapObj.GetComponent<RectTransform>();

        return controller;
    }

    private static void SetupRouteChoice(
        Canvas canvas, Transform carTransform, NavigationLineManager lineManager,
        MinimapController minimapController, RoadGridPathfinder pathfinder)
    {
        Transform existingPanel = canvas.transform.Find("RouteChoicePanel");
        GameObject panelObj = existingPanel != null ? existingPanel.gameObject : CreateRouteChoicePanel(canvas.transform);

        RouteChoiceManager routeChoiceManager = pathfinder.GetComponent<RouteChoiceManager>();
        if (routeChoiceManager == null)
        {
            routeChoiceManager = pathfinder.gameObject.AddComponent<RouteChoiceManager>();
        }

        routeChoiceManager.pathfinder = pathfinder;
        routeChoiceManager.lineManager = lineManager;
        routeChoiceManager.player = carTransform;
        routeChoiceManager.routeChoicePanel = panelObj;
        // 每次安裝都強制覆蓋，避免舊版本裝過的元件卡著舊的序列化預設值不會自動更新。
        routeChoiceManager.alreadyArrivedDistance = 12f;

        routeChoiceManager.routeButtons = new List<Button>();
        for (int i = 0; i < 3; i++)
        {
            Transform buttonT = panelObj.transform.Find($"RouteButton_{i}");
            if (buttonT != null)
            {
                routeChoiceManager.routeButtons.Add(buttonT.GetComponent<Button>());
            }
        }

        if (minimapController != null)
        {
            minimapController.routeChoiceManager = routeChoiceManager;
        }

        panelObj.SetActive(false);
    }

    private static GameObject CreateRouteChoicePanel(Transform canvasTransform)
    {
        GameObject panel = new GameObject("RouteChoicePanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasTransform, false);

        // 貼右邊、垂直置中：避開既有 HUD（左下 WASD 按鈕、右下速度表、置中偏下的轉向文字提示）
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 0.5f);
        panelRect.anchorMax = new Vector2(1f, 0.5f);
        panelRect.pivot = new Vector2(1f, 0.5f);
        panelRect.sizeDelta = new Vector2(300, 200);
        panelRect.anchoredPosition = new Vector2(-20, 0);
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        Color[] labelColors =
        {
            new Color(0.15f, 0.45f, 0.95f, 1f),
            new Color(0.6f, 0.6f, 0.6f, 1f),
            new Color(0.6f, 0.35f, 0.8f, 1f)
        };
        string[] labels = { "路線 1 (最短)", "路線 2 (替代)", "路線 3 (替代)" };

        for (int i = 0; i < 3; i++)
        {
            GameObject buttonObj = new GameObject($"RouteButton_{i}", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObj.transform.SetParent(panel.transform, false);

            RectTransform buttonRect = buttonObj.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0f, 1f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.pivot = new Vector2(0.5f, 1f);
            buttonRect.sizeDelta = new Vector2(-20, 50);
            buttonRect.anchoredPosition = new Vector2(0, -10 - i * 58);

            buttonObj.GetComponent<Image>().color = labelColors[i];

            GameObject textObj = new GameObject("Label", typeof(RectTransform));
            textObj.transform.SetParent(buttonObj.transform, false);
            TextMeshProUGUI label = textObj.AddComponent<TextMeshProUGUI>();
            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            label.text = labels[i];
            label.fontSize = 24;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
        }

        panel.SetActive(false);
        return panel;
    }

    private static void SetupDestinationSearch(Canvas canvas, MinimapController minimapController)
    {
        Transform existingPanel = canvas.transform.Find("SearchPanel");
        GameObject panelObj;
        TMP_InputField inputField;
        GameObject resultsPanelObj;
        List<Button> resultButtons;
        List<TextMeshProUGUI> resultLabels;

        if (existingPanel != null)
        {
            panelObj = existingPanel.gameObject;
            inputField = panelObj.GetComponentInChildren<TMP_InputField>(true);
            Transform resultsT = panelObj.transform.Find("SearchResultsPanel");
            resultsPanelObj = resultsT != null ? resultsT.gameObject : null;
            resultButtons = new List<Button>();
            resultLabels = new List<TextMeshProUGUI>();
            if (resultsPanelObj != null)
            {
                for (int i = 0; i < 5; i++)
                {
                    Transform buttonT = resultsPanelObj.transform.Find($"ResultButton_{i}");
                    if (buttonT == null) continue;
                    resultButtons.Add(buttonT.GetComponent<Button>());
                    Transform labelT = buttonT.Find("Label");
                    resultLabels.Add(labelT != null ? labelT.GetComponent<TextMeshProUGUI>() : null);
                }
            }
        }
        else
        {
            panelObj = CreateSearchPanel(
                canvas.transform, out inputField, out resultsPanelObj, out resultButtons, out resultLabels);
        }

        DestinationSearchController controller = panelObj.GetComponent<DestinationSearchController>();
        if (controller == null)
        {
            controller = panelObj.AddComponent<DestinationSearchController>();
        }

        controller.minimapController = minimapController;
        controller.searchInput = inputField;
        controller.resultsPanel = resultsPanelObj;
        controller.resultButtons = resultButtons;
        controller.resultLabels = resultLabels;

        // 獨立呼叫、不管 SearchPanel 是不是這次新建的都會執行——道理跟 ApplyMinimapIcons、
        // EnsureCancelButton 一樣：場景裡已經有 SearchPanel 時就不會重新執行
        // CreateSearchPanel，尺寸/字體大小的調整永遠套用不上去。
        ApplySearchPanelStyle(panelObj, inputField, resultsPanelObj, resultButtons, resultLabels);
    }

    /// <summary>
    /// 搜尋欄跟結果清單的尺寸/字體大小統一在這裡設定，每次安裝都強制覆蓋，
    /// 確保不管場景之前裝的是哪一版尺寸，都會更新成最新的數值。
    /// </summary>
    private static void ApplySearchPanelStyle(
        GameObject panelObj, TMP_InputField inputField, GameObject resultsPanelObj,
        List<Button> resultButtons, List<TextMeshProUGUI> resultLabels)
    {
        const float panelWidth = 380f;
        const float inputHeight = 60f;
        const float inputFontSize = 28f;
        const float rowHeight = 48f;
        const float rowFontSize = 24f;

        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        if (panelRect != null)
        {
            panelRect.sizeDelta = new Vector2(panelWidth, inputHeight);
        }

        if (inputField != null)
        {
            if (inputField.textComponent != null)
            {
                inputField.textComponent.fontSize = inputFontSize;
            }

            if (inputField.placeholder is TextMeshProUGUI placeholderTmp)
            {
                placeholderTmp.fontSize = inputFontSize;
            }
        }

        if (resultsPanelObj != null)
        {
            RectTransform resultsRect = resultsPanelObj.GetComponent<RectTransform>();
            if (resultsRect != null)
            {
                resultsRect.sizeDelta = new Vector2(0, resultButtons.Count * rowHeight);
            }
        }

        for (int i = 0; i < resultButtons.Count; i++)
        {
            if (resultButtons[i] == null) continue;

            RectTransform buttonRect = resultButtons[i].GetComponent<RectTransform>();
            if (buttonRect != null)
            {
                buttonRect.sizeDelta = new Vector2(0, rowHeight - 2);
                buttonRect.anchoredPosition = new Vector2(0, -i * rowHeight);
            }

            if (i < resultLabels.Count && resultLabels[i] != null)
            {
                resultLabels[i].fontSize = rowFontSize;
            }
        }
    }

    /// <summary>
    /// 建立目的地搜尋欄：畫面左上角一個輸入框，下方是符合搜尋文字的地點清單（預設隱藏，
    /// 有輸入文字且找到符合的地點才展開）。位置刻意選左上角——上方置中是轉彎卡片
    /// （TurnCardPanel），右上角是小地圖（MinimapPanel），左上角是唯一還空著的角落。
    /// </summary>
    private static GameObject CreateSearchPanel(
        Transform canvasTransform, out TMP_InputField inputField, out GameObject resultsPanel,
        out List<Button> resultButtons, out List<TextMeshProUGUI> resultLabels)
    {
        GameObject panel = new GameObject("SearchPanel", typeof(RectTransform));
        panel.transform.SetParent(canvasTransform, false);

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.sizeDelta = new Vector2(380, 60); // 實際數值以 ApplySearchPanelStyle 為準，這裡只是初始值
        panelRect.anchoredPosition = new Vector2(20, -20);

        GameObject inputObj = new GameObject(
            "SearchInputField", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        inputObj.transform.SetParent(panel.transform, false);
        RectTransform inputRect = inputObj.GetComponent<RectTransform>();
        inputRect.anchorMin = Vector2.zero;
        inputRect.anchorMax = Vector2.one;
        inputRect.offsetMin = Vector2.zero;
        inputRect.offsetMax = Vector2.zero;
        inputObj.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.9f);

        inputField = inputObj.GetComponent<TMP_InputField>();

        GameObject textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(inputObj.transform, false);
        RectTransform textAreaRect = textArea.GetComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(10, 6);
        textAreaRect.offsetMax = new Vector2(-10, -6);

        GameObject placeholderObj = new GameObject("Placeholder", typeof(RectTransform));
        placeholderObj.transform.SetParent(textArea.transform, false);
        TextMeshProUGUI placeholder = placeholderObj.AddComponent<TextMeshProUGUI>();
        RectTransform placeholderRect = placeholderObj.GetComponent<RectTransform>();
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = Vector2.zero;
        placeholderRect.offsetMax = Vector2.zero;
        placeholder.text = "搜尋地點名稱...";
        placeholder.fontSize = 28;
        placeholder.fontStyle = FontStyles.Italic;
        placeholder.color = new Color(0f, 0f, 0f, 0.5f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;

        GameObject textObj = new GameObject("Text", typeof(RectTransform));
        textObj.transform.SetParent(textArea.transform, false);
        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        text.fontSize = 28;
        text.color = Color.black;
        text.alignment = TextAlignmentOptions.MidlineLeft;

        inputField.textViewport = textAreaRect;
        inputField.textComponent = text;
        inputField.placeholder = placeholder;
        inputField.lineType = TMP_InputField.LineType.SingleLine;

        const int maxRows = 5;
        const float rowHeight = 48f; // 實際數值以 ApplySearchPanelStyle 為準，這裡只是初始值

        resultsPanel = new GameObject("SearchResultsPanel", typeof(RectTransform), typeof(Image));
        resultsPanel.transform.SetParent(panel.transform, false);
        RectTransform resultsRect = resultsPanel.GetComponent<RectTransform>();
        resultsRect.anchorMin = new Vector2(0f, 0f);
        resultsRect.anchorMax = new Vector2(1f, 0f);
        resultsRect.pivot = new Vector2(0.5f, 1f);
        resultsRect.anchoredPosition = new Vector2(0, -4);
        resultsRect.sizeDelta = new Vector2(0, maxRows * rowHeight);
        resultsPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

        resultButtons = new List<Button>();
        resultLabels = new List<TextMeshProUGUI>();

        for (int i = 0; i < maxRows; i++)
        {
            GameObject buttonObj = new GameObject($"ResultButton_{i}", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObj.transform.SetParent(resultsPanel.transform, false);

            RectTransform buttonRect = buttonObj.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0f, 1f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.pivot = new Vector2(0.5f, 1f);
            buttonRect.sizeDelta = new Vector2(0, rowHeight - 2);
            buttonRect.anchoredPosition = new Vector2(0, -i * rowHeight);

            buttonObj.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

            GameObject labelObj = new GameObject("Label", typeof(RectTransform));
            labelObj.transform.SetParent(buttonObj.transform, false);
            TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
            RectTransform labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10, 0);
            labelRect.offsetMax = new Vector2(-10, 0);
            label.text = "";
            label.fontSize = 24;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.MidlineLeft;

            resultButtons.Add(buttonObj.GetComponent<Button>());
            resultLabels.Add(label);
            buttonObj.SetActive(false);
        }

        resultsPanel.SetActive(false);

        return panel;
    }

    private static RenderTexture GetOrCreateMinimapRenderTexture()
    {
        const string path = "Assets/Materials/MinimapRenderTexture.renderTexture";
        RenderTexture rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
        if (rt != null) return rt;

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }

        rt = new RenderTexture(512, 512, 16);
        AssetDatabase.CreateAsset(rt, path);
        return rt;
    }

    private static GameObject CreateMinimapPanel(Transform canvasTransform, RenderTexture rt)
    {
        GameObject panel = new GameObject("MinimapPanel", typeof(RectTransform), typeof(RawImage));
        panel.transform.SetParent(canvasTransform, false);

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.sizeDelta = new Vector2(220, 220);
        panelRect.anchoredPosition = new Vector2(-30, -30);

        panel.GetComponent<RawImage>().texture = rt;

        GameObject arrowObj = new GameObject("PlayerArrow", typeof(RectTransform), typeof(Image));
        arrowObj.transform.SetParent(panel.transform, false);
        RectTransform arrowRect = arrowObj.GetComponent<RectTransform>();
        arrowRect.anchorMin = new Vector2(0.5f, 0.5f);
        arrowRect.anchorMax = new Vector2(0.5f, 0.5f);
        arrowRect.pivot = new Vector2(0.5f, 0.5f);
        arrowRect.sizeDelta = new Vector2(28, 28);
        arrowRect.anchoredPosition = Vector2.zero;
        arrowObj.GetComponent<Image>().color = new Color(1f, 0.85f, 0f, 1f);

        GameObject destObj = new GameObject("DestinationMarker", typeof(RectTransform), typeof(Image));
        destObj.transform.SetParent(panel.transform, false);
        RectTransform destRect = destObj.GetComponent<RectTransform>();
        destRect.anchorMin = new Vector2(0.5f, 0.5f);
        destRect.anchorMax = new Vector2(0.5f, 0.5f);
        destRect.pivot = new Vector2(0.5f, 1f); // pivot 對齊圖釘的尖端，這樣標記位置才會對準實際座標
        destRect.sizeDelta = new Vector2(22, 28);
        destObj.GetComponent<Image>().color = new Color(0.9f, 0.15f, 0.15f, 1f);
        destObj.SetActive(false);

        return panel;
    }

    /// <summary>
    /// 幫玩家箭頭/目的地標記套用程式產生的 Sprite。
    /// 特意獨立成一個「不管物件是不是這次新建的都會執行」的步驟——如果只寫在 CreateMinimapPanel 裡，
    /// 場景裡已經有 MinimapPanel（例如上一版安裝腳本建立的）時就不會重新執行 CreateMinimapPanel，
    /// 新加的圖示也就永遠套用不上去。
    /// </summary>
    private static void ApplyMinimapIcons(Transform arrowT, Transform destT)
    {
        if (arrowT != null)
        {
            Image arrowImage = arrowT.GetComponent<Image>();
            if (arrowImage != null)
            {
                arrowImage.sprite = GetOrCreateArrowSprite();
                arrowImage.preserveAspect = true;
            }
        }

        if (destT != null)
        {
            Image destImage = destT.GetComponent<Image>();
            if (destImage != null)
            {
                destImage.sprite = GetOrCreatePinSprite();
                destImage.preserveAspect = true;
            }
        }
    }

    /// <summary>
    /// 用程式畫一個朝上的三角形箭頭圖示，存成 PNG + Sprite 資源。
    /// 如果之前已經產生過就直接沿用，不重複產生。
    /// </summary>
    private static Sprite GetOrCreateArrowSprite()
    {
        const string path = "Assets/Textures/PlayerArrowIcon.png";
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null) return existing;

        const int size = 64;
        Vector2 tip = new Vector2(size / 2f, size * 0.92f);
        Vector2 left = new Vector2(size * 0.12f, size * 0.12f);
        Vector2 right = new Vector2(size * 0.88f, size * 0.12f);

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                tex.SetPixel(x, y, IsInTriangle(p, tip, left, right) ? Color.white : Color.clear);
            }
        }
        tex.Apply();

        return SaveGeneratedIcon(tex, path);
    }

    /// <summary>
    /// 用程式畫一個「地圖圖釘」圖示（圓形 + 下方尖角），存成 PNG + Sprite 資源。
    /// </summary>
    private static Sprite GetOrCreatePinSprite()
    {
        const string path = "Assets/Textures/DestinationPinIcon.png";
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null) return existing;

        const int size = 64;
        Vector2 circleCenter = new Vector2(size / 2f, size * 0.68f);
        float radius = size * 0.28f;
        Vector2 tip = new Vector2(size / 2f, size * 0.04f);
        Vector2 left = new Vector2(size * 0.32f, size * 0.46f);
        Vector2 right = new Vector2(size * 0.68f, size * 0.46f);

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                bool inCircle = Vector2.Distance(p, circleCenter) <= radius;
                bool inTail = IsInTriangle(p, tip, left, right);
                tex.SetPixel(x, y, (inCircle || inTail) ? Color.white : Color.clear);
            }
        }
        tex.Apply();

        return SaveGeneratedIcon(tex, path);
    }

    private static bool IsInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = TriangleSign(p, a, b);
        float d2 = TriangleSign(p, b, c);
        float d3 = TriangleSign(p, c, a);

        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;

        return !(hasNeg && hasPos);
    }

    private static float TriangleSign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }

    /// <summary>把產生好的 Texture2D 編碼成 PNG 存到磁碟、匯入成 Sprite，並回傳結果。</summary>
    private static Sprite SaveGeneratedIcon(Texture2D tex, string assetPath)
    {
        string dir = Path.GetDirectoryName(assetPath);
        if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
        {
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(dir));
        }

        File.WriteAllBytes(assetPath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(assetPath);

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }

    private static GameObject CreateFullMapPanel(Transform canvasTransform, RenderTexture rt)
    {
        GameObject panel = new GameObject("FullMapPanel", typeof(RectTransform), typeof(RawImage));
        panel.transform.SetParent(canvasTransform, false);

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        // 貼齊四個邊，留一點邊界，展開時幾乎佔滿整個畫面
        panelRect.anchorMin = new Vector2(0.05f, 0.05f);
        panelRect.anchorMax = new Vector2(0.95f, 0.95f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        panel.GetComponent<RawImage>().texture = rt;

        panel.SetActive(false); // 平常收合，由 MinimapController 依按鍵切換顯示

        return panel;
    }

    private static void SetupNavigationUI(Canvas canvas, NavigationLineManager lineManager, Transform carTransform)
    {
        Transform existingPanel = canvas.transform.Find("TurnCardPanel");
        GameObject panelObj = existingPanel != null ? existingPanel.gameObject : CreateTurnCardPanel(canvas.transform);

        // 舊版安裝腳本把 NavigationUIManager 誤裝在 TurnCardPanel 本身上面——TurnCardPanel 會被
        // SetActive(false) 收合，物件一關掉，掛在同一個物件上的腳本的 Update() 也會跟著永久停止，
        // 之後不管距離多近都沒有機會再把自己打開（跟先前修過的 MinimapController 是同一種 bug）。
        // 這裡先清掉裝錯地方的舊元件，改成裝在一個常駐 Active 的獨立物件上。
        NavigationUIManager staleOnPanel = panelObj.GetComponent<NavigationUIManager>();
        if (staleOnPanel != null)
        {
            Object.DestroyImmediate(staleOnPanel);
        }

        GameObject managerObj = GameObject.Find("NavigationUIManager");
        if (managerObj == null)
        {
            managerObj = new GameObject("NavigationUIManager");
            managerObj.transform.SetParent(canvas.transform, false);
        }

        NavigationUIManager uiManager = managerObj.GetComponent<NavigationUIManager>();
        if (uiManager == null)
        {
            uiManager = managerObj.AddComponent<NavigationUIManager>();
        }

        uiManager.lineManager = lineManager;
        uiManager.player = carTransform;
        uiManager.turnCardPanel = panelObj;

        Transform iconT = panelObj.transform.Find("TurnIconImage");
        Transform distanceT = panelObj.transform.Find("DistanceText");
        Transform roadNameT = panelObj.transform.Find("RoadNameText");

        if (iconT != null) uiManager.turnIconImage = iconT.GetComponent<Image>();
        if (distanceT != null) uiManager.distanceText = distanceT.GetComponent<TMP_Text>();
        if (roadNameT != null) uiManager.roadNameText = roadNameT.GetComponent<TMP_Text>();

        // 獨立呼叫、不管 TurnCardPanel 是不是這次新建的都會執行——道理跟 ApplyMinimapIcons 一樣，
        // 場景裡已經有 TurnCardPanel 時就不會重新執行 CreateTurnCardPanel，取消按鈕會漏裝。
        uiManager.cancelButton = EnsureCancelButton(panelObj);

        panelObj.SetActive(false); // 初始隱藏，交由 NavigationUIManager 依距離控制顯示
    }

    private static GameObject CreateTurnCardPanel(Transform canvasTransform)
    {
        GameObject panel = new GameObject("TurnCardPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasTransform, false);

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.sizeDelta = new Vector2(520, 150);
        panelRect.anchoredPosition = new Vector2(0, -40);
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

        GameObject iconObj = new GameObject("TurnIconImage", typeof(RectTransform), typeof(Image));
        iconObj.transform.SetParent(panel.transform, false);
        RectTransform iconRect = iconObj.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0f, 0.5f);
        iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.sizeDelta = new Vector2(100, 100);
        iconRect.anchoredPosition = new Vector2(20, 0);
        iconObj.GetComponent<Image>().color = Color.white;

        GameObject distanceObj = new GameObject("DistanceText", typeof(RectTransform));
        distanceObj.transform.SetParent(panel.transform, false);
        TextMeshProUGUI distanceTMP = distanceObj.AddComponent<TextMeshProUGUI>();
        RectTransform distanceRect = distanceObj.GetComponent<RectTransform>();
        distanceRect.anchorMin = new Vector2(0f, 1f);
        distanceRect.anchorMax = new Vector2(1f, 1f);
        distanceRect.pivot = new Vector2(0f, 1f);
        distanceRect.sizeDelta = new Vector2(-140, 60);
        distanceRect.anchoredPosition = new Vector2(140, -10);
        distanceTMP.text = "100 m";
        distanceTMP.fontSize = 40;
        distanceTMP.fontStyle = FontStyles.Bold;
        distanceTMP.color = Color.white;
        distanceTMP.alignment = TextAlignmentOptions.Left;

        GameObject roadNameObj = new GameObject("RoadNameText", typeof(RectTransform));
        roadNameObj.transform.SetParent(panel.transform, false);
        TextMeshProUGUI roadNameTMP = roadNameObj.AddComponent<TextMeshProUGUI>();
        RectTransform roadRect = roadNameObj.GetComponent<RectTransform>();
        roadRect.anchorMin = new Vector2(0f, 0f);
        roadRect.anchorMax = new Vector2(1f, 1f);
        roadRect.pivot = new Vector2(0f, 0f);
        roadRect.sizeDelta = new Vector2(-140, -70);
        roadRect.anchoredPosition = new Vector2(140, 10);
        roadNameTMP.text = "右轉 路名";
        roadNameTMP.fontSize = 26;
        roadNameTMP.color = new Color(0.85f, 0.85f, 0.85f, 1f);
        roadNameTMP.alignment = TextAlignmentOptions.Left;

        EnsureCancelButton(panel);

        return panel;
    }

    /// <summary>
    /// 確保 panel 底下有一個 CancelButton 子物件，沒有的話才建立（冪等：可以重複呼叫）。
    /// 回傳該按鈕的 Button 元件。
    /// </summary>
    private static Button EnsureCancelButton(GameObject panel)
    {
        Transform existing = panel.transform.Find("CancelButton");
        if (existing != null)
        {
            return existing.GetComponent<Button>();
        }

        GameObject cancelObj = new GameObject("CancelButton", typeof(RectTransform), typeof(Image), typeof(Button));
        cancelObj.transform.SetParent(panel.transform, false);
        RectTransform cancelRect = cancelObj.GetComponent<RectTransform>();
        cancelRect.anchorMin = new Vector2(1f, 1f);
        cancelRect.anchorMax = new Vector2(1f, 1f);
        cancelRect.pivot = new Vector2(1f, 1f);
        cancelRect.sizeDelta = new Vector2(32, 32);
        cancelRect.anchoredPosition = new Vector2(-6, -6);
        cancelObj.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);

        GameObject cancelLabelObj = new GameObject("Label", typeof(RectTransform));
        cancelLabelObj.transform.SetParent(cancelObj.transform, false);
        TextMeshProUGUI cancelLabel = cancelLabelObj.AddComponent<TextMeshProUGUI>();
        RectTransform cancelLabelRect = cancelLabelObj.GetComponent<RectTransform>();
        cancelLabelRect.anchorMin = Vector2.zero;
        cancelLabelRect.anchorMax = Vector2.one;
        cancelLabelRect.offsetMin = Vector2.zero;
        cancelLabelRect.offsetMax = Vector2.zero;
        cancelLabel.text = "X"; // 刻意用純 ASCII 字元，避免預設字體(LiberationSans SDF)不支援特殊符號又跳警告
        cancelLabel.fontSize = 20;
        cancelLabel.color = Color.white;
        cancelLabel.alignment = TextAlignmentOptions.Center;

        return cancelObj.GetComponent<Button>();
    }
}
