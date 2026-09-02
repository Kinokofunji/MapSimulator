using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Navigation.Tools
{
    /// <summary>
    /// ★ 編輯器工具 ★
    /// 用程式生成一台真正的轎車模型，取代原本用兩個 Cube 拼出來的占位車輛
    /// （白色長方體車身 + 深色長方體座艙，而且完全沒有輪胎視覺模型，只有看不見的 WheelCollider）。
    ///
    /// 為什麼用程式生成而不是找現成模型：這個專案裡沒有任何 fbx/obj 模型檔，
    /// 而 Asset Store 的車輛模型有授權與檔案體積問題（畢專要繳交原始碼）。程式生成的
    /// 模型完全自有、可重複產生、幾百個三角形而已，對 4GB 顯存的筆電也毫無負擔。
    ///
    /// 車身用「斷面放樣（loft）」建：沿著車長方向定義數個橫斷面，每個斷面是一個可調圓角
    /// 程度的超橢圓，再把相鄰斷面縫起來。這樣就能做出引擎蓋下傾、A 柱傾斜、車尾收窄
    /// 這些真實車輛的輪廓，而不是方塊的直角。
    ///
    /// 使用方式：Unity 上方選單列 → Tools → Navigation → 車輛外觀 → 建立真實轎車模型
    /// </summary>
    public static class VehicleModelBuilder
    {
        // 生成的資源刻意不放在 Editor 資料夾底下。Editor 資料夾的內容在建置（Build）時會被
        // 整個剝除，場景若參照到裡面的材質/網格，打包成 exe 後就會變成一片洋紅色。
        private const string GeneratedFolder = "Assets/NavigationCore/Generated/Vehicle";

        /// <summary>車輪半徑，必須跟 NavigationSceneSetup.CreateWheelCollider 裡的設定一致。</summary>
        private const float WheelRadius = 0.35f;

        /// <summary>輪胎寬度的一半。</summary>
        private const float WheelHalfWidth = 0.11f;

        /// <summary>輪圈半徑（輪胎的內緣）。</summary>
        private const float RimRadius = 0.235f;

        /// <summary>車輪中心距離車身中線的距離。車身最寬處比這個窄，讓輪胎露出來像葉子板。</summary>
        private const float WheelTrackHalfWidth = 0.80f;

        /// <summary>前後輪距離車輛原點的距離（軸距的一半），沿用場景中既有的 WheelCollider 位置。</summary>
        private const float WheelBaseHalfLength = 1.3f;

        /// <summary>斷面的圓周取樣數。28 已經非常滑順，整台車也才一千多個三角形。</summary>
        private const int RadialSegments = 28;

        /// <summary>
        /// 車身的一個橫斷面。座標系統以「輪軸中心高度」為 y = 0，車頭方向為 +z。
        /// 這樣定義的好處是不用管懸吊靜止時被壓縮多少——車身高度交給 VehicleBodyRide
        /// 在執行時直接讀輪胎的實際位置決定。
        /// </summary>
        private struct Section
        {
            public float z;
            public float halfWidth;
            public float bottomY;
            public float topY;

            /// <summary>上半部的超橢圓指數：2 = 純橢圓，數字越大越接近方形（肩線越明顯）。</summary>
            public float topPower;

            public float bottomPower;

            public Section(float z, float halfWidth, float bottomY, float topY, float topPower, float bottomPower)
            {
                this.z = z;
                this.halfWidth = halfWidth;
                this.bottomY = bottomY;
                this.topY = topY;
                this.topPower = topPower;
                this.bottomPower = bottomPower;
            }
        }

        /// <summary>
        /// 下車身：從車尾收窄 → 車門最寬 → 引擎蓋下傾 → 車頭收窄。
        /// 最寬處 0.78（車身寬 1.56m），輪胎中心在 0.80，所以輪胎會露出車身外側約 8 公分，
        /// 看起來就像葉子板包住輪拱——這是低多邊形車輛模型讓輪胎「看得見」的標準做法。
        /// </summary>
        private static readonly Section[] BodySections =
        {
            new Section(-1.95f, 0.56f, -0.08f, 0.24f, 2.8f, 2.4f),
            new Section(-1.80f, 0.70f, -0.15f, 0.34f, 3.0f, 2.6f),
            new Section(-1.35f, 0.77f, -0.20f, 0.40f, 3.4f, 2.6f),
            new Section(-0.55f, 0.78f, -0.22f, 0.42f, 3.4f, 2.6f),
            new Section( 0.35f, 0.78f, -0.22f, 0.42f, 3.4f, 2.6f),
            new Section( 1.10f, 0.77f, -0.21f, 0.38f, 3.4f, 2.6f),
            new Section( 1.70f, 0.74f, -0.18f, 0.32f, 3.2f, 2.6f),
            new Section( 1.95f, 0.66f, -0.14f, 0.25f, 3.0f, 2.4f),
            new Section( 2.05f, 0.52f, -0.10f, 0.17f, 2.8f, 2.4f)
        };

        /// <summary>
        /// 座艙玻璃罩（greenhouse）：後擋風斜面 → 車頂 → A 柱與前擋風斜面。
        /// 底部刻意插進下車身內部 6 公分，避免兩個網格之間出現縫隙。
        /// </summary>
        private static readonly Section[] GreenhouseSections =
        {
            new Section(-1.15f, 0.38f, 0.34f, 0.42f, 2.6f, 6f),
            new Section(-0.90f, 0.54f, 0.34f, 0.64f, 2.6f, 6f),
            new Section(-0.50f, 0.62f, 0.34f, 0.76f, 2.6f, 6f),
            new Section(-0.10f, 0.63f, 0.34f, 0.79f, 2.6f, 6f),
            new Section( 0.30f, 0.61f, 0.34f, 0.74f, 2.6f, 6f),
            new Section( 0.60f, 0.52f, 0.34f, 0.58f, 2.6f, 6f),
            new Section( 0.78f, 0.38f, 0.34f, 0.44f, 2.6f, 6f)
        };

        [MenuItem("Tools/Navigation/車輛外觀/建立真實轎車模型", priority = 60)]
        public static void BuildCarModel()
        {
            GameObject vehicle = FindObjectIncludingInactive("PlayerVehicle");
            if (vehicle == null)
            {
                EditorUtility.DisplayDialog("找不到 PlayerVehicle",
                    "場景中找不到 PlayerVehicle，無法建立車輛模型。", "了解");
                return;
            }

            EnsureFolder();

            // ★ 金屬度是這裡最容易踩的坑：在 URP 裡，金屬度接近 1 的表面幾乎不反射自己的
            // BaseColor，顏色全部來自環境反射。這個場景沒有任何 Reflection Probe，天空盒的
            // 反射又很暗，所以金屬度 0.85 的「藍色車漆」實際算出來會是一片黑。
            // 遊戲裡的車漆標準做法是低金屬度 + 高平滑度（靠高光而不是靠反射做出烤漆感）。
            Material paint = CreateOrUpdateMaterial("CarPaint",
                new Color(0.16f, 0.36f, 0.72f), metallic: 0.15f, smoothness: 0.72f);
            Material glass = CreateOrUpdateMaterial("CarGlass",
                new Color(0.10f, 0.13f, 0.19f), metallic: 0.05f, smoothness: 0.92f);
            Material trim = CreateOrUpdateMaterial("CarTrim",
                new Color(0.10f, 0.10f, 0.115f), metallic: 0.05f, smoothness: 0.35f);
            Material tire = CreateOrUpdateMaterial("CarTire",
                new Color(0.075f, 0.075f, 0.08f), metallic: 0f, smoothness: 0.2f);
            Material rim = CreateOrUpdateMaterial("CarRim",
                new Color(0.80f, 0.82f, 0.85f), metallic: 0.3f, smoothness: 0.6f);
            Material headlight = CreateOrUpdateMaterial("CarHeadlight",
                new Color(0.9f, 0.93f, 1f), metallic: 0f, smoothness: 0.95f,
                emission: new Color(1f, 0.97f, 0.88f) * 3.2f);
            Material taillight = CreateOrUpdateMaterial("CarTaillight",
                new Color(0.55f, 0.05f, 0.05f), metallic: 0f, smoothness: 0.9f,
                emission: new Color(1f, 0.12f, 0.08f) * 2.6f);

            Mesh bodyMesh = SaveMesh(BuildLoftMesh(BodySections, RadialSegments, "CarBodyMesh"), "CarBodyMesh");
            Mesh glassMesh = SaveMesh(BuildLoftMesh(GreenhouseSections, RadialSegments, "CarGlassMesh"), "CarGlassMesh");
            Mesh tireMesh = SaveMesh(BuildTireMesh(WheelRadius, RimRadius, WheelHalfWidth, RadialSegments), "CarTireMesh");
            Mesh rimMesh = SaveMesh(BuildCylinderMesh(RimRadius, WheelHalfWidth * 0.86f, RadialSegments), "CarRimMesh");

            // 舊的占位方塊（VehicleVisual 底下的 Body / Cabin）直接刪掉，不然會跟新車身重疊穿插。
            Transform legacyVisual = vehicle.transform.Find("VehicleVisual");
            if (legacyVisual != null)
            {
                Undo.DestroyObjectImmediate(legacyVisual.gameObject);
                Debug.Log("[VehicleModelBuilder] 已移除舊的方塊占位模型 VehicleVisual。");
            }

            MeshRenderer rootRenderer = vehicle.GetComponent<MeshRenderer>();
            if (rootRenderer != null && rootRenderer.enabled)
            {
                rootRenderer.enabled = false;
            }

            // 車身視覺的根節點。VehicleBodyRide 會在執行時移動它，所以這裡的 Y 只是個起始值。
            GameObject bodyRoot = GetOrCreateChild(vehicle.transform, "CarBody");
            bodyRoot.transform.localPosition = new Vector3(0f, -0.44f, 0f);
            bodyRoot.transform.localRotation = Quaternion.identity;

            CreateMeshChild(bodyRoot.transform, "BodyShell", bodyMesh, paint, Vector3.zero);
            CreateMeshChild(bodyRoot.transform, "Greenhouse", glassMesh, glass, Vector3.zero);

            // 保險桿與水箱罩：用扁方塊就夠了，它們在真車上本來就是方正的區塊。
            // 這些裝飾件全部刻意做小、並往車身內側塞，讓它們讀起來像「車身上的一個區塊」，
            // 而不是黏在外面的獨立方塊——上一版就是因為做太大太外凸，看起來像貼了幾塊木板。
            CreateBox(bodyRoot.transform, "FrontBumper", trim,
                new Vector3(1.10f, 0.15f, 0.22f), new Vector3(0f, -0.15f, 1.84f));
            CreateBox(bodyRoot.transform, "RearBumper", trim,
                new Vector3(1.06f, 0.15f, 0.20f), new Vector3(0f, -0.13f, -1.76f));
            CreateBox(bodyRoot.transform, "Grille", trim,
                new Vector3(0.70f, 0.11f, 0.07f), new Vector3(0f, 0.01f, 1.98f));

            CreateBox(bodyRoot.transform, "HeadlightLeft", headlight,
                new Vector3(0.27f, 0.085f, 0.08f), new Vector3(-0.38f, 0.11f, 1.91f));
            CreateBox(bodyRoot.transform, "HeadlightRight", headlight,
                new Vector3(0.27f, 0.085f, 0.08f), new Vector3(0.38f, 0.11f, 1.91f));
            CreateBox(bodyRoot.transform, "TaillightLeft", taillight,
                new Vector3(0.27f, 0.095f, 0.06f), new Vector3(-0.41f, 0.20f, -1.79f));
            CreateBox(bodyRoot.transform, "TaillightRight", taillight,
                new Vector3(0.27f, 0.095f, 0.06f), new Vector3(0.41f, 0.20f, -1.79f));

            // 側視鏡，加了之後車子的「面向」一眼就看得出來。
            CreateBox(bodyRoot.transform, "MirrorLeft", trim,
                new Vector3(0.15f, 0.055f, 0.09f), new Vector3(-0.80f, 0.42f, 0.70f));
            CreateBox(bodyRoot.transform, "MirrorRight", trim,
                new Vector3(0.15f, 0.055f, 0.09f), new Vector3(0.80f, 0.42f, 0.70f));

            Transform frontLeft = BuildWheel(vehicle.transform, "WheelVisual_FrontLeft", tireMesh, rimMesh, tire, rim, true);
            Transform frontRight = BuildWheel(vehicle.transform, "WheelVisual_FrontRight", tireMesh, rimMesh, tire, rim, false);
            Transform rearLeft = BuildWheel(vehicle.transform, "WheelVisual_RearLeft", tireMesh, rimMesh, tire, rim, true);
            Transform rearRight = BuildWheel(vehicle.transform, "WheelVisual_RearRight", tireMesh, rimMesh, tire, rim, false);

            // 輪胎視覺的位置每一影格都會被 VehiclePhysicsController 依 WheelCollider 的實際姿態覆寫，
            // 這裡先擺到大致位置，只是為了在編輯器裡看得出車子的樣子。
            frontLeft.localPosition = new Vector3(-WheelTrackHalfWidth, -0.44f, WheelBaseHalfLength);
            frontRight.localPosition = new Vector3(WheelTrackHalfWidth, -0.44f, WheelBaseHalfLength);
            rearLeft.localPosition = new Vector3(-WheelTrackHalfWidth, -0.44f, -WheelBaseHalfLength);
            rearRight.localPosition = new Vector3(WheelTrackHalfWidth, -0.44f, -WheelBaseHalfLength);

            AlignWheelColliders(vehicle.transform);
            BindWheelVisuals(vehicle, frontLeft, frontRight, rearLeft, rearRight);
            SetupBodyRide(vehicle, bodyRoot.transform, frontLeft, frontRight, rearLeft, rearRight);

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            Debug.Log($"[VehicleModelBuilder] 轎車模型建立完成：車身 {bodyMesh.triangles.Length / 3} 面、" +
                      $"玻璃 {glassMesh.triangles.Length / 3} 面、單顆輪胎 {tireMesh.triangles.Length / 3} 面。" +
                      $"資源存放於 {GeneratedFolder}。");

            EditorUtility.DisplayDialog("完成",
                "已建立真實轎車模型，取代原本的兩個方塊：\n\n" +
                "・放樣車身（引擎蓋下傾、A 柱傾斜、車尾收窄）\n" +
                "・深色玻璃座艙罩\n" +
                "・四顆有輪圈的輪胎，會跟著懸吊起伏與轉向轉動\n" +
                "・頭燈／尾燈（自發光）、保險桿、水箱罩、後視鏡\n" +
                "・車身會隨懸吊做煞車點頭與過彎側傾\n\n" +
                "請按 Ctrl+S 存檔後進 Play 模式看看。",
                "好");
        }

        // ────────────────────────────────────────────────────────────────
        //  網格生成
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 把一連串橫斷面縫成一個封閉的網格（放樣）。每個斷面用超橢圓取樣：
        /// 指數 2 是純橢圓，指數越大越接近圓角矩形，車身的肩線就是靠這個做出來的。
        /// </summary>
        private static Mesh BuildLoftMesh(Section[] sections, int radialSegments, string meshName)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();

            for (int sectionIndex = 0; sectionIndex < sections.Length; sectionIndex++)
            {
                Section section = sections[sectionIndex];
                float centerY = (section.topY + section.bottomY) * 0.5f;
                float upperExtent = section.topY - centerY;
                float lowerExtent = centerY - section.bottomY;

                for (int i = 0; i < radialSegments; i++)
                {
                    float angle = Mathf.PI * 2f * i / radialSegments;
                    float cos = Mathf.Cos(angle);
                    float sin = Mathf.Sin(angle);

                    bool upper = sin >= 0f;
                    float power = upper ? section.topPower : section.bottomPower;
                    float exponent = 2f / power;

                    float x = section.halfWidth * Mathf.Sign(cos) * Mathf.Pow(Mathf.Abs(cos), exponent);
                    float y = upper
                        ? centerY + upperExtent * Mathf.Pow(sin, exponent)
                        : centerY - lowerExtent * Mathf.Pow(-sin, exponent);

                    vertices.Add(new Vector3(x, y, section.z));
                    uvs.Add(new Vector2((float)i / radialSegments, (float)sectionIndex / sections.Length));
                }
            }

            for (int s = 0; s < sections.Length - 1; s++)
            {
                int current = s * radialSegments;
                int next = (s + 1) * radialSegments;

                for (int i = 0; i < radialSegments; i++)
                {
                    int i2 = (i + 1) % radialSegments;

                    triangles.Add(current + i);
                    triangles.Add(next + i);
                    triangles.Add(next + i2);

                    triangles.Add(current + i);
                    triangles.Add(next + i2);
                    triangles.Add(current + i2);
                }
            }

            AddCap(sections[0], vertices, uvs, triangles, 0, radialSegments, facingForward: false);
            AddCap(sections[sections.Length - 1], vertices, uvs, triangles,
                (sections.Length - 1) * radialSegments, radialSegments, facingForward: true);

            Mesh mesh = new Mesh { name = meshName };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>用扇形三角形把放樣的頭尾封起來，否則從車頭或車尾看進去會是空心的。</summary>
        private static void AddCap(Section section, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
            int ringStart, int radialSegments, bool facingForward)
        {
            int centerIndex = vertices.Count;
            vertices.Add(new Vector3(0f, (section.topY + section.bottomY) * 0.5f, section.z));
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int i = 0; i < radialSegments; i++)
            {
                int i2 = (i + 1) % radialSegments;

                if (facingForward)
                {
                    triangles.Add(centerIndex);
                    triangles.Add(ringStart + i);
                    triangles.Add(ringStart + i2);
                }
                else
                {
                    triangles.Add(centerIndex);
                    triangles.Add(ringStart + i2);
                    triangles.Add(ringStart + i);
                }
            }
        }

        /// <summary>
        /// 輪胎：一圈中空的環，轉軸沿著本地 X 軸（Unity 的 WheelCollider.GetWorldPose 就是用這個朝向）。
        /// 外圈是胎面，兩側是胎壁，內圈留給輪圈。
        /// </summary>
        private static Mesh BuildTireMesh(float outerRadius, float innerRadius, float halfWidth, int segments)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            // 每個面各自擁有一組頂點，四組共八圈——刻意不共用。
            // 共用頂點的話 RecalculateNormals 會把胎面與胎壁之間那道 90 度的折邊平均掉，
            // 整顆輪胎就會被著色成一顆球（上一版就是這樣，四顆輪子看起來像黑色保齡球）。
            AddRing(vertices, outerRadius, -halfWidth, segments);  // 0 胎面左緣
            AddRing(vertices, outerRadius, halfWidth, segments);   // 1 胎面右緣
            AddRing(vertices, outerRadius, halfWidth, segments);   // 2 右胎壁外緣
            AddRing(vertices, innerRadius, halfWidth, segments);   // 3 右胎壁內緣
            AddRing(vertices, innerRadius, halfWidth, segments);   // 4 內孔右
            AddRing(vertices, innerRadius, -halfWidth, segments);  // 5 內孔左
            AddRing(vertices, innerRadius, -halfWidth, segments);  // 6 左胎壁內緣
            AddRing(vertices, outerRadius, -halfWidth, segments);  // 7 左胎壁外緣

            ConnectRings(triangles, 0, segments, segments);                 // 胎面
            ConnectRings(triangles, segments * 2, segments * 3, segments);  // 右胎壁
            ConnectRings(triangles, segments * 4, segments * 5, segments);  // 內孔
            ConnectRings(triangles, segments * 6, segments * 7, segments);  // 左胎壁

            Mesh mesh = new Mesh { name = "CarTireMesh" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>輪圈：一個兩端封起來的圓柱，轉軸同樣沿著本地 X 軸。</summary>
        private static Mesh BuildCylinderMesh(float radius, float halfWidth, int segments)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            AddRing(vertices, radius, -halfWidth, segments);
            AddRing(vertices, radius, halfWidth, segments);
            ConnectRings(triangles, 0, segments, segments);

            int leftCenter = vertices.Count;
            vertices.Add(new Vector3(-halfWidth, 0f, 0f));
            int rightCenter = vertices.Count;
            vertices.Add(new Vector3(halfWidth, 0f, 0f));

            for (int i = 0; i < segments; i++)
            {
                int i2 = (i + 1) % segments;

                triangles.Add(leftCenter);
                triangles.Add(i);
                triangles.Add(i2);

                triangles.Add(rightCenter);
                triangles.Add(segments + i2);
                triangles.Add(segments + i);
            }

            Mesh mesh = new Mesh { name = "CarRimMesh" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddRing(List<Vector3> vertices, float radius, float x, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                vertices.Add(new Vector3(x, Mathf.Sin(angle) * radius, Mathf.Cos(angle) * radius));
            }
        }

        private static void ConnectRings(List<int> triangles, int ringA, int ringB, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                int i2 = (i + 1) % segments;

                triangles.Add(ringA + i);
                triangles.Add(ringB + i);
                triangles.Add(ringB + i2);

                triangles.Add(ringA + i);
                triangles.Add(ringB + i2);
                triangles.Add(ringA + i2);
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  場景組裝
        // ────────────────────────────────────────────────────────────────

        private static Transform BuildWheel(Transform parent, string name, Mesh tireMesh, Mesh rimMesh,
            Material tireMaterial, Material rimMaterial, bool leftSide)
        {
            GameObject wheel = GetOrCreateChild(parent, name);

            CreateMeshChild(wheel.transform, "Tire", tireMesh, tireMaterial, Vector3.zero);
            GameObject rimObj = CreateMeshChild(wheel.transform, "Rim", rimMesh, rimMaterial, Vector3.zero);

            // 輪圈往車外側偏一點點，做出輪圈略微凸出胎面內緣的視覺（正面看得到輪圈盤面）。
            rimObj.transform.localPosition = new Vector3(leftSide ? -0.012f : 0.012f, 0f, 0f);

            return wheel.transform;
        }

        /// <summary>
        /// 把 WheelCollider 的橫向位置往內收到 ±0.80（原本是 ±0.90，跟車身一樣寬，
        /// 輪胎會完全埋進車身裡看不見）。車體的 Rigidbody 已鎖住 X/Z 軸旋轉，
        /// 輪距縮小 10 公分不會有翻車風險。
        /// </summary>
        private static void AlignWheelColliders(Transform vehicle)
        {
            AlignOne(vehicle, "WheelCollider_FrontLeft", -WheelTrackHalfWidth, WheelBaseHalfLength);
            AlignOne(vehicle, "WheelCollider_FrontRight", WheelTrackHalfWidth, WheelBaseHalfLength);
            AlignOne(vehicle, "WheelCollider_RearLeft", -WheelTrackHalfWidth, -WheelBaseHalfLength);
            AlignOne(vehicle, "WheelCollider_RearRight", WheelTrackHalfWidth, -WheelBaseHalfLength);
        }

        private static void AlignOne(Transform vehicle, string name, float x, float z)
        {
            Transform wheel = vehicle.Find(name);
            if (wheel == null)
            {
                Debug.LogWarning($"[VehicleModelBuilder] 找不到 {name}，跳過位置對齊。");
                return;
            }

            Undo.RecordObject(wheel, "Align Wheel Collider");
            Vector3 position = wheel.localPosition;
            wheel.localPosition = new Vector3(x, position.y, z);
            EditorUtility.SetDirty(wheel);
        }

        private static void BindWheelVisuals(GameObject vehicle, Transform fl, Transform fr, Transform rl, Transform rr)
        {
            VehiclePhysicsController controller = vehicle.GetComponent<VehiclePhysicsController>();
            if (controller == null)
            {
                Debug.LogWarning("[VehicleModelBuilder] PlayerVehicle 上沒有 VehiclePhysicsController，輪胎不會跟著轉。");
                return;
            }

            SerializedObject so = new SerializedObject(controller);
            so.FindProperty("frontLeftMesh").objectReferenceValue = fl;
            so.FindProperty("frontRightMesh").objectReferenceValue = fr;
            so.FindProperty("rearLeftMesh").objectReferenceValue = rl;
            so.FindProperty("rearRightMesh").objectReferenceValue = rr;
            so.ApplyModifiedProperties();
        }

        private static void SetupBodyRide(GameObject vehicle, Transform body,
            Transform fl, Transform fr, Transform rl, Transform rr)
        {
            VehicleBodyRide ride = vehicle.GetComponent<VehicleBodyRide>();
            if (ride == null)
            {
                ride = Undo.AddComponent<VehicleBodyRide>(vehicle);
            }

            SerializedObject so = new SerializedObject(ride);
            so.FindProperty("body").objectReferenceValue = body;
            so.FindProperty("frontLeftWheel").objectReferenceValue = fl;
            so.FindProperty("frontRightWheel").objectReferenceValue = fr;
            so.FindProperty("rearLeftWheel").objectReferenceValue = rl;
            so.FindProperty("rearRightWheel").objectReferenceValue = rr;
            so.ApplyModifiedProperties();
        }

        private static GameObject GetOrCreateChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                return existing.gameObject;
            }

            GameObject created = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(created, $"Create {name}");
            created.transform.SetParent(parent, false);
            return created;
        }

        private static GameObject CreateMeshChild(Transform parent, string name, Mesh mesh, Material material, Vector3 localPosition)
        {
            GameObject child = GetOrCreateChild(parent, name);
            child.transform.localPosition = localPosition;

            MeshFilter filter = child.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = Undo.AddComponent<MeshFilter>(child);
            }

            MeshRenderer renderer = child.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = Undo.AddComponent<MeshRenderer>(child);
            }

            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            return child;
        }

        private static void CreateBox(Transform parent, string name, Material material, Vector3 size, Vector3 localPosition)
        {
            Transform existing = parent.Find(name);
            GameObject box = existing != null ? existing.gameObject : GameObject.CreatePrimitive(PrimitiveType.Cube);

            if (existing == null)
            {
                box.name = name;
                Undo.RegisterCreatedObjectUndo(box, $"Create {name}");
                box.transform.SetParent(parent, false);

                // 這些只是裝飾件，留著碰撞體會干擾車體本身的物理。
                Collider collider = box.GetComponent<Collider>();
                if (collider != null)
                {
                    Undo.DestroyObjectImmediate(collider);
                }
            }

            box.transform.localPosition = localPosition;
            box.transform.localScale = size;

            MeshRenderer renderer = box.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  資源存檔
        // ────────────────────────────────────────────────────────────────

        private static void EnsureFolder()
        {
            if (!Directory.Exists(GeneratedFolder))
            {
                Directory.CreateDirectory(GeneratedFolder);
                AssetDatabase.Refresh();
            }
        }

        /// <summary>
        /// 把生成的網格存成 .asset。這一步不能省：MeshFilter.sharedMesh 指向的如果是純記憶體物件，
        /// 場景存檔後那個參照就會斷掉，重開專案時車子會整台消失。
        /// </summary>
        private static Mesh SaveMesh(Mesh mesh, string assetName)
        {
            string path = $"{GeneratedFolder}/{assetName}.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (existing != null)
            {
                // 覆寫既有資源而不是刪掉重建，這樣場景裡原本的參照不會斷。
                existing.Clear();
                existing.SetVertices(new List<Vector3>(mesh.vertices));
                existing.SetUVs(0, new List<Vector2>(mesh.uv));
                existing.SetTriangles(mesh.triangles, 0);
                existing.RecalculateNormals();
                existing.RecalculateTangents();
                existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return existing;
            }

            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            return mesh;
        }

        private static Material CreateOrUpdateMaterial(string name, Color baseColor, float metallic, float smoothness,
            Color? emission = null)
        {
            string path = $"{GeneratedFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                Shader shader = NavigationShaderCompat.Lit();
                if (shader == null)
                {
                    Debug.LogError("[VehicleModelBuilder] 找不到 URP Lit 著色器。");
                    return null;
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", baseColor);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);

            if (emission.HasValue)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetColor("_EmissionColor", emission.Value);
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
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
