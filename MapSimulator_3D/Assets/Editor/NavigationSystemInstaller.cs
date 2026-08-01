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
        SetupMinimap(canvas, car.transform, lineManager);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        EditorUtility.DisplayDialog(
            "安裝完成",
            "已在目前場景安裝：\n" +
            "・GoogleMapCamera（掛在 " + cam.name + "，並停用了原本的 CameraFollow）\n" +
            "・NavigationLineManager（NavigationManager 物件）\n" +
            "・NavigationUIManager（Canvas 底下的 TurnCardPanel）\n" +
            "・CarResetController（掛在車輛上，預設按 R 鍵重置回起點，掉出地圖也會自動重置）\n" +
            "・MinimapController（Canvas 右上角的 MinimapPanel，可點擊小地圖設定導航目的地）\n\n" +
            "還需要你手動完成：\n" +
            "1. 到 NavigationManager 的 Waypoints 清單填入實際路口座標（目前是示範用的暫時座標）\n" +
            "2. 到 TurnCardPanel 的 NavigationUIManager 上指定左轉/右轉/直行/迴轉的圖示 Sprite\n" +
            "3. 小地圖上的玩家箭頭/目的地標記目前是純色方塊，可以到 MinimapPanel 底下的 PlayerArrow / DestinationMarker 換成美術 Sprite\n" +
            "4. 確認無誤後記得存檔 (Ctrl+S)\n\n" +
            "注意：點小地圖設定目的地目前是「直線導航」，不會沿實際道路繞路，" +
            "因為場景裡還沒有建置道路節點圖。",
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
        UIFadeController[] faders = Object.FindObjectsOfType<UIFadeController>(true);
        if (faders.Length == 0)
        {
            EditorUtility.DisplayDialog("沒有找到", "目前場景中沒有任何 UIFadeController 元件。", "確定");
            return;
        }

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

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog(
            "完成",
            $"共找到 {faders.Length} 個 UIFadeController，已停用其中 {disabledCount} 個。\n" +
            "對應的 CanvasGroup alpha 已還原為 1、物件已確保為 Active。\n" +
            "記得存檔 (Ctrl+S)。",
            "確定");
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

        if (lineManager.waypoints == null || lineManager.waypoints.Count == 0)
        {
            lineManager.waypoints = CreateSampleWaypoints(carTransform);
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

    /// <summary>
    /// 建立兩個以車輛目前位置往前推算的示範路口節點，讓安裝完後可以立刻在 Play 模式看到效果。
    /// 實際專案請務必到 Inspector 換成真實的路口座標。
    /// </summary>
    private static List<NavWaypoint> CreateSampleWaypoints(Transform carTransform)
    {
        Vector3 forward = carTransform.forward;
        Vector3 start = carTransform.position;

        return new List<NavWaypoint>
        {
            new NavWaypoint
            {
                position = start + forward * 50f,
                turnType = TurnType.Straight,
                roadName = "(示範座標，請替換為實際路口 A)"
            },
            new NavWaypoint
            {
                position = start + forward * 100f,
                turnType = TurnType.TurnLeft,
                roadName = "(示範座標，請替換為實際路口 B)"
            }
        };
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

    private static void SetupMinimap(Canvas canvas, Transform carTransform, NavigationLineManager lineManager)
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

        Transform arrowT = panelObj.transform.Find("PlayerArrow");
        Transform destT = panelObj.transform.Find("DestinationMarker");
        if (arrowT != null) controller.playerArrow = arrowT.GetComponent<RectTransform>();
        if (destT != null) controller.destinationMarker = destT.GetComponent<RectTransform>();
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

    private static void SetupNavigationUI(Canvas canvas, NavigationLineManager lineManager, Transform carTransform)
    {
        Transform existingPanel = canvas.transform.Find("TurnCardPanel");
        GameObject panelObj = existingPanel != null ? existingPanel.gameObject : CreateTurnCardPanel(canvas.transform);

        NavigationUIManager uiManager = panelObj.GetComponent<NavigationUIManager>();
        if (uiManager == null)
        {
            uiManager = panelObj.AddComponent<NavigationUIManager>();
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
