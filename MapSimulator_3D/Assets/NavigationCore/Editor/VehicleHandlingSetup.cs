using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Navigation.Tools
{
    /// <summary>
    /// ★ 編輯器工具 ★
    /// 套用一組「好開」的車輛操控設定。程式碼裡的速度感應轉向與極速限制只是其中一半，
    /// 另一半在 WheelCollider 的摩擦力曲線與 Rigidbody 的阻力上——那些是場景資料，
    /// 改不了程式碼預設值，只能用這個工具寫進場景。
    ///
    /// 使用方式：Tools → Navigation → 車輛操控 → 套用好開的操控設定
    /// </summary>
    public static class VehicleHandlingSetup
    {
        [MenuItem("Tools/Navigation/車輛操控/套用好開的操控設定", priority = 70)]
        public static void ApplyHandling()
        {
            int carCount = ApplyToVehicle("PlayerVehicle",
                sidewaysStiffness: 2.4f, forwardStiffness: 2.0f,
                drag: 0.12f, angularDrag: 3.5f,
                // 上一版把高速轉向角壓到 7 度，實際開起來「轉不動、反應遲鈍」。
                // 14 度仍然遠低於靜止時的 36 度（高速時方向盤該變重是對的），但至少轉得過來；
                // 方向盤轉速也從 90°/s 拉到 150°/s，鍵盤按下去才會有即時反應。
                // 改成四輪驅動、扭力砍半、極速降到 35：後驅是甩尾的主因（動力全壓在
                // 側向抓地已經吃緊的後輪上），而導航展示根本不需要跑到 60 km/h。
                maxSteer: 36f, minSteer: 14f, steerSmooth: 150f,
                maxSpeed: 35f, motorTorque: 800f, brake: 4000f, engineBrake: 350f,
                // 懸吊加長、彈簧與阻尼放軟：車輪實際踩的是 Cesium 的地形網格，
                // 那是照真實高程生成的三角網，有起伏也會隨 LOD 變化。硬懸吊會把每一個
                // 三角形的稜線都變成一次彈跳，長行程軟懸吊才吸得掉。
                suspensionDistance: 0.45f, springForce: 26000f, damperForce: 3800f);

            int motoCount = ApplyToVehicle("PlayerMotorcycle",
                sidewaysStiffness: 2.0f, forwardStiffness: 1.8f,
                drag: 0.1f, angularDrag: 3f,
                maxSteer: 42f, minSteer: 18f, steerSmooth: 180f,
                maxSpeed: 35f, motorTorque: 500f, brake: 2200f, engineBrake: 250f,
                suspensionDistance: 0.4f, springForce: 9000f, damperForce: 1400f);

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            Debug.Log($"[VehicleHandlingSetup] 已套用操控設定：汽車 {carCount} 台、機車 {motoCount} 台。");

            EditorUtility.DisplayDialog("完成",
                "已套用好開的操控設定：\n\n" +
                "・速度感應轉向：時速 0 時 32 度，65 km/h 時只剩 7 度\n" +
                "・轉向平滑：方向盤每秒最多轉 90 度，不會瞬間打到底\n" +
                "・極速上限 65 km/h（原本沒有上限，會一直加速到失控）\n" +
                "・放開油門有引擎煞車，車子會自己緩緩減速\n" +
                "・輪胎橫向抓地力 2.4 倍（原本是 1.0，過彎容易整台滑出去）\n" +
                "・角阻力 0.5 → 3.5，車頭不會一直晃\n\n" +
                "請按 Ctrl+S 存檔後進 Play 模式試開。",
                "好");
        }

        private static int ApplyToVehicle(string objectName,
            float sidewaysStiffness, float forwardStiffness,
            float drag, float angularDrag,
            float maxSteer, float minSteer, float steerSmooth,
            float maxSpeed, float motorTorque, float brake, float engineBrake,
            float suspensionDistance, float springForce, float damperForce)
        {
            GameObject vehicle = FindObjectIncludingInactive(objectName);
            if (vehicle == null)
            {
                return 0;
            }

            Rigidbody body = vehicle.GetComponent<Rigidbody>();
            if (body != null)
            {
                Undo.RecordObject(body, "Tune Vehicle Handling");

                // 角阻力原本是 0.5，車子在轉向後會持續自轉一段時間（像在冰上）。
                // 拉高之後方向盤回正時車頭也會跟著穩下來。
                body.drag = drag;
                body.angularDrag = angularDrag;

                // 連續碰撞偵測：預設的離散偵測是每個物理影格檢查一次位置，
                // 時速 60 公里下每影格要移動 33 公分，遇到 Cesium 剛串流進來的薄地形網格
                // 很容易「跳過」它直接穿到地下。這是車子突然跑進地裡最常見的成因。
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                // 插值讓車身在影格之間平滑，減少視覺上的抖動。
                body.interpolation = RigidbodyInterpolation.Interpolate;

                EditorUtility.SetDirty(body);
            }

            foreach (WheelCollider wheel in vehicle.GetComponentsInChildren<WheelCollider>(true))
            {
                Undo.RecordObject(wheel, "Tune Wheel Friction");

                // Stiffness 是摩擦力曲線的整體倍率。預設 1.0 對「1200 公斤、半徑 0.35 公尺」
                // 的設定來說抓地力明顯不足，稍微轉一下車尾就滑出去，回正之後又會反向甩，
                // 這種來回擺盪就是操控感覺很爛的主因。
                // 後輪的側向抓地力刻意調得比前輪高。這是防止甩尾的標準底盤設定：
                // 後輪先失去抓地就是轉向過度（車尾甩出去、難救回來），
                // 前輪先失去抓地則是轉向不足（車頭推出去、放開油門就會回來），
                // 對不熟悉的操作者來說後者安全得多。
                bool isRear = wheel.name.Contains("Rear");
                WheelFrictionCurve sideways = wheel.sidewaysFriction;
                sideways.stiffness = isRear ? sidewaysStiffness * 1.35f : sidewaysStiffness;
                wheel.sidewaysFriction = sideways;

                WheelFrictionCurve forward = wheel.forwardFriction;
                forward.stiffness = forwardStiffness;
                wheel.forwardFriction = forward;

                wheel.suspensionDistance = suspensionDistance;

                JointSpring spring = wheel.suspensionSpring;
                spring.spring = springForce;
                spring.damper = damperForce;
                spring.targetPosition = 0.5f;
                wheel.suspensionSpring = spring;

                EditorUtility.SetDirty(wheel);
            }

            VehiclePhysicsController controller = vehicle.GetComponent<VehiclePhysicsController>();
            if (controller != null)
            {
                SerializedObject so = new SerializedObject(controller);
                so.FindProperty("maxSteerAngle").floatValue = maxSteer;
                so.FindProperty("minSteerAngle").floatValue = minSteer;
                so.FindProperty("steerSmoothSpeed").floatValue = steerSmooth;
                so.FindProperty("steerReductionSpeed").floatValue = maxSpeed;
                so.FindProperty("maxSpeedKph").floatValue = maxSpeed;
                so.FindProperty("maxMotorTorque").floatValue = motorTorque;
                so.FindProperty("brakeTorque").floatValue = brake;
                so.FindProperty("engineBrakeTorque").floatValue = engineBrake;

                // 防打轉輔助。後輪驅動 + 全油門 + 滿舵，只要路面夠平能跑到 40 km/h
                // 就會動力過度轉向甩出去——實測偏航 475 度/秒、側滑 179 度（整台倒著滑）。
                // 力道 4 完全擋不住：實測平均偏航仍有 148 度/秒、43% 的時間在打轉，
                // 而幾何上滿舵時速 35 應該只有 53 度/秒。門檻降到 70、力道加到 18。
                so.FindProperty("maxYawRateDegrees").floatValue = 70f;
                so.FindProperty("yawDampStrength").floatValue = 18f;
                so.FindProperty("sideSlipLimitDegrees").floatValue = 18f;

                // 四輪驅動：把驅動力分散到四個輪子，後輪的側向抓地才不會被驅動力吃光。
                so.FindProperty("allWheelDrive").boolValue = true;

                // 質心從輪軸高度（-0.5）改成離地約 0.5 公尺（-0.35），符合真實轎車配置。
                so.FindProperty("centerOfMassOffset").vector3Value = new Vector3(0f, -0.35f, 0f);
                so.ApplyModifiedProperties();
            }

            // 展示流程預設走自動駕駛：導航展示本來就不需要手動開，而且手動模式下
            // 車子在真實地形上很容易開出路面。手動仍可用 Tab 隨時切換。
            DriveModeSwitcher switcher = vehicle.GetComponent<DriveModeSwitcher>();
            if (switcher != null)
            {
                SerializedObject switcherSo = new SerializedObject(switcher);
                switcherSo.FindProperty("startInAutoMode").boolValue = true;

                // 關掉它自己那套「等地面再貼齊」：那段射線是從上方 15 公尺打下來的，
                // 在山洞口或山壁旁會把車貼到山上。定位交給 VehicleTerrainSnap 統一處理，
                // 兩套並存只會互相打架。
                switcherSo.FindProperty("snapToGroundOnStart").boolValue = false;
                switcherSo.ApplyModifiedProperties();
            }

            // 自動駕駛的貼地設定。原本它朝路口做 3D 直線內插（含高度），
            // 路口相距約 200 公尺、中間地形起伏好幾公尺，車子等於沿直線飛過去，
            // 遇到隆起的地形就整台埋進去——實測跑完全程被埋 12～22 次、最深 4.9 公尺。
            AutoDriveController autoDrive = vehicle.GetComponent<AutoDriveController>();
            if (autoDrive != null)
            {
                GameObject buildingTiles = FindObjectIncludingInactive("CesiumOSMBuildings");
                SerializedObject autoSo = new SerializedObject(autoDrive);
                autoSo.FindProperty("followGround").boolValue = true;
                autoSo.FindProperty("groundBuffer").floatValue = 1.2f;
                autoSo.FindProperty("probeAbove").floatValue = 8f;
                autoSo.FindProperty("probeDistance").floatValue = 60f;
                autoSo.FindProperty("heightSmoothSpeed").floatValue = 8f;
                autoSo.FindProperty("buildingsTileset").objectReferenceValue =
                    buildingTiles != null ? buildingTiles.transform : null;
                autoSo.ApplyModifiedProperties();
            }

            // 車身視覺跟著地面坡度傾斜所需的參照：建築的碰撞網格要排除，屋頂不是路面。
            VehicleBodyRide ride = vehicle.GetComponent<VehicleBodyRide>();
            if (ride != null)
            {
                GameObject buildings = FindObjectIncludingInactive("CesiumOSMBuildings");
                SerializedObject rideSo = new SerializedObject(ride);
                rideSo.FindProperty("buildingsTileset").objectReferenceValue =
                    buildings != null ? buildings.transform : null;
                rideSo.FindProperty("alignToGroundSlope").boolValue = true;
                rideSo.ApplyModifiedProperties();
            }

            Debug.Log($"[VehicleHandlingSetup] 「{objectName}」：橫向抓地 {sidewaysStiffness}、" +
                      $"角阻力 {angularDrag}、轉向 {maxSteer}°→{minSteer}°、極速 {maxSpeed} km/h、" +
                      $"懸吊行程 {suspensionDistance}m／彈簧 {springForce}／阻尼 {damperForce}。");
            return 1;
        }

        private static GameObject FindObjectIncludingInactive(string objectName)
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
                    GameObject match = FindInChildren(root.transform, objectName);
                    if (match != null)
                    {
                        return match;
                    }
                }
            }

            return null;
        }

        private static GameObject FindInChildren(Transform current, string objectName)
        {
            if (current.name.Trim() == objectName)
            {
                return current.gameObject;
            }

            foreach (Transform child in current)
            {
                GameObject match = FindInChildren(child, objectName);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }
    }
}
