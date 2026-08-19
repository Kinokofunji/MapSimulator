using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 給命令列批次模式呼叫的 WebGL 建置腳本，搭配 repo 根目錄的 deploy-webgl.ps1 使用：
///   Unity.exe -batchmode -quit -projectPath MapSimulator_3D -executeMethod WebGLBuildScript.BuildForDeploy
///
/// 只建置 Build Settings 裡目前「已啟用」的場景，輸出到專案根目錄的 WebGLStagingBuild 資料夾，
/// 之後由 deploy-webgl.ps1 負責複製到 simulator-web/public/Build 並處理 git commit/push。
/// </summary>
public static class WebGLBuildScript
{
    // 沿用專案原本就在用、且已經被 .gitignore 排除的 "Build" 資料夾當輸出目標，
    // 不額外發明新資料夾名稱、也不需要再改 .gitignore。
    // Unity 會在裡面自動建立 Build/Build/*.wasm、Build/TemplateData/、Build/index.html。
    private const string OutputFolder = "Build";

    public static void BuildForDeploy()
    {
        // 固定 Product Name，確保輸出的檔名永遠是 Build.data / Build.wasm / Build.framework.js / Build.loader.js，
        // 跟 simulator-web/src/App.jsx 裡寫死的路徑對得上，不會因為 Player Settings 裡 Product Name 是空的
        // 而產生不同檔名（Console 之前出現過 "productName is missing" 的警告就是因為這個欄位是空的）。
        PlayerSettings.productName = "Build";

        string[] enabledScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (enabledScenes.Length == 0)
        {
            Debug.LogError("WebGLBuildScript：Build Settings 裡沒有任何啟用的場景，無法建置。請先執行 " +
                "Tools → 導航系統 → 整理 Build Settings，或手動在 File → Build Settings 勾選場景。");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log("WebGLBuildScript：即將建置以下場景：\n" + string.Join("\n", enabledScenes));

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = enabledScenes,
            locationPathName = OutputFolder,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);

        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"WebGLBuildScript：建置失敗，結果：{report.summary.result}，錯誤數：{report.summary.totalErrors}");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"WebGLBuildScript：建置成功！耗時 {report.summary.totalTime}，輸出於 {OutputFolder}");
        EditorApplication.Exit(0);
    }
}
