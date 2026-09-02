using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Navigation.Tools
{
    /// <summary>
    /// ★ 編輯器工具 ★
    /// 把導航線與目的地標記重做成參考圖（Google Maps 沉浸式導航）那種質感：
    /// 貼在路面上的發光青色路徑帶、以及漂浮在終點上方的紅色圖釘。
    ///
    /// 使用方式：Tools → Navigation → 向量地圖 → 重做導航線與目的地圖釘
    /// </summary>
    public static class NavigationLineStyler
    {
        private const string GeneratedFolder = "Assets/NavigationCore/Generated/Map";

        [MenuItem("Tools/Navigation/向量地圖/重做導航線與目的地圖釘", priority = 13)]
        public static void RestyleNavigationLine()
        {
            GameObject navManager = FindObjectIncludingInactive("NavigationManager");
            if (navManager == null)
            {
                EditorUtility.DisplayDialog("找不到 NavigationManager", "場景中找不到 NavigationManager。", "了解");
                return;
            }

            EnsureFolder();

            LineRenderer line = navManager.GetComponent<LineRenderer>();
            if (line == null)
            {
                EditorUtility.DisplayDialog("找不到 LineRenderer", "NavigationManager 上沒有 LineRenderer。", "了解");
                return;
            }

            Undo.RecordObject(line, "Restyle Navigation Line");

            // 路徑帶要「平躺在路面上」，不是像旗子一樣立起來面向攝影機。
            // LineRenderer 預設的 View 對齊是billboard（永遠面向攝影機），在俯視導航畫面裡
            // 會看到一條立起來的帶子。改用 TransformZ，並把物件轉成 Z 軸朝上，帶子就會平貼地面。
            Undo.RecordObject(navManager.transform, "Align Navigation Line");
            navManager.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            line.alignment = LineAlignment.TransformZ;

            line.useWorldSpace = true;
            line.widthMultiplier = 3.6f;
            line.numCornerVertices = 8;
            line.numCapVertices = 8;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            Material lineMaterial = CreateOrUpdateGlowMaterial("NavigationRouteMaterial",
                new Color(0.10f, 1.55f, 1.85f));
            line.sharedMaterial = lineMaterial;

            // 顏色從車頭端稍亮、遠端稍暗，做出「路徑往前延伸」的層次。
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.35f, 0.95f, 1f), 0f),
                    new GradientColorKey(new Color(0.10f, 0.72f, 0.92f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.85f),
                    new GradientAlphaKey(0.75f, 1f)
                });
            line.colorGradient = gradient;

            EditorUtility.SetDirty(line);

            int pinCreated = BuildDestinationPin();

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            Debug.Log("[NavigationLineStyler] 導航線已重做：寬 3.6m、平貼地面、發光青色、圓角端點。");

            EditorUtility.DisplayDialog("完成",
                "導航線與目的地圖釘已重做：\n\n" +
                "・路徑帶平貼路面（原本是面向攝影機的立起帶子）\n" +
                "・寬度 3.6 公尺（約一個車道）\n" +
                "・發光青色，會被 Bloom 拾取產生光暈\n" +
                "・端點與轉角都是圓角\n" +
                $"・目的地圖釘：{(pinCreated > 0 ? "已建立" : "略過")}\n\n請按 Ctrl+S 存檔。",
                "好");
        }

        /// <summary>
        /// 建立目的地圖釘：一個倒圓錐 + 一顆球，漂浮在 DestinationMarker 上方。
        /// 參考圖裡那個紅色水滴狀圖釘是整個畫面裡辨識度最高的元素之一。
        /// </summary>
        private static int BuildDestinationPin()
        {
            GameObject destination = FindObjectIncludingInactive("DestinationMarker");
            if (destination == null)
            {
                Debug.LogWarning("[NavigationLineStyler] 找不到 DestinationMarker，略過圖釘。");
                return 0;
            }

            // 舊的視覺子物件先清掉，避免重複執行時愈疊愈多。
            Transform existing = destination.transform.Find("PinVisual");
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            GameObject pin = new GameObject("PinVisual");
            Undo.RegisterCreatedObjectUndo(pin, "Create PinVisual");
            pin.transform.SetParent(destination.transform, false);
            pin.transform.localPosition = Vector3.zero;

            Material pinMaterial = CreateOrUpdateGlowMaterial("DestinationPinMaterial",
                new Color(1.35f, 0.16f, 0.20f));

            // 球體當作圖釘的頭
            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            Undo.RegisterCreatedObjectUndo(head, "Create Pin Head");
            head.transform.SetParent(pin.transform, false);
            head.transform.localPosition = new Vector3(0f, 16f, 0f);
            head.transform.localScale = Vector3.one * 7f;
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.GetComponent<MeshRenderer>().sharedMaterial = pinMaterial;

            // 圓錐（用 Unity 內建的圓柱壓扁上端做不出來，改用縮放的球體＋細長圓柱當尖端）
            GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tip.name = "Tip";
            Undo.RegisterCreatedObjectUndo(tip, "Create Pin Tip");
            tip.transform.SetParent(pin.transform, false);
            tip.transform.localPosition = new Vector3(0f, 8f, 0f);
            tip.transform.localScale = new Vector3(1.6f, 5.5f, 1.6f);
            Object.DestroyImmediate(tip.GetComponent<Collider>());
            tip.GetComponent<MeshRenderer>().sharedMaterial = pinMaterial;

            Debug.Log("[NavigationLineStyler] 目的地圖釘已建立（球頭 + 尖端，離地約 16 公尺）。");
            return 1;
        }

        /// <summary>
        /// 建立自發光材質。用 URP/Unlit 並把顏色值設成大於 1，讓 Bloom 產生光暈——
        /// 導航線不需要受光影響（它是介面元素而不是實體物件），
        /// 用 Unlit 才不會在陰影裡變暗、在陽光下變白。
        /// </summary>
        private static Material CreateOrUpdateGlowMaterial(string name, Color hdrColor)
        {
            string path = $"{GeneratedFolder}/{name}.mat";
            Shader shader = NavigationShaderCompat.Unlit();

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader != shader)
            {
                material = new Material(shader);
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", hdrColor);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static void EnsureFolder()
        {
            if (!Directory.Exists(GeneratedFolder))
            {
                Directory.CreateDirectory(GeneratedFolder);
                AssetDatabase.Refresh();
            }
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
