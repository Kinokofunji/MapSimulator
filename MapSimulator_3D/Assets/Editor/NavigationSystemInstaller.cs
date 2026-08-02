using System.Collections.Generic;
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
            fontNote + "\n" +
            "還需要你手動完成：\n" +
            "1. 小地圖上的玩家箭頭/目的地標記目前是純色方塊，可以到 MinimapPanel 底下的 PlayerArrow / DestinationMarker 換成美術 Sprite\n" +
            "2. 確認無誤後記得存檔 (Ctrl+S)\n\n" +
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
        arrowRect.sizeDelta = new Vector2(20, 20);
        arrowRect.anchoredPosition = Vector2.zero;
        // 先用顯眼的黃色方塊代表玩家方向，之後可以到 PlayerArrow 換成箭頭 Sprite
        arrowObj.GetComponent<Image>().color = new Color(1f, 0.85f, 0f, 1f);

        GameObject destObj = new GameObject("DestinationMarker", typeof(RectTransform), typeof(Image));
        destObj.transform.SetParent(panel.transform, false);
        RectTransform destRect = destObj.GetComponent<RectTransform>();
        destRect.anchorMin = new Vector2(0.5f, 0.5f);
        destRect.anchorMax = new Vector2(0.5f, 0.5f);
        destRect.pivot = new Vector2(0.5f, 0.5f);
        destRect.sizeDelta = new Vector2(16, 16);
        // 先用紅色方塊代表目的地，之後可以到 DestinationMarker 換成圖釘 Sprite
        destObj.GetComponent<Image>().color = new Color(0.9f, 0.15f, 0.15f, 1f);
        destObj.SetActive(false);

        return panel;
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

        return panel;
    }
}
