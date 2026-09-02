using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Navigation.Tools
{
    /// <summary>
    /// ★ 編輯器工具 ★
    /// 一鍵在目前場景中建立 / 補齊導航系統所需的所有 GameObject、元件與 Inspector 欄位設定，
    /// 省去手動 Add Component、手動拖曳綁定的步驟。可重複執行，已存在的物件不會被重建或覆蓋。
    /// 使用方式：Unity 上方選單列 → Tools → Navigation → 一鍵建立導航場景物件
    /// </summary>
    public static class NavigationSceneSetup
    {
        private const string GeneratedIconFolder = "Assets/NavigationCore/Art/Icons";
        private const string GeneratedMaterialFolder = "Assets/NavigationCore/Art/Materials";
        private const string GeneratedFontFolder = "Assets/NavigationCore/Art/Fonts";

        /// <summary>
        /// 修正用工具：先前 SetupMotorcycle 曾經誤用 GameObject.Find 搜尋「已停用」的機車，
        /// 導致每次執行「一鍵建立」都誤判成不存在而重複建立，場景裡可能累積了多個
        /// PlayerMotorcycle 物件。這裡會保留 VehicleSwitcher 目前實際參照的那一個，
        /// 其餘的重複物件全部刪除。
        /// </summary>
        [MenuItem("Tools/Navigation/清理重複的機車物件")]
        public static void CleanupDuplicateMotorcycles()
        {
            GameObject navManagerObj = GameObject.Find("NavigationManager");
            VehicleSwitcher switcher = navManagerObj != null ? navManagerObj.GetComponent<VehicleSwitcher>() : null;

            Transform referencedMoto = null;
            if (switcher != null)
            {
                SerializedObject so = new SerializedObject(switcher);
                SerializedProperty vehiclesProp = so.FindProperty("vehicles");
                for (int i = 0; i < vehiclesProp.arraySize; i++)
                {
                    Object vehicleObj = vehiclesProp.GetArrayElementAtIndex(i).objectReferenceValue;
                    Transform vehicleTransform = vehicleObj as Transform;
                    if (vehicleTransform != null && vehicleTransform.name == "PlayerMotorcycle")
                    {
                        referencedMoto = vehicleTransform;
                        break;
                    }
                }
            }

            System.Collections.Generic.List<GameObject> allMotos = new System.Collections.Generic.List<GameObject>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    CollectAllByName(root.transform, "PlayerMotorcycle", allMotos);
                }
            }

            if (allMotos.Count <= 1)
            {
                EditorUtility.DisplayDialog("不需要清理", $"場景中只找到 {allMotos.Count} 個 PlayerMotorcycle，沒有重複物件需要清理。", "好");
                return;
            }

            GameObject keep = referencedMoto != null ? referencedMoto.gameObject : allMotos[0];
            int deletedCount = 0;

            foreach (GameObject moto in allMotos)
            {
                if (moto != keep)
                {
                    Undo.DestroyObjectImmediate(moto);
                    deletedCount++;
                }
            }

            Debug.Log($"[NavigationSceneSetup] 清理完成，刪除了 {deletedCount} 個重複的 PlayerMotorcycle，保留「{keep.name}」（InstanceID: {keep.GetInstanceID()}）。");

            EditorUtility.DisplayDialog("完成", $"已刪除 {deletedCount} 個重複的機車物件，保留 1 個。請記得按 Ctrl+S 存檔。", "好");
        }

        private static void CollectAllByName(Transform current, string name, System.Collections.Generic.List<GameObject> results)
        {
            if (current.name == name)
            {
                results.Add(current.gameObject);
            }

            foreach (Transform child in current)
            {
                CollectAllByName(child, name, results);
            }
        }

        [MenuItem("Tools/Navigation/一鍵建立導航場景物件")]
        public static void SetupScene()
        {
            GameObject player = FindPlayerVehicleRobust();
            if (player == null)
            {
                LogAllLoadedSceneHierarchies();

                EditorUtility.DisplayDialog("找不到 PlayerVehicle",
                    "場景中找不到名稱為 PlayerVehicle 的物件，請先建立測試方塊並命名為 PlayerVehicle 後再執行。\n\n" +
                    "詳細診斷資訊（所有已載入場景的完整物件清單）已輸出到 Console，請展開黃色警告訊息查看。",
                    "了解");
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                EditorUtility.DisplayDialog("找不到 Main Camera",
                    "場景中找不到標記為 MainCamera 的攝影機，請確認 Main Camera 的 Tag 是否為 MainCamera。",
                    "了解");
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("一鍵建立導航場景物件");

            SetupSafetyFloor(player);

            SetupCamera(mainCamera.gameObject, player.transform);
            GoogleMapCamera cameraComponent = mainCamera.gameObject.GetComponent<GoogleMapCamera>();

            GameObject navManager = SetupNavigationManager(player.transform);
            NavigationLineManager lineManager = navManager.GetComponent<NavigationLineManager>();

            VehiclePhysicsController carController = AddVehiclePhysicsRig(player, VehicleTuning.Car);
            AutoDriveController carAutoDrive = SetupAutoDrive(player, lineManager);
            SetupDriveModeSwitcher(player, carController, carAutoDrive);
            ImproveVehicleVisual(player, isMotorcycle: false);

            GameObject motorcycle = SetupMotorcycle(player, lineManager);

            Canvas canvas = FindOrCreateCanvas();
            GameObject card = CreateNavigationCard(canvas.transform);
            SetupUIManager(navManager, card, player.transform);
            NavigationUIManager uiManager = navManager.GetComponent<NavigationUIManager>();

            GameObject speedometerObj = CreateSpeedometerUI(canvas.transform);
            SpeedometerUI speedometer = SetupSpeedometer(speedometerObj, player);

            GameObject etaBarObj = CreateETABarUI(canvas.transform);
            ETADisplayUI etaDisplay = SetupETADisplay(etaBarObj, lineManager, player);

            SetupVehicleSwitcher(navManager, player, motorcycle, cameraComponent, lineManager, uiManager, speedometer, etaDisplay);

            Undo.CollapseUndoOperations(undoGroup);

            Debug.Log("[NavigationSceneSetup] 導航場景物件建立 / 補齊完成。");
            EditorUtility.DisplayDialog("完成",
                "導航場景物件已自動建立完成！\n\n" +
                "操作方式：\n" +
                "・W/S 或 ↑/↓：加速／煞車（後退需先停下再倒車）\n" +
                "・A/D 或 ←/→：轉向\n" +
                "・空白鍵：煞車\n" +
                "・按住滑鼠右鍵：自由環顧周邊路景，放開自動歸位\n" +
                "・V：切換汽車／機車\n" +
                "・Tab：切換「手動物理駕駛」與「自動沿路線行進」\n\n" +
                "請到 Hierarchy 檢查 NavigationManager 與 Canvas，並記得按 Ctrl+S 儲存場景。\n\n" +
                "若要修改真實路口座標，請到 NavigationManager 的 Navigation Line Manager 元件展開 Waypoints 清單修改。",
                "好");
        }

        /// <summary>兩種載具的物理調校參數（僅供工具內部使用的簡單資料結構）。</summary>
        private readonly struct VehiclePhysicsTuning
        {
            public readonly float TrackHalfWidth;
            public readonly float WheelBaseHalfLength;
            public readonly float Mass;
            public readonly float MaxMotorTorque;
            public readonly float MaxSteerAngle;
            public readonly float BrakeTorque;

            public VehiclePhysicsTuning(float trackHalfWidth, float wheelBaseHalfLength, float mass, float maxMotorTorque, float maxSteerAngle, float brakeTorque)
            {
                TrackHalfWidth = trackHalfWidth;
                WheelBaseHalfLength = wheelBaseHalfLength;
                Mass = mass;
                MaxMotorTorque = maxMotorTorque;
                MaxSteerAngle = maxSteerAngle;
                BrakeTorque = brakeTorque;
            }
        }

        private static class VehicleTuning
        {
            // 汽車：較重、較穩，轉向角較小
            public static readonly VehiclePhysicsTuning Car = new VehiclePhysicsTuning(
                trackHalfWidth: 0.9f, wheelBaseHalfLength: 1.3f, mass: 1200f,
                maxMotorTorque: 1500f, maxSteerAngle: 30f, brakeTorque: 3000f);

            // 機車：較輕、車身較窄，轉向角較大、煞車距離較短（占位用參數，非真實兩輪機車物理模擬）
            public static readonly VehiclePhysicsTuning Motorcycle = new VehiclePhysicsTuning(
                trackHalfWidth: 0.35f, wheelBaseHalfLength: 1.0f, mass: 220f,
                maxMotorTorque: 900f, maxSteerAngle: 40f, brakeTorque: 1500f);
        }

        /// <summary>
        /// 遞迴搜尋所有「已載入」場景中的物件（不限根物件、不限啟用狀態的父層是否剛好符合 GameObject.Find 的限制），
        /// 用來繞過 GameObject.Find 只搜尋單一啟用中根物件、可能因多場景載入或巢狀結構而找不到物件的問題。
        /// </summary>
        private static GameObject FindPlayerVehicleRobust()
        {
            GameObject found = GameObject.Find("PlayerVehicle");
            if (found != null)
            {
                return found;
            }

            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    GameObject match = FindInChildrenByName(root.transform, "PlayerVehicle");
                    if (match != null)
                    {
                        return match;
                    }
                }
            }

            return null;
        }

        private static GameObject FindInChildrenByName(Transform current, string name)
        {
            // 使用 Trim() 比對，避免物件名稱前後不小心多打空白字元導致比對失敗
            if (current.name.Trim() == name)
            {
                return current.gameObject;
            }

            foreach (Transform child in current)
            {
                GameObject match = FindInChildrenByName(child, name);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        /// <summary>把目前所有已載入場景的完整物件階層（含縮排、含子物件）輸出到 Console，方便排查找不到物件的原因。</summary>
        private static void LogAllLoadedSceneHierarchies()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"目前共有 {UnityEngine.SceneManagement.SceneManager.sceneCount} 個場景已載入：");

            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                sb.AppendLine($"[場景 {i}] 名稱：「{scene.name}」  已載入：{scene.isLoaded}  是否為作用中場景：{scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene()}");

                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    AppendHierarchy(sb, root.transform, 1);
                }
            }

            Debug.LogWarning(sb.ToString());
        }

        private static void AppendHierarchy(System.Text.StringBuilder sb, Transform t, int depth)
        {
            sb.AppendLine($"{new string(' ', depth * 2)}- 「{t.name}」 (active: {t.gameObject.activeSelf})");
            foreach (Transform child in t)
            {
                AppendHierarchy(sb, child, depth + 1);
            }
        }

        /// <summary>設定 GoogleMapCamera 並綁定跟隨目標。</summary>
        private static void SetupCamera(GameObject cameraObj, Transform playerTransform)
        {
            GoogleMapCamera camScript = cameraObj.GetComponent<GoogleMapCamera>();
            if (camScript == null)
            {
                camScript = Undo.AddComponent<GoogleMapCamera>(cameraObj);
            }

            SerializedObject so = new SerializedObject(camScript);
            so.FindProperty("target").objectReferenceValue = playerTransform;
            so.ApplyModifiedProperties();
        }




        /// <summary>建立（或沿用既有的）NavigationManager 物件，掛上 LineRenderer 與 NavigationLineManager。</summary>
        private static GameObject SetupNavigationManager(Transform playerTransform)
        {
            GameObject navManager = GameObject.Find("NavigationManager");
            if (navManager == null)
            {
                navManager = new GameObject("NavigationManager");
                Undo.RegisterCreatedObjectUndo(navManager, "Create NavigationManager");
            }

            LineRenderer lineRenderer = navManager.GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                lineRenderer = Undo.AddComponent<LineRenderer>(navManager);
            }

            lineRenderer.useWorldSpace = true;
            lineRenderer.widthMultiplier = 1f;
            lineRenderer.numCapVertices = 4;
            lineRenderer.numCornerVertices = 4;
            lineRenderer.sharedMaterial = GetOrCreateLineMaterial();
            lineRenderer.startColor = new Color(0.25f, 1.1f, 2.2f);
            lineRenderer.endColor = new Color(0.25f, 1.1f, 2.2f);

            NavigationLineManager lineManager = navManager.GetComponent<NavigationLineManager>();
            if (lineManager == null)
            {
                lineManager = Undo.AddComponent<NavigationLineManager>(navManager);
            }

            SerializedObject so = new SerializedObject(lineManager);
            so.FindProperty("player").objectReferenceValue = playerTransform;

            // 若尚未設定任何路口座標，於玩家前方自動放 3 個測試路口，方便先行測試流程是否正常
            SerializedProperty waypointsProp = so.FindProperty("waypoints");
            SerializedProperty infosProp = so.FindProperty("waypointInfos");

            if (waypointsProp.arraySize == 0)
            {
                Vector3 basePos = playerTransform.position;
                Vector3 forward = playerTransform.forward;
                Vector3[] testPoints =
                {
                    basePos + forward * 50f,
                    basePos + forward * 100f,
                    basePos + forward * 150f
                };

                waypointsProp.arraySize = testPoints.Length;
                infosProp.arraySize = testPoints.Length;

                for (int i = 0; i < testPoints.Length; i++)
                {
                    waypointsProp.GetArrayElementAtIndex(i).vector3Value = testPoints[i];

                    SerializedProperty infoElement = infosProp.GetArrayElementAtIndex(i);
                    infoElement.FindPropertyRelative("turnType").enumValueIndex = i % 4;
                    infoElement.FindPropertyRelative("roadName").stringValue = $"測試路口 {i + 1}";
                }
            }

            so.ApplyModifiedProperties();
            return navManager;
        }


        // Cesium ion 的全域公開 asset：Cesium World Terrain（地形，提供地面/道路的實際碰撞與高度）、
        // Cesium OSM Buildings（簡化擠出建築物）、Bing Maps Aerial（衛星空拍影像，貼在地形上讓
        // 路面/地面有真實顏色與紋理，這是「看起來像 Google Maps」的關鍵——Google 平常的導航畫面
        // 本身就是空拍/向量地圖影像貼在簡化地形與建築上，並不是照片級 3D 掃描）。
        private const long OsmBuildingsAssetId = 96188;
        private const long BingMapsAerialAssetId = 2;





        /// <summary>查詢指定 Unity 座標所在經緯度的真實地面高度，回傳對應的 Unity Y 座標。</summary>
        // Google Elevation API 回傳的高度是「平均海平面（EGM96 大地水準面）基準」，
        // 但 Cesium 的座標系統要用「WGS84 橢球高度」。台灣地區的 EGM96 大地水準面高度（geoid undulation）
        // 大約落在 +20 公尺左右（橢球高度 ≈ 海平面高度 + 大地水準面高度），這裡用這個近似值做修正，
        // 避免兩種高度基準沒對齊，導致物件被放到比實際地面低了一截的地方。
        private const double TaiwanGeoidUndulationMeters = 20.0;


        /// <summary>遞迴搜尋所有已載入場景中的物件（含停用中的物件），GameObject.Find 找不到停用物件時使用。</summary>
        private static GameObject FindObjectByNameIncludingInactive(string objectName)
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    GameObject match = FindInChildrenByName(root.transform, objectName);
                    if (match != null)
                    {
                        return match;
                    }
                }
            }

            return null;
        }



        /// <summary>Cesium 效能調校用的保守預設值，在啟用物理碰撞網格的前提下維持可接受的即時運算效能。</summary>
        private const float PerformanceFriendlyMaxScreenSpaceError = 24f;
        private const uint PerformanceFriendlyMaxSimultaneousTileLoads = 6;
        private const uint PerformanceFriendlyLoadingDescendantLimit = 10;


        /// <summary>
        /// 在車輛起始位置正下方建立一塊很大、隱形的安全地板（BoxCollider），做為最後一道防線。
        /// Cesium 的物理碰撞網格是非同步串流載入的，剛啟用 Create Physics Meshes 或剛進入 Play
        /// 時真實地形碰撞可能還沒生成好，沒有安全地板的話車輛會直接無限往下掉。
        /// 因為安全地板刻意放在車輛起始高度下方，只要 Cesium 真實地形碰撞就緒，車輪會先接觸到
        /// 更高的真實路面，不會真的碰到安全地板；只有在真實碰撞來不及載入時才會派上用場。
        /// </summary>
        private static void SetupSafetyFloor(GameObject player)
        {
            GameObject floor = GameObject.Find("NavigationSafetyFloor");
            if (floor == null)
            {
                floor = new GameObject("NavigationSafetyFloor", typeof(BoxCollider));
                Undo.RegisterCreatedObjectUndo(floor, "Create NavigationSafetyFloor");
            }

            BoxCollider floorCollider = floor.GetComponent<BoxCollider>();
            floorCollider.size = new Vector3(4000f, 1f, 4000f);

            Vector3 playerPos = player.transform.position;
            floor.transform.position = new Vector3(playerPos.x, playerPos.y - 3f, playerPos.z);
        }

        /// <summary>
        /// 幫指定載具掛上 Rigidbody 與四個 WheelCollider，並加上 VehiclePhysicsController，
        /// 建立具備加速、煞車、轉向、碰撞的物理車輛，對應報告要求的「擬真車輛駕駛」。
        /// 依傳入的調校參數，同一套邏輯可以同時用來設定汽車與機車兩種不同手感的載具。
        /// </summary>
        private static VehiclePhysicsController AddVehiclePhysicsRig(GameObject vehicleObject, VehiclePhysicsTuning tuning)
        {
            Rigidbody rb = vehicleObject.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = Undo.AddComponent<Rigidbody>(vehicleObject);
            }

            rb.mass = tuning.Mass;
            rb.drag = 0.05f;
            rb.angularDrag = 0.5f;

            // 只允許車輛繞 Y 軸旋轉（方向盤轉向），禁止俯仰/側翻。這是賽車類遊戲常見的簡化：
            // 在密集的照片級 3D 建築場景中，車輛偶爾會因為撞到建築物邊緣、或剛落地時的瞬間
            // 衝擊力而翻覆，翻覆後畫面會呈現詭異的顛倒視角。與其處理所有可能導致翻覆的物理
            // 邊界情況，直接鎖住俯仰/側翻的旋轉自由度更穩定可靠。
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            // 把方塊本身的碰撞體往上抬，避免車身碰撞體跟輪胎同時壓在地面上互相打架
            CapsuleCollider bodyCollider = vehicleObject.GetComponent<CapsuleCollider>();
            if (bodyCollider != null)
            {
                bodyCollider.center = new Vector3(0f, 0.5f, 0f);
            }

            float tw = tuning.TrackHalfWidth;
            float wb = tuning.WheelBaseHalfLength;
            WheelCollider frontLeft = CreateWheelCollider(vehicleObject.transform, "WheelCollider_FrontLeft", new Vector3(-tw, -0.5f, wb));
            WheelCollider frontRight = CreateWheelCollider(vehicleObject.transform, "WheelCollider_FrontRight", new Vector3(tw, -0.5f, wb));
            WheelCollider rearLeft = CreateWheelCollider(vehicleObject.transform, "WheelCollider_RearLeft", new Vector3(-tw, -0.5f, -wb));
            WheelCollider rearRight = CreateWheelCollider(vehicleObject.transform, "WheelCollider_RearRight", new Vector3(tw, -0.5f, -wb));

            VehiclePhysicsController controller = vehicleObject.GetComponent<VehiclePhysicsController>();
            if (controller == null)
            {
                controller = Undo.AddComponent<VehiclePhysicsController>(vehicleObject);
            }

            SerializedObject so = new SerializedObject(controller);
            so.FindProperty("frontLeftWheel").objectReferenceValue = frontLeft;
            so.FindProperty("frontRightWheel").objectReferenceValue = frontRight;
            so.FindProperty("rearLeftWheel").objectReferenceValue = rearLeft;
            so.FindProperty("rearRightWheel").objectReferenceValue = rearRight;
            so.FindProperty("maxMotorTorque").floatValue = tuning.MaxMotorTorque;
            so.FindProperty("maxSteerAngle").floatValue = tuning.MaxSteerAngle;
            so.FindProperty("brakeTorque").floatValue = tuning.BrakeTorque;
            so.ApplyModifiedProperties();

            return controller;
        }

        /// <summary>
        /// 建立（或沿用既有的）機車載具：以汽車目前的位置為起點複製出一台較輕、較窄、
        /// 轉向較靈活的機車，並掛上跟汽車相同的一整套駕駛腳本（手動物理／自動路線預演）。
        /// </summary>
        private static GameObject SetupMotorcycle(GameObject carVehicle, NavigationLineManager lineManager)
        {
            // 機車一開始就會被停用（SetActive(false)），GameObject.Find 找不到停用物件，
            // 若這裡誤用 GameObject.Find，每次重新執行都會判斷成「還沒有機車」而重複建立新的。
            GameObject moto = FindObjectByNameIncludingInactive("PlayerMotorcycle");
            bool isNew = moto == null;

            if (isNew)
            {
                moto = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                moto.name = "PlayerMotorcycle";
                Undo.RegisterCreatedObjectUndo(moto, "Create PlayerMotorcycle");
            }

            moto.transform.position = carVehicle.transform.position;
            moto.transform.rotation = carVehicle.transform.rotation;

            AddVehiclePhysicsRig(moto, VehicleTuning.Motorcycle);

            AutoDriveController motoAutoDrive = SetupAutoDrive(moto, lineManager);
            VehiclePhysicsController motoController = moto.GetComponent<VehiclePhysicsController>();
            SetupDriveModeSwitcher(moto, motoController, motoAutoDrive);
            ImproveVehicleVisual(moto, isMotorcycle: true);

            // 一開始預設啟用汽車、機車先關閉，由 VehicleSwitcher 在 Start() 時決定啟用哪一台
            moto.SetActive(false);

            return moto;
        }

        /// <summary>
        /// 把預設的膠囊占位模型換成簡單的「車身＋座艙／車身＋騎士」方塊組合，
        /// 視覺上比單一膠囊更接近車輛輪廓（不是正式美術資產，但比膠囊好辨識）。
        /// 同時把根物件的縮放重設為 1（避免非等比例縮放連帶影響 WheelCollider 子物件的實際世界座標），
        /// 車型大小差異改用 CapsuleCollider 的 radius/height 直接調整。
        /// </summary>
        private static void ImproveVehicleVisual(GameObject vehicle, bool isMotorcycle)
        {
            vehicle.transform.localScale = Vector3.one;

            MeshRenderer rootRenderer = vehicle.GetComponent<MeshRenderer>();
            if (rootRenderer != null)
            {
                rootRenderer.enabled = false;
            }

            CapsuleCollider bodyCollider = vehicle.GetComponent<CapsuleCollider>();
            if (bodyCollider != null)
            {
                bodyCollider.radius = isMotorcycle ? 0.35f : 0.5f;
                bodyCollider.height = isMotorcycle ? 1.6f : 2f;
            }

            if (vehicle.transform.Find("VehicleVisual") != null)
            {
                return;
            }

            GameObject visualRoot = new GameObject("VehicleVisual");
            Undo.RegisterCreatedObjectUndo(visualRoot, "Create VehicleVisual");
            visualRoot.transform.SetParent(vehicle.transform, false);

            if (isMotorcycle)
            {
                CreateVisualBox(visualRoot.transform, "Body", new Vector3(0.5f, 0.9f, 1.9f), new Vector3(0f, 0.1f, 0f), new Color(0.82f, 0.1f, 0.12f));
                CreateVisualBox(visualRoot.transform, "Rider", new Vector3(0.4f, 0.6f, 0.5f), new Vector3(0f, 0.75f, -0.1f), new Color(0.15f, 0.15f, 0.18f));
            }
            else
            {
                CreateVisualBox(visualRoot.transform, "Body", new Vector3(1.8f, 0.55f, 4.2f), new Vector3(0f, -0.05f, 0f), Color.white);
                CreateVisualBox(visualRoot.transform, "Cabin", new Vector3(1.5f, 0.5f, 2.1f), new Vector3(0f, 0.45f, -0.2f), new Color(0.1f, 0.12f, 0.16f));
            }
        }

        private static void CreateVisualBox(Transform parent, string name, Vector3 size, Vector3 localPosition, Color color)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            Undo.RegisterCreatedObjectUndo(box, $"Create {name}");

            Collider boxCollider = box.GetComponent<Collider>();
            if (boxCollider != null)
            {
                Undo.DestroyObjectImmediate(boxCollider);
            }

            box.transform.SetParent(parent, false);
            box.transform.localScale = size;
            box.transform.localPosition = localPosition;

            MeshRenderer renderer = box.GetComponent<MeshRenderer>();
            Material material = new Material(NavigationShaderCompat.Lit()) { color = color };
            renderer.sharedMaterial = material;
        }

        private static WheelCollider CreateWheelCollider(Transform parent, string name, Vector3 localPosition)
        {
            Transform existing = parent.Find(name);
            GameObject wheelObj = existing != null ? existing.gameObject : null;

            if (wheelObj == null)
            {
                wheelObj = new GameObject(name, typeof(WheelCollider));
                Undo.RegisterCreatedObjectUndo(wheelObj, $"Create {name}");
                wheelObj.transform.SetParent(parent, false);
            }

            wheelObj.transform.localPosition = localPosition;

            WheelCollider wheel = wheelObj.GetComponent<WheelCollider>();
            wheel.radius = 0.35f;
            wheel.suspensionDistance = 0.3f;
            wheel.mass = 20f;

            JointSpring spring = wheel.suspensionSpring;
            spring.spring = 35000f;
            spring.damper = 4500f;
            spring.targetPosition = 0.5f;
            wheel.suspensionSpring = spring;

            return wheel;
        }

        /// <summary>
        /// 在 PlayerVehicle 上掛好 AutoDriveController，讓車輛可以自動沿導航路線行進
        /// （做為手動駕駛之外的「路線預演」模式）。
        /// </summary>
        private static AutoDriveController SetupAutoDrive(GameObject player, NavigationLineManager lineManager)
        {
            AutoDriveController autoDrive = player.GetComponent<AutoDriveController>();
            if (autoDrive == null)
            {
                autoDrive = Undo.AddComponent<AutoDriveController>(player);
            }

            SerializedObject so = new SerializedObject(autoDrive);
            so.FindProperty("lineManager").objectReferenceValue = lineManager;
            so.ApplyModifiedProperties();

            return autoDrive;
        }

        /// <summary>掛上 DriveModeSwitcher，讓玩家可以按 Tab 在「手動物理駕駛」與「自動沿路線行進」之間切換。</summary>
        private static void SetupDriveModeSwitcher(GameObject player, VehiclePhysicsController manualController, AutoDriveController autoController)
        {
            DriveModeSwitcher switcher = player.GetComponent<DriveModeSwitcher>();
            if (switcher == null)
            {
                switcher = Undo.AddComponent<DriveModeSwitcher>(player);
            }

            SerializedObject so = new SerializedObject(switcher);
            so.FindProperty("manualController").objectReferenceValue = manualController;
            so.FindProperty("autoController").objectReferenceValue = autoController;
            so.FindProperty("vehicleRigidbody").objectReferenceValue = player.GetComponent<Rigidbody>();
            so.ApplyModifiedProperties();

            // 讓「編輯時存檔的狀態」就直接是正確的預設模式（手動物理駕駛開、自動駕駛關），
            // 不要只依賴 DriveModeSwitcher.Start() 在 Play 一開始才修正，避免兩套駕駛邏輯
            // 在存檔的當下就同時是「啟用」的狀態、造成混淆或潛在的邊際情況。
            SerializedObject manualSo = new SerializedObject(manualController);
            manualSo.FindProperty("m_Enabled").boolValue = true;
            manualSo.ApplyModifiedProperties();

            SerializedObject autoSo = new SerializedObject(autoController);
            autoSo.FindProperty("m_Enabled").boolValue = false;
            autoSo.ApplyModifiedProperties();

            Rigidbody rb = player.GetComponent<Rigidbody>();
            if (rb != null)
            {
                SerializedObject rbSo = new SerializedObject(rb);
                SerializedProperty kinematicProp = rbSo.FindProperty("m_IsKinematic");
                if (kinematicProp != null)
                {
                    kinematicProp.boolValue = false;
                    rbSo.ApplyModifiedProperties();
                }
            }
        }

        /// <summary>找到場景中既有的 Canvas，若沒有則建立一個 Screen Space - Overlay 的 Canvas。</summary>
        private static Canvas FindOrCreateCanvas()
        {
#pragma warning disable CS0618 // Unity 2022.3 相容：FindObjectOfType 在較新版本才被標為過時，此處刻意使用以確保跨版本編譯成功
            Canvas canvas = Object.FindObjectOfType<Canvas>();
#pragma warning restore CS0618
            if (canvas != null)
            {
                return canvas;
            }

            GameObject canvasObj = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasObj, "Create Canvas");

            canvas = canvasObj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObj.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            return canvas;
        }

        /// <summary>
        /// 在 Canvas 下建立 Google Maps 風格的導航卡片 UI 階層。
        /// 注意：每次執行都會「重建」卡片外觀（先刪除舊的再重新產生），
        /// 這是設計調整階段刻意的行為，方便重複套用最新樣式；若之後在 Inspector 手動微調過卡片，
        /// 重新執行這個工具會蓋掉那些手動調整。
        /// </summary>
        private static GameObject CreateNavigationCard(Transform canvasTransform)
        {
            Transform existing = canvasTransform.Find("NavigationCard");
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            Sprite roundedRectSprite = GetOrCreateRoundedRectSprite();
            Sprite circleSprite = GetOrCreateCircleSprite();
            TMP_FontAsset chineseFont = GetOrCreateChineseFontAsset();

            // 卡片本體：深色圓角背景 + 陰影
            GameObject card = new GameObject("NavigationCard", typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(card, "Create NavigationCard");
            card.transform.SetParent(canvasTransform, false);

            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 1f);
            cardRect.anchorMax = new Vector2(0.5f, 1f);
            cardRect.pivot = new Vector2(0.5f, 1f);
            cardRect.anchoredPosition = new Vector2(0f, -40f);
            cardRect.sizeDelta = new Vector2(620f, 170f);

            Image cardImage = card.GetComponent<Image>();
            cardImage.sprite = roundedRectSprite;
            cardImage.type = Image.Type.Sliced;
            cardImage.color = new Color(0.10f, 0.11f, 0.13f, 0.94f);

            Shadow cardShadow = card.AddComponent<Shadow>();
            cardShadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
            cardShadow.effectDistance = new Vector2(0f, -6f);

            // 圓形深青綠色底 + 白色箭頭圖示（參考 Google Maps 導航畫面的轉彎指標配色）
            GameObject badgeObj = new GameObject("IconBadge", typeof(RectTransform), typeof(Image));
            badgeObj.transform.SetParent(card.transform, false);
            RectTransform badgeRect = badgeObj.GetComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(0f, 0.5f);
            badgeRect.anchorMax = new Vector2(0f, 0.5f);
            badgeRect.pivot = new Vector2(0f, 0.5f);
            badgeRect.anchoredPosition = new Vector2(28f, 0f);
            badgeRect.sizeDelta = new Vector2(118f, 118f);
            Image badgeImage = badgeObj.GetComponent<Image>();
            badgeImage.sprite = circleSprite;
            badgeImage.color = new Color(0.02f, 0.44f, 0.42f); // 深青綠色

            GameObject iconObj = new GameObject("TurnIconImage", typeof(RectTransform), typeof(Image));
            iconObj.transform.SetParent(badgeObj.transform, false);
            RectTransform iconRect = iconObj.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = Vector2.zero;
            iconRect.sizeDelta = new Vector2(66f, 66f);
            Image iconImage = iconObj.GetComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.color = Color.white;

            // 剩餘距離：大字體、粗體、置於卡片右上
            GameObject distanceObj = new GameObject("DistanceText", typeof(RectTransform), typeof(TextMeshProUGUI));
            distanceObj.transform.SetParent(card.transform, false);
            RectTransform distRect = distanceObj.GetComponent<RectTransform>();
            distRect.anchorMin = new Vector2(0f, 1f);
            distRect.anchorMax = new Vector2(1f, 1f);
            distRect.pivot = new Vector2(0f, 1f);
            distRect.anchoredPosition = new Vector2(164f, -22f);
            distRect.sizeDelta = new Vector2(-192f, 68f);
            TextMeshProUGUI distanceText = distanceObj.GetComponent<TextMeshProUGUI>();
            distanceText.text = "0 m";
            distanceText.fontSize = 46f;
            distanceText.fontStyle = FontStyles.Bold;
            distanceText.color = Color.white;
            distanceText.alignment = TextAlignmentOptions.Left;
            if (chineseFont != null)
            {
                distanceText.font = chineseFont;
            }

            // 路名提示：淺灰色小字，置於距離文字下方
            GameObject roadNameObj = new GameObject("RoadNameText", typeof(RectTransform), typeof(TextMeshProUGUI));
            roadNameObj.transform.SetParent(card.transform, false);
            RectTransform roadRect = roadNameObj.GetComponent<RectTransform>();
            roadRect.anchorMin = new Vector2(0f, 0f);
            roadRect.anchorMax = new Vector2(1f, 0f);
            roadRect.pivot = new Vector2(0f, 0f);
            roadRect.anchoredPosition = new Vector2(164f, 22f);
            roadRect.sizeDelta = new Vector2(-192f, 48f);
            TextMeshProUGUI roadText = roadNameObj.GetComponent<TextMeshProUGUI>();
            roadText.text = "路名提示";
            roadText.fontSize = 27f;
            roadText.color = new Color(0.75f, 0.76f, 0.8f);
            roadText.alignment = TextAlignmentOptions.Left;
            if (chineseFont != null)
            {
                roadText.font = chineseFont;
            }

            // 預設先隱藏，實際顯示/隱藏交給 NavigationUIManager 依距離即時控制
            card.SetActive(false);

            return card;
        }

        /// <summary>
        /// 在 Canvas 右下角建立簡易儀表板 UI（顯示即時車速），對應報告系統架構裡
        /// 「展示層...呈現 3D 渲染畫面、儀表板與選單介面」的需求。
        /// </summary>
        private static GameObject CreateSpeedometerUI(Transform canvasTransform)
        {
            Transform existing = canvasTransform.Find("SpeedometerPanel");
            if (existing != null)
            {
                return existing.gameObject;
            }

            TMP_FontAsset chineseFont = GetOrCreateChineseFontAsset();
            Sprite roundedRectSprite = GetOrCreateRoundedRectSprite();

            GameObject panel = new GameObject("SpeedometerPanel", typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(panel, "Create SpeedometerPanel");
            panel.transform.SetParent(canvasTransform, false);

            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1f, 0f);
            panelRect.anchorMax = new Vector2(1f, 0f);
            panelRect.pivot = new Vector2(1f, 0f);
            panelRect.anchoredPosition = new Vector2(-30f, 30f);
            panelRect.sizeDelta = new Vector2(180f, 100f);

            Image panelImage = panel.GetComponent<Image>();
            panelImage.sprite = roundedRectSprite;
            panelImage.type = Image.Type.Sliced;
            panelImage.color = new Color(0.10f, 0.11f, 0.13f, 0.85f);

            GameObject speedTextObj = new GameObject("SpeedText", typeof(RectTransform), typeof(TextMeshProUGUI));
            speedTextObj.transform.SetParent(panel.transform, false);
            RectTransform speedRect = speedTextObj.GetComponent<RectTransform>();
            speedRect.anchorMin = Vector2.zero;
            speedRect.anchorMax = Vector2.one;
            speedRect.sizeDelta = Vector2.zero;
            speedRect.anchoredPosition = Vector2.zero;

            TextMeshProUGUI speedText = speedTextObj.GetComponent<TextMeshProUGUI>();
            speedText.text = "0 km/h";
            speedText.fontSize = 34f;
            speedText.fontStyle = FontStyles.Bold;
            speedText.color = Color.white;
            speedText.alignment = TextAlignmentOptions.Center;
            if (chineseFont != null)
            {
                speedText.font = chineseFont;
            }

            return panel;
        }

        /// <summary>把 SpeedometerUI 掛到儀表板面板上，並綁定目前啟用中載具的 Rigidbody。</summary>
        private static SpeedometerUI SetupSpeedometer(GameObject speedometerPanel, GameObject initialVehicle)
        {
            SpeedometerUI speedometer = speedometerPanel.GetComponent<SpeedometerUI>();
            if (speedometer == null)
            {
                speedometer = Undo.AddComponent<SpeedometerUI>(speedometerPanel);
            }

            TextMeshProUGUI speedText = speedometerPanel.transform.Find("SpeedText").GetComponent<TextMeshProUGUI>();

            SerializedObject so = new SerializedObject(speedometer);
            so.FindProperty("targetRigidbody").objectReferenceValue = initialVehicle.GetComponent<Rigidbody>();
            so.FindProperty("speedText").objectReferenceValue = speedText;
            so.ApplyModifiedProperties();

            return speedometer;
        }

        /// <summary>
        /// 在 Canvas 左下角建立「預估到達時間」資訊列（抵達時間／剩餘時間／剩餘距離三欄），
        /// 對應 Google Maps 導航畫面底部常見的行程摘要列。
        /// </summary>
        private static GameObject CreateETABarUI(Transform canvasTransform)
        {
            Transform existing = canvasTransform.Find("ETABarPanel");
            if (existing != null)
            {
                return existing.gameObject;
            }

            TMP_FontAsset chineseFont = GetOrCreateChineseFontAsset();
            Sprite roundedRectSprite = GetOrCreateRoundedRectSprite();

            GameObject panel = new GameObject("ETABarPanel", typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(panel, "Create ETABarPanel");
            panel.transform.SetParent(canvasTransform, false);

            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(0f, 0f);
            panelRect.pivot = new Vector2(0f, 0f);
            panelRect.anchoredPosition = new Vector2(30f, 30f);
            panelRect.sizeDelta = new Vector2(420f, 100f);

            Image panelImage = panel.GetComponent<Image>();
            panelImage.sprite = roundedRectSprite;
            panelImage.type = Image.Type.Sliced;
            panelImage.color = new Color(0.10f, 0.11f, 0.13f, 0.85f);

            GameObject arrivalObj = CreateEtaColumn(panel.transform, "ArrivalTimeText", new Vector2(0f, 0f), new Vector2(0.34f, 1f), "12:00", new Color(0.35f, 0.85f, 0.4f), chineseFont);
            GameObject durationObj = CreateEtaColumn(panel.transform, "DurationText", new Vector2(0.34f, 0f), new Vector2(0.67f, 1f), "0 min", Color.white, chineseFont);
            GameObject distanceObj = CreateEtaColumn(panel.transform, "DistanceText", new Vector2(0.67f, 0f), new Vector2(1f, 1f), "0 m", Color.white, chineseFont);

            return panel;
        }

        private static GameObject CreateEtaColumn(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, string initialText, Color color, TMP_FontAsset chineseFont)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            obj.transform.SetParent(parent, false);

            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            TextMeshProUGUI text = obj.GetComponent<TextMeshProUGUI>();
            text.text = initialText;
            text.fontSize = 28f;
            text.fontStyle = FontStyles.Bold;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            if (chineseFont != null)
            {
                text.font = chineseFont;
            }

            return obj;
        }

        /// <summary>把 ETADisplayUI 掛到 ETA 資訊列面板上，並綁定資料來源與初始載具。</summary>
        private static ETADisplayUI SetupETADisplay(GameObject etaPanel, NavigationLineManager lineManager, GameObject initialVehicle)
        {
            ETADisplayUI eta = etaPanel.GetComponent<ETADisplayUI>();
            if (eta == null)
            {
                eta = Undo.AddComponent<ETADisplayUI>(etaPanel);
            }

            TextMeshProUGUI arrivalText = etaPanel.transform.Find("ArrivalTimeText").GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI durationText = etaPanel.transform.Find("DurationText").GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI distanceText = etaPanel.transform.Find("DistanceText").GetComponent<TextMeshProUGUI>();

            SerializedObject so = new SerializedObject(eta);
            so.FindProperty("lineManager").objectReferenceValue = lineManager;
            so.FindProperty("player").objectReferenceValue = initialVehicle.transform;
            so.FindProperty("playerRigidbody").objectReferenceValue = initialVehicle.GetComponent<Rigidbody>();
            so.FindProperty("panelRoot").objectReferenceValue = etaPanel;
            so.FindProperty("arrivalTimeText").objectReferenceValue = arrivalText;
            so.FindProperty("durationText").objectReferenceValue = durationText;
            so.FindProperty("distanceText").objectReferenceValue = distanceText;
            so.ApplyModifiedProperties();

            return eta;
        }

        /// <summary>
        /// 掛上 VehicleSwitcher，並把汽車／機車、攝影機、導航路徑管理器、導航 UI、儀表板全部接好，
        /// 讓玩家按 V 鍵就能在兩種載具之間無縫切換。
        /// </summary>
        private static void SetupVehicleSwitcher(GameObject navManager, GameObject car, GameObject motorcycle, GoogleMapCamera cameraComponent, NavigationLineManager lineManager, NavigationUIManager uiManager, SpeedometerUI speedometer, ETADisplayUI etaDisplay)
        {
            VehicleSwitcher switcher = navManager.GetComponent<VehicleSwitcher>();
            if (switcher == null)
            {
                switcher = Undo.AddComponent<VehicleSwitcher>(navManager);
            }

            SerializedObject so = new SerializedObject(switcher);
            SerializedProperty vehiclesProp = so.FindProperty("vehicles");
            vehiclesProp.arraySize = 2;
            vehiclesProp.GetArrayElementAtIndex(0).objectReferenceValue = car.transform;
            vehiclesProp.GetArrayElementAtIndex(1).objectReferenceValue = motorcycle.transform;

            so.FindProperty("mapCamera").objectReferenceValue = cameraComponent;
            so.FindProperty("lineManager").objectReferenceValue = lineManager;
            so.FindProperty("uiManager").objectReferenceValue = uiManager;
            so.FindProperty("speedometer").objectReferenceValue = speedometer;
            so.FindProperty("etaDisplay").objectReferenceValue = etaDisplay;
            so.ApplyModifiedProperties();
        }

        /// <summary>取得（或以程式產生）卡片背景用的圓角矩形 9-slice Sprite。</summary>
        private static Sprite GetOrCreateRoundedRectSprite()
        {
            if (!Directory.Exists(GeneratedIconFolder))
            {
                Directory.CreateDirectory(GeneratedIconFolder);
            }

            const int size = 128;
            const int radius = 32;
            string path = $"{GeneratedIconFolder}/RoundedRectPanel.png";

            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null)
            {
                return existing;
            }

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 white = new Color32(255, 255, 255, 255);

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    bool inside = IsInsideRoundedRect(px + 0.5f, py + 0.5f, size, radius);
                    texture.SetPixel(px, py, inside ? white : clear);
                }
            }

            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.spriteBorder = new Vector4(radius, radius, radius, radius);
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static bool IsInsideRoundedRect(float x, float y, float size, float radius)
        {
            bool inCornerZoneX = x < radius || x > size - radius;
            bool inCornerZoneY = y < radius || y > size - radius;

            if (!(inCornerZoneX && inCornerZoneY))
            {
                return true;
            }

            float nearestCx = x < radius ? radius : size - radius;
            float nearestCy = y < radius ? radius : size - radius;
            float dx = x - nearestCx;
            float dy = y - nearestCy;
            return dx * dx + dy * dy <= radius * radius;
        }

        /// <summary>取得（或以程式產生）轉彎圖示背後的實心圓形 Sprite。</summary>
        private static Sprite GetOrCreateCircleSprite()
        {
            if (!Directory.Exists(GeneratedIconFolder))
            {
                Directory.CreateDirectory(GeneratedIconFolder);
            }

            const int size = 128;
            string path = $"{GeneratedIconFolder}/IconBadgeCircle.png";

            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null)
            {
                return existing;
            }

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 white = new Color32(255, 255, 255, 255);
            float radius = size / 2f;
            Vector2 center = new Vector2(radius, radius);

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    float dx = px + 0.5f - center.x;
                    float dy = py + 0.5f - center.y;
                    bool inside = dx * dx + dy * dy <= radius * radius;
                    texture.SetPixel(px, py, inside ? white : clear);
                }
            }

            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>掛上 NavigationUIManager 並綁定所有 UI / 資料來源欄位，含自動產生的占位箭頭圖示。</summary>
        private static void SetupUIManager(GameObject navManager, GameObject card, Transform playerTransform)
        {
            NavigationUIManager uiManager = navManager.GetComponent<NavigationUIManager>();
            if (uiManager == null)
            {
                uiManager = Undo.AddComponent<NavigationUIManager>(navManager);
            }

            NavigationLineManager lineManager = navManager.GetComponent<NavigationLineManager>();
            Image iconImage = card.transform.Find("IconBadge/TurnIconImage").GetComponent<Image>();
            TextMeshProUGUI roadText = card.transform.Find("RoadNameText").GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI distanceText = card.transform.Find("DistanceText").GetComponent<TextMeshProUGUI>();

            SerializedObject so = new SerializedObject(uiManager);
            so.FindProperty("lineManager").objectReferenceValue = lineManager;
            so.FindProperty("player").objectReferenceValue = playerTransform;
            so.FindProperty("navigationCard").objectReferenceValue = card;
            so.FindProperty("turnIconImage").objectReferenceValue = iconImage;
            so.FindProperty("distanceText").objectReferenceValue = distanceText;
            so.FindProperty("roadNameText").objectReferenceValue = roadText;

            so.FindProperty("straightIcon").objectReferenceValue = GetOrCreateArrowSprite(TurnType.Straight);
            so.FindProperty("turnLeftIcon").objectReferenceValue = GetOrCreateArrowSprite(TurnType.TurnLeft);
            so.FindProperty("turnRightIcon").objectReferenceValue = GetOrCreateArrowSprite(TurnType.TurnRight);
            so.FindProperty("uTurnIcon").objectReferenceValue = GetOrCreateArrowSprite(TurnType.UTurn);

            so.ApplyModifiedProperties();
        }

        /// <summary>
        /// 取得（或建立）一個支援繁體中文的 TMP 字型資源。
        /// TextMeshPro 預設字型（Liberation Sans SDF）不含中文字元，會顯示成方框，
        /// 這裡用 Windows 內建的「微軟正黑體」動態產生字型資源（Dynamic 模式，需要哪個字才即時產生，不用預先窮舉字元）。
        /// 若系統找不到該字型（例如非 Windows 或未安裝），會回傳 null，呼叫端需自行判斷是否要沿用預設字型。
        /// </summary>
        // 依優先順序嘗試的 Windows 內建中文字型檔案（優先選單一 .ttf，.ttc 字型集合檔案在 Unity 匯入時較容易出問題）
        private static readonly string[] CandidateSystemFontPaths =
        {
            @"C:\Windows\Fonts\kaiu.ttf",     // 標楷體 (DFKai-SB)
            @"C:\Windows\Fonts\msjh.ttc",     // 微軟正黑體
            @"C:\Windows\Fonts\mingliu.ttc",  // 細明體
            @"C:\Windows\Fonts\simsun.ttc",   // 新細明體
        };

        private static TMP_FontAsset GetOrCreateChineseFontAsset()
        {
            string assetPath = $"{GeneratedFontFolder}/ChineseFont.asset";

            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null)
            {
                return existing;
            }

            try
            {
                string sourceFontPath = null;
                foreach (string candidate in CandidateSystemFontPaths)
                {
                    if (File.Exists(candidate))
                    {
                        sourceFontPath = candidate;
                        break;
                    }
                }

                if (sourceFontPath == null)
                {
                    Debug.LogWarning("[NavigationSceneSetup] 找不到任何內建中文字型檔案，中文字可能會顯示為方框，請自行改用 Window > TextMeshPro > Font Asset Creator 建立中文字型並手動指定給 RoadNameText / DistanceText。");
                    return null;
                }

                if (!Directory.Exists(GeneratedFontFolder))
                {
                    Directory.CreateDirectory(GeneratedFontFolder);
                }

                // 把系統字型檔案複製進專案，讓 Unity 用正常的字型匯入流程處理（避免 CreateDynamicFontFromOSFont 讀不到完整字型資料的問題）
                string importedFontPath = $"{GeneratedFontFolder}/SourceChineseFont{Path.GetExtension(sourceFontPath)}";
                if (!File.Exists(importedFontPath))
                {
                    File.Copy(sourceFontPath, importedFontPath, true);
                    AssetDatabase.ImportAsset(importedFontPath, ImportAssetOptions.ForceSynchronousImport);
                }

                Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(importedFontPath);
                if (sourceFont == null)
                {
                    Debug.LogWarning($"[NavigationSceneSetup] 已複製字型檔案到 {importedFontPath}，但 Unity 無法將它匯入為 Font 資源，請改用 Font Asset Creator 手動處理。");
                    return null;
                }

                TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                    sourceFont, 90, 5, GlyphRenderMode.SDFAA, 1024, 1024,
                    AtlasPopulationMode.Dynamic, true);
                fontAsset.name = "ChineseFont";

                AssetDatabase.CreateAsset(fontAsset, assetPath);

                if (fontAsset.atlasTexture != null)
                {
                    AssetDatabase.AddObjectToAsset(fontAsset.atlasTexture, fontAsset);
                }

                if (fontAsset.material != null)
                {
                    AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
                }

                AssetDatabase.SaveAssets();
                return fontAsset;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[NavigationSceneSetup] 自動建立中文字型失敗：{e.Message}\n請改用 Window > TextMeshPro > Font Asset Creator 手動建立中文字型並指定給 RoadNameText / DistanceText。");
                return null;
            }
        }

        /// <summary>取得（或建立）導航藍線用的簡單材質。</summary>
        private static Material GetOrCreateLineMaterial()
        {
            if (!Directory.Exists(GeneratedMaterialFolder))
            {
                Directory.CreateDirectory(GeneratedMaterialFolder);
            }

            string path = $"{GeneratedMaterialFolder}/NavLineMaterial.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find("Sprites/Default");
            // 顏色值刻意超過 1（HDR 亮度），搭配場景裡的 Bloom 後製效果，讓導航線呈現發光感，
            // 而不是扁平的純色線條，視覺上更接近 Google Maps 導航畫面裡那種發光的藍色路線。
            Material material = new Material(shader) { color = new Color(0.25f, 1.1f, 2.2f) };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>取得（或以程式產生）指定轉彎類型的占位箭頭 Sprite。</summary>
        private static Sprite GetOrCreateArrowSprite(TurnType type)
        {
            if (!Directory.Exists(GeneratedIconFolder))
            {
                Directory.CreateDirectory(GeneratedIconFolder);
            }

            string path = $"{GeneratedIconFolder}/Icon_{type}.png";
            Sprite existingSprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existingSprite != null)
            {
                return existingSprite;
            }

            float rotationDeg = type switch
            {
                TurnType.TurnLeft => 90f,
                TurnType.TurnRight => -90f,
                TurnType.UTurn => 180f,
                _ => 0f
            };

            Texture2D texture = GenerateArrowTexture(128, rotationDeg);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>用簡單的幾何測試畫出一個指向上方、可旋轉的箭頭圖案（占位美術用）。</summary>
        private static Texture2D GenerateArrowTexture(int size, float rotationDeg)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 white = new Color32(255, 255, 255, 255);
            float rad = -rotationDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    float x = (px + 0.5f) / size * 2f - 1f;
                    float y = (py + 0.5f) / size * 2f - 1f;

                    float rx = x * cos - y * sin;
                    float ry = x * sin + y * cos;

                    bool inside = IsInsideUpArrow(rx, ry);
                    texture.SetPixel(px, py, inside ? white : clear);
                }
            }

            texture.Apply();
            return texture;
        }

        /// <summary>定義一個指向正上方的箭頭形狀（座標範圍 -1 ~ 1）。</summary>
        private static bool IsInsideUpArrow(float x, float y)
        {
            bool inShaft = Mathf.Abs(x) < 0.18f && y >= -0.65f && y <= 0.05f;

            bool inHead = false;
            if (y <= 0.75f && y >= 0f)
            {
                float t = (0.75f - y) / 0.75f;
                float halfWidth = 0.55f * t;
                inHead = Mathf.Abs(x) <= halfWidth;
            }

            return inShaft || inHead;
        }
    }
}
