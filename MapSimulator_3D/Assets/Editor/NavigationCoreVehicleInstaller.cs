using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
// 這個檔案刻意不寫 using System;——這裡多處用 UnityEngine.Object.FindObjectOfType<T>()
// (寫成 Object.FindObjectOfType<T>())，System 命名空間裡也有一個 System.Object，
// 兩個 using 同時存在時，編譯器對沒有前綴的「Object」沒辦法判斷是指哪一個，
// 會報 CS0104 ambiguous reference。下面唯一需要 System 命名空間的地方
// (StringComparison.OrdinalIgnoreCase) 改成寫完整路徑 System.StringComparison，
// 不需要在檔案層級整個 using System;。

/// <summary>
/// 把 NavigationCore 套件（她的畢業專題導航核心，見 Assets/NavigationCore/README.md）的
/// WheelCollider 真實車輪物理 + 自動駕駛，接到我們自己已經在用的車輛與導航系統上。
///
/// 設計原則：
/// ・不動用她的 GoogleMapCamera / NavigationLineManager / NavigationUIManager / VehicleSwitcher
///   這一整套——跟我們自己的系統功能重疊，兩邊都跑會變成兩台攝影機、兩張卡片、兩條線互相打架。
/// ・只借「真實車輪物理」（VehiclePhysicsController + WheelCollider）跟「自動駕駛」
///   （AutoDriveController + DriveModeSwitcher，按 Tab 切換手動/自動），這兩塊是我們原本沒有、
///   純粹補強駕駛手感與展示用途的部分。
/// ・她的 Navigation.NavigationLineManager 在這裡只拿來當 AutoDriveController 讀路線用的資料容器，
///   不負責畫線、不負責 UI——那些我們自己的 NavigationLineManager / NavigationUIManager 已經在做，
///   用 NavigationCoreVehicleBridge 把兩邊的路線資料同步起來就好。
///
/// 這個檔案刻意不寫 using Navigation;，理由跟 NavigationCoreVehicleBridge.cs 一樣：
/// 她的 TurnType 跟我們專案的全域 TurnType 同名，混用 using 會讓編譯器對每個沒加前綴的
/// TurnType 都報 ambiguous reference，所以全部用完整路徑 Navigation.xxx 明確指定。
///
/// 使用方式：Tools → 導航系統 → 安裝真實車輪物理與自動駕駛 (NavigationCore)
/// </summary>
public static class NavigationCoreVehicleInstaller
{
    [MenuItem("Tools/導航系統/安裝真實車輪物理與自動駕駛 (NavigationCore)")]
    public static void InstallVehiclePhysics()
    {
        CarController car = Object.FindObjectOfType<CarController>();
        if (car == null)
        {
            EditorUtility.DisplayDialog(
                "安裝失敗",
                "場景中找不到掛有 CarController 的車輛，請先確認車輛已放入場景中。",
                "確定");
            return;
        }

        NavigationLineManager ourLineManager = Object.FindObjectOfType<NavigationLineManager>();
        if (ourLineManager == null)
        {
            EditorUtility.DisplayDialog(
                "安裝失敗",
                "場景中找不到我們自己的 NavigationLineManager，請先執行「Tools → 導航系統 → 安裝到目前場景」。",
                "確定");
            return;
        }

        GameObject vehicleObject = car.gameObject;

        NormalizeVehicleRotation(vehicleObject);
        FixBodyBoxCollider(vehicleObject);

        // 停用我們原本簡化版的 CarController（直接改 Rigidbody 速度那種），
        // 換成 VehiclePhysicsController 接手同一個 Rigidbody，避免兩邊同時對它下指令互相打架。
        // 不刪除、只停用：想比較手感或退回原本的簡化駕駛，隨時把它勾選回來就好。
        car.enabled = false;

        Navigation.VehiclePhysicsController physicsController = AddVehiclePhysicsRig(vehicleObject);
        Navigation.NavigationLineManager bridgeLineManager = SetupBridgeLineManager(car.transform);
        RoadGridPathfinder pathfinder = Object.FindObjectOfType<RoadGridPathfinder>();
        Navigation.AutoDriveController autoDrive = SetupAutoDrive(vehicleObject, bridgeLineManager, pathfinder);
        Navigation.DriveModeSwitcher driveModeSwitcher = SetupDriveModeSwitcher(vehicleObject, physicsController, autoDrive);
        SetupBridge(vehicleObject, ourLineManager, bridgeLineManager, driveModeSwitcher);
        SetupSpeedometerBridge(car, vehicleObject);

        if (vehicleObject.GetComponent<VehicleUprightStabilizer>() == null)
        {
            Undo.AddComponent<VehicleUprightStabilizer>(vehicleObject);
        }

        VehicleReverseBrakeAssist reverseBrakeAssist = vehicleObject.GetComponent<VehicleReverseBrakeAssist>();
        if (reverseBrakeAssist == null)
        {
            reverseBrakeAssist = Undo.AddComponent<VehicleReverseBrakeAssist>(vehicleObject);
        }
        reverseBrakeAssist.physicsController = physicsController;

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        EditorUtility.DisplayDialog(
            "安裝完成",
            "已安裝：\n" +
            "・車輛起始姿態已強制歸零成水平（如果之前測試翻車時被存檔成歪的，這裡會自動修正，只保留車頭朝向）\n" +
            "・車身原本的 Box Collider 位置已校正（原本嚴重偏移到車身外側，會拉歪自動計算的質心與慣性張量，是靜止傾斜/原地彈跳的根本原因）\n" +
            "・Rigidbody 質心改用手動計算（關閉了 Automatic Center Of Mass，先前這個選項會讓手動設定的質心被無視）\n" +
            "・Rigidbody 慣性張量主軸強制對齊車身座標軸（關閉了 Automatic Inertia Tensor，避免主軸偏移導致 Freeze Rotation 某一軸還是會漏轉幾度）\n" +
            "・4 個 WheelCollider + Navigation.VehiclePhysicsController（真實車輪物理，輪子位置是依車輛模型的實際外觀尺寸自動估算的，" +
            "如果看起來輪子沒有對齊視覺輪胎，麻煩到 Hierarchy 底下的 WheelCollider_* 四個子物件手動微調位置）\n" +
            "・原本的 CarController 已停用（沒有刪除，想換回來就重新勾選）\n" +
            "・Navigation.AutoDriveController + DriveModeSwitcher（按 Tab 切換手動 / 自動駕駛）\n" +
            "・NavigationCoreVehicleBridge：你在小地圖 / 全景地圖上選的路線，會自動同步給自動駕駛使用\n" +
            "・防打轉（甩尾/翻車防護）參數已調得更保守，比原始設定更早介入修正\n" +
            "・Rigidbody 物理求解迭代次數提高，讓 Freeze Rotation 在強烈碰撞衝量下更不容易被撞破\n" +
            "・新增 VehicleUprightStabilizer：每個物理步驟強制把車身拉回水平，不依賴物理引擎自己收斂，杜絕長時間轉彎側傾累積\n" +
            "・新增 VehicleReverseBrakeAssist：前進中瞬間按反方向鍵會先視為煞車，完全停下來才切換反向出力，修正之前「先怪怪加速再減速」的手感\n" +
            "・自動駕駛貼地邏輯改用 AutoDriveGroundFollow（進自動駕駛當下即時量測正確的離地高度），修正路徑上方有障礙物時車輛會飛越過去的問題\n" +
            "・新增 AutoDriveObstacleAvoidance：自動駕駛時偵測正前方障礙物會平滑往左右閃避，不再直接穿模過去（純粹是視覺上的側閃修正，不是真正的路徑重新規劃）\n" +
            "・閃避方向改為優先挑選離真實道路網格較近的一側，避免轉向時開上人行道/空地\n" +
            "・目的地「最後停靠精準點」改成優先偵測人行道方塊：沿路只要會先碰到人行道，就停在人行道前面，不會為了貼近建築物邊界而直接壓過人行道\n" +
            "・修正閃避距離計算：改成把障礙物邊界框投影到車身的側向/車頭方向分別計算寬度和長度，不再對又長又窄、順著馬路停的障礙物（卡車、公車）誤把長度當寬度，導致算出的側閃距離超出馬路範圍、車輛卡住磨蹭\n" +
            "・NavigationCoreVehicleBridge 新增抵達終點自動切回手動駕駛：修正之前導航明明已經走到終點附近、畫面上的線也消失了，車輛卻還會自己繼續開一段的問題\n" +
            "・CarResetController 新增「翻覆自動原地扶正」：側翻超過角度門檻並持續一段時間會自動扶正，不會傳送回起點、不影響導航進度\n" +
            "・時速表改由 SpeedometerBridge 接手更新（沿用原本的 speedText，CarController 停用後不會再壞掉顯示）\n\n" +
            "測試前記得：\n" +
            "1. 先點地圖選一個目的地，路線要有內容，自動駕駛才有得開\n" +
            "2. 存檔 (Ctrl+S)\n\n" +
            "已知限制（NavigationCore 原專案就有、不是這次搬移造成的）：\n" +
            "・自動駕駛實測速度只會達到設定值的四分之一左右，原因原作者尚未查明\n" +
            "・手動駕駛的操控手感是針對她原本的照片級掃描地圖調校的；我們的 SimplePoly 低多邊形道路是乾淨路面，" +
            "預期會比原專案好開，但沒有實測過\n" +
            "・車輪視覺模型可能會被塗成一顆球（原專案已知的網格法線問題，還沒修）",
            "了解");
    }

    /// <summary>
    /// 把車輛的起始姿態強制歸零成水平（只保留 Y 軸朝向/車頭方向，不動 Y）。
    ///
    /// 起因：先前測試中車輛真的翻覆過（「翻得更嚴重了，還會原地跳躍」那一輪），
    /// 當時歪斜的 Transform.rotation 很可能就這樣被存進場景檔。之後即使修好了質量、
    /// 懸吊、FreezeRotationX|Z，Freeze Rotation 也只會「擋住之後物理引擎再改動 X/Z」，
    /// 並不會把已經存檔的歪斜角度歸零——於是每次進 Play 模式車輛都從歪掉的姿態開始，
    /// 然後被凍結鎖死在錯的角度上，外觀看起來就像「車子連靜止時都是斜的」。
    /// 每次執行安裝都強制跑一次，確保不管場景之前被存成什麼樣子，起始姿態一定水平。
    /// </summary>
    private static void NormalizeVehicleRotation(GameObject vehicleObject)
    {
        Transform t = vehicleObject.transform;
        Vector3 euler = t.eulerAngles;
        if (Mathf.Approximately(euler.x, 0f) && Mathf.Approximately(euler.z, 0f))
        {
            return; // 已經是水平的，不用動，避免產生多餘的 Undo 紀錄
        }

        Undo.RecordObject(t, "Normalize Vehicle Rotation");
        t.eulerAngles = new Vector3(0f, euler.y, 0f);
    }

    /// <summary>
    /// 車身原本就帶著一個 Box Collider（供舊版 CarController 使用——單純改 Rigidbody 速度的
    /// 簡化控制器不管碰撞體實際位置對不對，只要有個 Collider 讓車輛不會掉出地面、撞到東西
    /// 有反應就行），實測發現它的 Center 座標是 (5.96, 0.49, -0.01)，X 幾乎偏出車身 6 個單位，
    /// 等於一塊跟車身差不多大的碰撞體積浮在車身外側很遠的地方。
    ///
    /// 這在舊版 CarController 底下完全不會被發現——它不看碰撞體貼不貼地，車子照樣正常跑。
    /// 但换成 WheelCollider 物理後，這顆嚴重偏移的 Box Collider 一樣掛在同一個 Rigidbody 上，
    /// 而 Rigidbody 的 Automatic Center Of Mass / Automatic Tensor 預設都是勾選的——Unity
    /// 會把這顆偏移的碰撞體積一起算進質心與慣性張量，讓整台車的質量分布嚴重偏向一側，
    /// 這正是車輛靜止時會傾斜、原地彈跳的根本原因，跟 WheelCollider 擺放位置無關。
    ///
    /// 修正方式：用車身本體的 Mesh Renderer 外觀邊界重新計算該有的 Center / Size，讓碰撞
    /// 體積確實包住車身，而不是憑感覺校正一個猜測值。找不到 Box Collider 或車身 Renderer
    /// 時略過，不強制新增或亂猜。
    /// </summary>
    private static void FixBodyBoxCollider(GameObject vehicleObject)
    {
        BoxCollider box = vehicleObject.GetComponent<BoxCollider>();
        if (box == null) return;

        Renderer bodyRenderer = vehicleObject.GetComponent<Renderer>();
        if (bodyRenderer == null) return;

        Transform root = vehicleObject.transform;
        Bounds worldBounds = bodyRenderer.bounds;
        Vector3 localA = root.InverseTransformPoint(worldBounds.min);
        Vector3 localB = root.InverseTransformPoint(worldBounds.max);

        Vector3 min = Vector3.Min(localA, localB);
        Vector3 max = Vector3.Max(localA, localB);

        Vector3 newCenter = (min + max) / 2f;
        Vector3 newSize = max - min;

        if (Vector3.Distance(newCenter, box.center) < 0.05f)
        {
            return; // 已經跟車身邊界對得上，不用動，避免產生多餘的 Undo 紀錄
        }

        Debug.LogWarning(
            $"[NavigationCoreVehicleInstaller] 車身 Box Collider 的 Center 原本是 {box.center}，" +
            $"跟車身 Mesh Renderer 的實際邊界差太多（正確大約是 {newCenter}），已自動修正，" +
            "避免嚴重偏移的碰撞體積拉歪 Unity 自動計算的質心與慣性張量。");

        Undo.RecordObject(box, "Fix Body Box Collider");
        box.center = newCenter;
        box.size = newSize;
    }

    /// <summary>
    /// 依車輛模型的實際外觀尺寸（所有 Renderer 的合併邊界）估算車輪該放的左右/前後/高度位置，
    /// 而不是照抄 NavigationCore 原本針對她自己那台程式產生的轎車模型寫死的數字——
    /// 我們的 FreeCar 車型跟她的模型比例、原點位置很可能不一樣，照抄會讓輪子懸空或埋進車身。
    ///
    /// 車寬/車長的「中心」是取邊界框本身的中心，不是假設車輛的 Transform 原點 (0,0) 剛好在
    /// 車身正中央——很多購買/免費的車輛模型 Pivot 沒有精準對齊幾何中心。原本的版本直接把
    /// 左右輪放在 ±halfTrackWidth（相對 Transform 原點），如果原點沒有剛好在車身中線上，
    /// 左右輪相對「實際車身」就不對稱：某一側的輪子可能懸空或卡進車殼，車輛靜止時就會因為
    /// 那一側懸吊沒有正常支撐而往那邊斜靠、翻向一邊。
    ///
    /// 抓不到任何 Renderer 時（例如車輛模型還沒指定）才退回一般房車常見的概略尺寸，此時假設
    /// 原點就在中心。
    /// </summary>
    private static void ComputeVehicleFootprint(
        GameObject vehicleObject, out float halfTrackWidth, out float halfWheelBase, out float wheelLocalY,
        out float centerLocalX, out float centerLocalZ)
    {
        Renderer[] renderers = vehicleObject.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
        {
            halfTrackWidth = 0.8f;
            halfWheelBase = 1.3f;
            wheelLocalY = -0.5f;
            centerLocalX = 0f;
            centerLocalZ = 0f;
            return;
        }

        Bounds worldBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            worldBounds.Encapsulate(renderers[i].bounds);
        }

        Transform root = vehicleObject.transform;
        Vector3 localMin = root.InverseTransformPoint(worldBounds.min);
        Vector3 localMax = root.InverseTransformPoint(worldBounds.max);

        // InverseTransformPoint 沒有保證 min/max 分量對應關係在旋轉物件上還是「min < max」，
        // 保險起見兩兩取最小/最大，避免車輛本身有奇怪的縮放或旋轉時算出負的寬度/長度。
        float minX = Mathf.Min(localMin.x, localMax.x);
        float maxX = Mathf.Max(localMin.x, localMax.x);
        float minZ = Mathf.Min(localMin.z, localMax.z);
        float maxZ = Mathf.Max(localMin.z, localMax.z);

        float width = maxX - minX;
        float length = maxZ - minZ;

        // 輪子抓車寬 45%、車長 42% 的位置，落在車身邊緣內側一點（抓車寬一半太寬，輪子會跑到車殼外面）。
        halfTrackWidth = Mathf.Max(0.3f, width * 0.45f);
        halfWheelBase = Mathf.Max(0.3f, length * 0.42f);

        // 輪子中心大約落在車身底部再往上一點（輪胎半徑的高度），不是貼齊模型最底部。
        wheelLocalY = Mathf.Min(localMin.y, localMax.y) + 0.35f;

        centerLocalX = (minX + maxX) / 2f;
        centerLocalZ = (minZ + maxZ) / 2f;
    }

    private static Navigation.VehiclePhysicsController AddVehiclePhysicsRig(GameObject vehicleObject)
    {
        Rigidbody rb = vehicleObject.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = Undo.AddComponent<Rigidbody>(vehicleObject);
        }

        // 車輛質量一定要設，不能沿用 CarController 原本的 Rigidbody（預設是 Unity 的 1 公斤）。
        // 下面 CreateWheelCollider 用的懸吊彈簧係數（spring: 35000, damper: 4500）是她針對
        // 1200 公斤房車校調出來的一整套數字，如果車身只有 1 公斤，這股彈簧力相對質量會強上
        // 一千多倍——一落地懸吊要把車頂回靜止位置，瞬間就把只有 1 公斤重的車體轟上天，
        // 這正是「一進 Play 模式車就被彈飛」最典型的成因。drag/angularDrag 也一併對齊她的校調值。
        rb.mass = 1200f;
        rb.drag = 0.05f;
        rb.angularDrag = 0.5f;

        // Automatic Center Of Mass 預設是勾選的：勾選時 Unity 會無視下面手動指定的
        // centerOfMassOffset，自己依身上所有 Collider 的形狀/位置重新計算質心，
        // 手動賦值等於白做工。必須關掉才能讓我們算出來的質心真正生效。
        rb.automaticCenterOfMass = false;

        // 只允許繞 Y 軸旋轉（方向盤轉向），避免撞車或懸吊瞬間衝擊力造成翻覆、畫面倒過來。
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        // Freeze Rotation 是物理引擎「迭代收斂」出來的約束，不是絕對不可能違反的鐵律——
        // 車輛高速甩尾撞上路緣、燈柱這類強烈碰撞衝量，如果求解器迭代次數不夠，可能
        // 來不及在單一物理步驟內完全收斂，就會出現「明明凍結了，還是被撞到側翻」的情況。
        // 預設迭代次數（通常是 Project Settings 裡的 6）對一般平穩行駛夠用，對這種
        // 瞬間大衝量場景不夠，提高這台車自己的迭代次數讓約束求解更確實。
        rb.solverIterations = 20;
        rb.solverVelocityIterations = 20;

        // Freeze Rotation 實際上是在「慣性張量主軸」的座標空間生效，不是單純的 Transform
        // local 座標軸。Automatic Inertia Tensor 開著時，Unity 會依身上所有 Collider
        // 的形狀/位置自動算主軸方向——這台車的 Box Collider、四個 WheelCollider 位置
        // 並不完全對稱（實測前後輪本地 Y 高度就差了 0.027），算出來的主軸很可能跟
        // Transform 本身的座標軸有些微偏差。偏差存在時，「凍結 X 軸」凍的其實是偏移過的
        // 主軸，不是視覺上的水平軸，車輛靜止時就會慢慢漏轉到某個角度才穩定下來——
        // 這正是 Freeze Rotation X/Z 都有勾選、Z 軸穩穩貼著 0，X 軸卻還是會漏轉出幾度
        // 傾角的成因。做法：用車身外觀邊界框當一個簡化長方體，手動算出慣性張量的大小，
        // 主軸方向直接鎖定跟 Transform 對齊（Quaternion.identity），不讓 Unity 自動算
        // 主軸方向。
        Renderer bodyRendererForTensor = vehicleObject.GetComponent<Renderer>();
        if (bodyRendererForTensor != null)
        {
            Vector3 tMin = vehicleObject.transform.InverseTransformPoint(bodyRendererForTensor.bounds.min);
            Vector3 tMax = vehicleObject.transform.InverseTransformPoint(bodyRendererForTensor.bounds.max);
            Vector3 size = new Vector3(Mathf.Abs(tMax.x - tMin.x), Mathf.Abs(tMax.y - tMin.y), Mathf.Abs(tMax.z - tMin.z));

            float ixx = (1f / 12f) * rb.mass * (size.y * size.y + size.z * size.z);
            float iyy = (1f / 12f) * rb.mass * (size.x * size.x + size.z * size.z);
            float izz = (1f / 12f) * rb.mass * (size.x * size.x + size.y * size.y);

            rb.automaticInertiaTensor = false;
            rb.inertiaTensor = new Vector3(Mathf.Max(ixx, 0.01f), Mathf.Max(iyy, 0.01f), Mathf.Max(izz, 0.01f));
            rb.inertiaTensorRotation = Quaternion.identity;
        }

        // 第一版用「車身整體外觀邊界框」去猜輪子該放哪裡，結果猜錯了方向（用車輛整體 Renderer
        // 邊界，含輪胎本身在內，去反推左右中心點），實測反而讓車翻得更嚴重、還會原地彈跳——
        // 代表邊界框中心跟輪子實際該落的位置沒有必然關係，純靠猜的做法本身就不可靠。
        //
        // 改用更直接、更可靠的依據：FreeCar 這個車型本來就有 4 個名稱包含「wheel」的視覺輪胎
        // 子物件（free_car_wheel），而且這台車一直是用原本的 CarController 開著到處跑、
        // 視覺上輪胎本來就穩穩貼在地面上——代表這 4 個輪胎子物件目前的位置就是「正確答案」，
        // 不需要用邊界框反推，直接照抄它們的位置放 WheelCollider 就好。
        // 只有在真的找不到剛好 4 個輪子子物件時，才退回邊界框估算當備案。
        bool foundWheelMeshes = TryFindWheelLocalPositionsFromMeshes(
            vehicleObject, out Vector3 flPos, out Vector3 frPos, out Vector3 rlPos, out Vector3 rrPos, out float wheelRadius);

        float centerOfMassX;
        float centerOfMassY;
        float centerOfMassZ;

        if (!foundWheelMeshes)
        {
            Debug.LogWarning(
                "[NavigationCoreVehicleInstaller] 找不到剛好 4 個名稱包含「wheel」的子物件，" +
                "改用車身外觀邊界框估算輪子位置，準確度比較低，請安裝完後務必檢查輪子跟車身是否貼合。");

            ComputeVehicleFootprint(vehicleObject, out float tw, out float wb, out float wheelY, out float cx, out float cz);
            flPos = new Vector3(cx - tw, wheelY, cz + wb);
            frPos = new Vector3(cx + tw, wheelY, cz + wb);
            rlPos = new Vector3(cx - tw, wheelY, cz - wb);
            rrPos = new Vector3(cx + tw, wheelY, cz - wb);
            wheelRadius = 0.35f;

            centerOfMassX = cx;
            centerOfMassY = wheelY + 0.15f;
            centerOfMassZ = cz;
        }
        else
        {
            // 質心用 4 個輪子實際位置的中點，比車身邊界框中心更貼近「這台車實際的重量分布重心」，
            // Y 再往上加一點（真實車輛質心通常比輪軸中心高一些）。
            Vector3 wheelCenter = (flPos + frPos + rlPos + rrPos) / 4f;
            centerOfMassX = wheelCenter.x;
            centerOfMassY = wheelCenter.y + 0.15f;
            centerOfMassZ = wheelCenter.z;
        }

        // 診斷用：車身修過兩次不對稱的問題（Box Collider 偏移、起始姿態歪斜）之後，
        // 如果車輛還是會朝某個方向傾斜，下一個嫌疑對象就是這四個輪子本地座標彼此是否
        // 真的對稱——與其憑截圖猜角度，直接把算出來的數字印出來對照比較快。
        Debug.Log(
            $"[NavigationCoreVehicleInstaller] 診斷—四輪本地座標 FL={flPos:F3} FR={frPos:F3} " +
            $"RL={rlPos:F3} RR={rrPos:F3}，輪胎半徑={wheelRadius:F3}，" +
            $"質心=({centerOfMassX:F3}, {centerOfMassY:F3}, {centerOfMassZ:F3})");

        WheelCollider frontLeft = CreateWheelCollider(vehicleObject.transform, "WheelCollider_FrontLeft", flPos, wheelRadius);
        WheelCollider frontRight = CreateWheelCollider(vehicleObject.transform, "WheelCollider_FrontRight", frPos, wheelRadius);
        WheelCollider rearLeft = CreateWheelCollider(vehicleObject.transform, "WheelCollider_RearLeft", rlPos, wheelRadius);
        WheelCollider rearRight = CreateWheelCollider(vehicleObject.transform, "WheelCollider_RearRight", rrPos, wheelRadius);

        Navigation.VehiclePhysicsController controller = vehicleObject.GetComponent<Navigation.VehiclePhysicsController>();
        if (controller == null)
        {
            controller = Undo.AddComponent<Navigation.VehiclePhysicsController>(vehicleObject);
        }

        SerializedObject so = new SerializedObject(controller);
        so.FindProperty("frontLeftWheel").objectReferenceValue = frontLeft;
        so.FindProperty("frontRightWheel").objectReferenceValue = frontRight;
        so.FindProperty("rearLeftWheel").objectReferenceValue = rearLeft;
        so.FindProperty("rearRightWheel").objectReferenceValue = rearRight;
        so.FindProperty("centerOfMassOffset").vector3Value = new Vector3(centerOfMassX, centerOfMassY, centerOfMassZ);

        // 實測轉彎一段時間會甩尾、甩到一定程度會翻車。VehiclePhysicsController 本來就有
        // 一套「防打轉」機制（她的註解：後輪驅動+重車+全油門滿舵，動力本來就容易超過
        // 側向抓地上限），但預設門檻（偏航角速度超過 90 度/秒、側滑角超過 25 度才修正）
        // 是針對她原本的車型跟地圖調的，套到我們這台車上顯然不夠早介入。調得更保守：
        // 門檻降低、修正力道加大，讓車輛在滑出去太多之前就先被拉回來，犧牲一點甩尾的
        // 「手感」換取比較安全、好控制的行駛體驗（這套系統的用途是行前導航熟悉，不是
        // 賽車遊戲）。這是手感調校，數值可能還要依實際試駕感受再微調。
        so.FindProperty("maxYawRateDegrees").floatValue = 50f;
        so.FindProperty("yawDampStrength").floatValue = 7f;
        so.FindProperty("sideSlipLimitDegrees").floatValue = 15f;

        so.ApplyModifiedProperties();

        return controller;
    }

    /// <summary>
    /// 找車輛底下名稱包含「wheel」（不分大小寫）的子物件，換算成相對車輛 Transform 原點的
    /// 本地座標，設法整理出剛好 4 個輪位，並依 local Z（前/後）與 local X（左/右）分類；
    /// 同時用外觀高度估算輪胎半徑（取代寫死的 0.35，避免半徑跟這台車實際的輪胎大小對不上
    /// ——半徑不對，WheelCollider 認定的接地點跟視覺輪胎的實際接地點會差一截，懸吊會一直
    /// 在錯誤的高度嘗試貼地，也是造成原地彈跳的常見原因）。找不到剛好 4 個輪位就回傳
    /// false，呼叫端自行決定備案。
    ///
    /// 排除已經有 WheelCollider 元件的子物件：這個安裝腳本自己創建的 4 個碰撞體物件
    /// 命名為 WheelCollider_FrontLeft/FrontRight/RearLeft/RearRight，名稱本身就包含
    /// 「Wheel」，重新執行安裝（冪等性設計本來就允許重複執行）時不排除會把自己創建的
    /// 物件也算進「wheel」名稱搜尋。
    ///
    /// 不能直接假設「符合命名的子物件數 == 4」：實測 FreeCar 這個車型，符合命名的子物件
    /// 共有 8 個、全部叫 free_car_wheel，分成兩組——一組半徑估計約 0.28~0.30、位置的
    /// 輪距/軸距比例符合正常車輛比例；另一組半徑估計只有約 0.20、位置擠在車身中央附近，
    /// 比較像用不到的參考/替代網格，不是實際顯示的輪胎。兩組之間半徑差距（0.204 到 0.282，
    /// 跳了 0.078）明顯大於同一組內部的差異，所以策略分兩階段：
    /// 1. 先直接對全部候選物件做位置分群（同一顆輪胎如果真的是輪胎+鋼圈疊在同一位置的
    ///    兩個網格，分群後會自動合併成一群），分群後剛好 4 群就直接採用。
    /// 2. 不是 4 群的話，依半徑排序找出「差距最大的分界點」，只留下差距分界點以上、
    ///    半徑較大的那一組再重新分群——用「最大斷層」而不是寫死的比例或公尺數篩選，
    ///    才不會被不同車型、不同縮放比例的模型絆倒。
    /// 兩階段都湊不出剛好 4 個輪位，才視為失敗。
    /// </summary>
    private static bool TryFindWheelLocalPositionsFromMeshes(
        GameObject vehicleObject, out Vector3 frontLeft, out Vector3 frontRight, out Vector3 rearLeft, out Vector3 rearRight,
        out float estimatedRadius)
    {
        frontLeft = frontRight = rearLeft = rearRight = Vector3.zero;
        estimatedRadius = 0.35f;

        List<Transform> wheelTransforms = new List<Transform>();
        foreach (Transform t in vehicleObject.GetComponentsInChildren<Transform>())
        {
            if (t == vehicleObject.transform) continue;
            if (t.GetComponent<WheelCollider>() != null) continue; // 排除我們自己先前安裝的 WheelCollider_* 物件
            if (t.name.IndexOf("wheel", System.StringComparison.OrdinalIgnoreCase) < 0) continue;

            wheelTransforms.Add(t);
        }

        List<Vector3> allLocalPositions = new List<Vector3>();
        List<float> allRadiusSamples = new List<float>();
        foreach (Transform t in wheelTransforms)
        {
            allLocalPositions.Add(vehicleObject.transform.InverseTransformPoint(t.position));

            Renderer wheelRenderer = t.GetComponentInChildren<Renderer>();
            // 輪胎側面看起來接近正圓形，用外觀邊界框的高度（Y 方向）除以 2 當半徑估計值，
            // 比寬度更可靠——寬度包含胎面厚度，高度才是實際輪胎直徑方向。
            allRadiusSamples.Add(wheelRenderer != null ? wheelRenderer.bounds.size.y / 2f : 0f);
        }

        // 第一階段：直接對全部候選物件做位置分群。
        if (TryResolveFourWheelsFromIndices(
            AllIndices(allLocalPositions.Count), allLocalPositions, allRadiusSamples,
            out frontLeft, out frontRight, out rearLeft, out rearRight, out estimatedRadius))
        {
            return true;
        }

        // 第二階段：依半徑排序找「差距最大的分界點」，只留下半徑較大的那一組再重新分群。
        List<int> sortedByRadius = AllIndices(allRadiusSamples.Count);
        sortedByRadius.Sort((a, b) => allRadiusSamples[a].CompareTo(allRadiusSamples[b]));

        int splitAfter = -1;
        float biggestGap = -1f;
        for (int i = 0; i < sortedByRadius.Count - 1; i++)
        {
            float gap = allRadiusSamples[sortedByRadius[i + 1]] - allRadiusSamples[sortedByRadius[i]];
            if (gap > biggestGap)
            {
                biggestGap = gap;
                splitAfter = i;
            }
        }

        if (splitAfter >= 0)
        {
            List<int> largerRadiusIndices = sortedByRadius.GetRange(splitAfter + 1, sortedByRadius.Count - splitAfter - 1);
            if (TryResolveFourWheelsFromIndices(
                largerRadiusIndices, allLocalPositions, allRadiusSamples,
                out frontLeft, out frontRight, out rearLeft, out rearRight, out estimatedRadius))
            {
                return true;
            }
        }

        // 兩階段都湊不出剛好 4 個輪位：把完整原始資料印出來，比繼續憑空猜測策略更快查出原因。
        List<string> rawInfo = new List<string>();
        for (int i = 0; i < allLocalPositions.Count; i++)
        {
            rawInfo.Add($"{allLocalPositions[i]:F2}(半徑估計{allRadiusSamples[i]:F3})");
        }
        Debug.LogWarning(
            $"[NavigationCoreVehicleInstaller] 診斷—車輛「{GetHierarchyPath(vehicleObject.transform)}」" +
            $"底下名稱包含「wheel」且非 WheelCollider 的子物件共 {wheelTransforms.Count} 個，" +
            "直接分群、依半徑分組後再分群都無法剛好湊出 4 個輪位：" +
            (rawInfo.Count > 0 ? string.Join("；", rawInfo) : "（一個都沒找到）"));
        return false;
    }

    private static List<int> AllIndices(int count)
    {
        List<int> indices = new List<int>(count);
        for (int i = 0; i < count; i++) indices.Add(i);
        return indices;
    }

    /// <summary>
    /// 對一組候選物件（用它們在 allLocalPositions/allRadiusSamples 裡的 index 表示）做位置
    /// 分群，分群結果剛好 4 群才視為成功，並依 local Z/X 分類成四個輪位、算出質心用的
    /// 平均半徑。分群數不是 4 就回傳 false，不印任何診斷訊息（診斷訊息交給最外層統一印）。
    /// </summary>
    private static bool TryResolveFourWheelsFromIndices(
        List<int> candidateIndices, List<Vector3> allLocalPositions, List<float> allRadiusSamples,
        out Vector3 frontLeft, out Vector3 frontRight, out Vector3 rearLeft, out Vector3 rearRight,
        out float estimatedRadius)
    {
        frontLeft = frontRight = rearLeft = rearRight = Vector3.zero;
        estimatedRadius = 0.35f;

        List<Vector3> positions = new List<Vector3>();
        foreach (int idx in candidateIndices) positions.Add(allLocalPositions[idx]);

        // 同一顆輪胎如果真的是由多個網格組成（例如輪胎/鋼圈疊在同一位置），彼此距離
        // 應該只有幾公分，遠小於左右輪距或前後軸距，0.5 公尺當分群門檻很安全。
        List<List<int>> clusters = ClusterByProximity(positions, 0.5f);
        if (clusters.Count != 4)
        {
            return false;
        }

        List<Vector3> localPositions = new List<Vector3>();
        List<float> perWheelRadius = new List<float>();
        foreach (List<int> cluster in clusters)
        {
            Vector3 sum = Vector3.zero;
            float maxRadius = 0f;
            foreach (int localIdx in cluster)
            {
                int globalIdx = candidateIndices[localIdx];
                sum += allLocalPositions[globalIdx];
                maxRadius = Mathf.Max(maxRadius, allRadiusSamples[globalIdx]);
            }
            localPositions.Add(sum / cluster.Count);
            perWheelRadius.Add(maxRadius);
        }

        float radiusSum = 0f;
        int radiusCount = 0;
        foreach (float r in perWheelRadius)
        {
            if (r > 0.05f) // 太小可能是量到雜訊或錯誤的子物件，不採用
            {
                radiusSum += r;
                radiusCount++;
            }
        }
        if (radiusCount > 0)
        {
            estimatedRadius = radiusSum / radiusCount;
        }

        // 先依 local Z 排序分出前 2 個（車頭方向，Z 較大）跟後 2 個，同一組再依 local X 分左右
        // （左邊 X 較小）。这個排序方式不假設任何固定座標值，只依賴「四個輪子彼此的相對關係」，
        // 對車輛實際尺寸、比例不敏感，比寫死數字或猜邊界框中心可靠。
        localPositions.Sort((a, b) => b.z.CompareTo(a.z));

        Vector3 f1 = localPositions[0];
        Vector3 f2 = localPositions[1];
        Vector3 r1 = localPositions[2];
        Vector3 r2 = localPositions[3];

        frontLeft = f1.x <= f2.x ? f1 : f2;
        frontRight = f1.x <= f2.x ? f2 : f1;
        rearLeft = r1.x <= r2.x ? r1 : r2;
        rearRight = r1.x <= r2.x ? r2 : r1;

        return true;
    }

    /// <summary>
    /// 把一組本地座標依「距離門檻內視為同一群」分群（連通分量），用於把同一顆輪胎的
    /// 多個組成網格（例如輪胎+鋼圈）合併成一個代表點，而不是誤判成好幾顆不同的輪胎。
    /// </summary>
    private static List<List<int>> ClusterByProximity(List<Vector3> points, float threshold)
    {
        int n = points.Count;
        List<List<int>> clusters = new List<List<int>>();
        bool[] visited = new bool[n];

        for (int i = 0; i < n; i++)
        {
            if (visited[i]) continue;

            List<int> cluster = new List<int>();
            Queue<int> queue = new Queue<int>();
            queue.Enqueue(i);
            visited[i] = true;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                cluster.Add(current);

                for (int j = 0; j < n; j++)
                {
                    if (visited[j]) continue;
                    if (Vector3.Distance(points[current], points[j]) <= threshold)
                    {
                        visited[j] = true;
                        queue.Enqueue(j);
                    }
                }
            }

            clusters.Add(cluster);
        }

        return clusters;
    }

    /// <summary>診斷訊息用：印出物件從車輛根節點算起的完整巢狀路徑，方便對照 Hierarchy 視窗。</summary>
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

    private static WheelCollider CreateWheelCollider(Transform parent, string name, Vector3 localPosition, float radius)
    {
        Transform existing = parent.Find(name);
        GameObject wheelObj;

        if (existing != null)
        {
            wheelObj = existing.gameObject;
        }
        else
        {
            wheelObj = new GameObject(name, typeof(WheelCollider));
            Undo.RegisterCreatedObjectUndo(wheelObj, $"Create {name}");
            wheelObj.transform.SetParent(parent, false);
        }

        wheelObj.transform.localPosition = localPosition;

        // 強制歸零旋轉/縮放：這幾個子物件在除錯過程中被邀請「手動微調位置」，
        // 不排除有次調整時滑鼠不小心也連旋轉或縮放一起動到——WheelCollider 的懸吊
        // 是沿著自己 Transform 的本地 Y 軸伸縮，旋轉/縮放歪掉會讓懸吊實際作用方向
        // 跟預期的垂直方向不一致，四輪之間只要有一輪不一致就會讓車身各邊支撐力不對稱、
        // 靜止時往某個方向傾斜。每次安裝都強制歸零，不管之前被調成怎樣。
        wheelObj.transform.localRotation = Quaternion.identity;
        wheelObj.transform.localScale = Vector3.one;

        WheelCollider wheel = wheelObj.GetComponent<WheelCollider>();
        wheel.radius = radius;
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
    /// 建一個獨立、不顯示線的 Navigation.NavigationLineManager，純粹當 AutoDriveController
    /// 讀路線用的資料容器——它自帶 LineRenderer（RequireComponent），這裡直接關掉那個 LineRenderer，
    /// 避免跟我們自己畫的導航線疊在一起變成兩條線互相打架。
    /// </summary>
    private static Navigation.NavigationLineManager SetupBridgeLineManager(Transform playerTransform)
    {
        GameObject obj = GameObject.Find("NavigationCoreRouteBridge");
        if (obj == null)
        {
            obj = new GameObject("NavigationCoreRouteBridge");
            Undo.RegisterCreatedObjectUndo(obj, "Create NavigationCoreRouteBridge");
        }

        LineRenderer lr = obj.GetComponent<LineRenderer>();
        if (lr == null)
        {
            lr = Undo.AddComponent<LineRenderer>(obj);
        }
        lr.enabled = false; // 只當資料容器用，不要真的畫出第二條線

        Navigation.NavigationLineManager lineManager = obj.GetComponent<Navigation.NavigationLineManager>();
        if (lineManager == null)
        {
            lineManager = Undo.AddComponent<Navigation.NavigationLineManager>(obj);
        }

        SerializedObject so = new SerializedObject(lineManager);
        so.FindProperty("player").objectReferenceValue = playerTransform;
        so.ApplyModifiedProperties();

        return lineManager;
    }

    private static Navigation.AutoDriveController SetupAutoDrive(
        GameObject vehicleObject, Navigation.NavigationLineManager bridgeLineManager, RoadGridPathfinder pathfinder)
    {
        Navigation.AutoDriveController autoDrive = vehicleObject.GetComponent<Navigation.AutoDriveController>();
        if (autoDrive == null)
        {
            autoDrive = Undo.AddComponent<Navigation.AutoDriveController>(vehicleObject);
        }

        SerializedObject so = new SerializedObject(autoDrive);
        so.FindProperty("lineManager").objectReferenceValue = bridgeLineManager;

        // 她原本的貼地邏輯（射線打到的最高點當地面）是為了 Cesium 圖磚場景寫的，套到我們
        // 平面城市地圖上，路徑正上方只要有裝飾物、招牌、柵欄，車輛就會被拉去貼合那個高度，
        // 變成飛越障礙物。關掉讓 AutoDriveGroundFollow 接手（用「離目前高度最近的表面」）。
        so.FindProperty("followGround").boolValue = false;
        so.ApplyModifiedProperties();

        AutoDriveGroundFollow groundFollow = vehicleObject.GetComponent<AutoDriveGroundFollow>();
        if (groundFollow == null)
        {
            groundFollow = Undo.AddComponent<AutoDriveGroundFollow>(vehicleObject);
        }
        groundFollow.autoDriveController = autoDrive;
        // 每次安裝都強制覆蓋這兩個值，避免舊版本裝過的元件卡著舊的序列化預設值不會自動更新。
        groundFollow.probeDistance = 15f;
        groundFollow.maxDeviationBeforeFallback = 1f;

        AutoDriveObstacleAvoidance obstacleAvoidance = vehicleObject.GetComponent<AutoDriveObstacleAvoidance>();
        if (obstacleAvoidance == null)
        {
            obstacleAvoidance = Undo.AddComponent<AutoDriveObstacleAvoidance>(vehicleObject);
        }
        obstacleAvoidance.autoDriveController = autoDrive;
        obstacleAvoidance.pathfinder = pathfinder;
        // 每次安裝都強制覆蓋，避免舊版本裝過的元件卡著舊的序列化值不會自動更新——
        // 舊的偵測球高度/半徑組合會讓球體貼到路面，把路面本身誤判成正前方的障礙物。
        obstacleAvoidance.detectionHeight = 1f;
        obstacleAvoidance.detectionRadius = 0.8f;
        obstacleAvoidance.avoidLateralClearance = 3.5f;
        obstacleAvoidance.avoidForwardDistance = 5f;

        return autoDrive;
    }

    private static Navigation.DriveModeSwitcher SetupDriveModeSwitcher(
        GameObject vehicleObject, Navigation.VehiclePhysicsController manualController, Navigation.AutoDriveController autoController)
    {
        Navigation.DriveModeSwitcher switcher = vehicleObject.GetComponent<Navigation.DriveModeSwitcher>();
        if (switcher == null)
        {
            switcher = Undo.AddComponent<Navigation.DriveModeSwitcher>(vehicleObject);
        }

        SerializedObject so = new SerializedObject(switcher);
        so.FindProperty("manualController").objectReferenceValue = manualController;
        so.FindProperty("autoController").objectReferenceValue = autoController;
        so.FindProperty("vehicleRigidbody").objectReferenceValue = vehicleObject.GetComponent<Rigidbody>();

        // 這台車整學期都穩穩站在 SimplePoly 的地面上測試過，不需要 DriveModeSwitcher 自己的
        // 開場找地面流程——那段邏輯是為了等 Cesium 圖磚非同步生成碰撞網格才寫的，
        // 我們的地圖是一次載入、不是串流地形，用不到，開著反而會讓車輛開場多凍結最多 8 秒。
        so.FindProperty("snapToGroundOnStart").boolValue = false;
        so.ApplyModifiedProperties();

        return switcher;
    }

    private static void SetupBridge(
        GameObject vehicleObject, NavigationLineManager ourLineManager, Navigation.NavigationLineManager bridgeLineManager,
        Navigation.DriveModeSwitcher driveModeSwitcher)
    {
        NavigationCoreVehicleBridge bridge = vehicleObject.GetComponent<NavigationCoreVehicleBridge>();
        if (bridge == null)
        {
            bridge = Undo.AddComponent<NavigationCoreVehicleBridge>(vehicleObject);
        }

        bridge.sourceLineManager = ourLineManager;
        bridge.targetLineManager = bridgeLineManager;
        bridge.driveModeSwitcher = driveModeSwitcher;
    }

    /// <summary>
    /// 原本的時速表是 CarController.Update() 順便更新的，換成 VehiclePhysicsController
    /// 接手駕駛、CarController 停用後，沒有人接手更新這段文字，時速表看起來就像壞掉了。
    /// 直接沿用 CarController 原本就有的 speedText 參照（不管是誰在控制車輛，車身這顆
    /// Rigidbody 的真實物理速度都能拿來換算時速），改用 SpeedometerBridge 接手更新。
    /// </summary>
    private static void SetupSpeedometerBridge(CarController car, GameObject vehicleObject)
    {
        SerializedObject carSo = new SerializedObject(car);
        SerializedProperty speedTextProp = carSo.FindProperty("speedText");
        if (speedTextProp == null || speedTextProp.objectReferenceValue == null)
        {
            return; // 舊版車輛沒接時速表，就不強行新增
        }

        SpeedometerBridge speedometer = vehicleObject.GetComponent<SpeedometerBridge>();
        if (speedometer == null)
        {
            speedometer = Undo.AddComponent<SpeedometerBridge>(vehicleObject);
        }

        speedometer.vehicleRigidbody = vehicleObject.GetComponent<Rigidbody>();
        speedometer.speedText = speedTextProp.objectReferenceValue as TMPro.TMP_Text;
    }
}
