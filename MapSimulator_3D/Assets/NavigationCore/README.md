# NavigationCore — 沉浸式導航功能包

從「沉浸式導航地圖平台」畢專拆出來的導航功能，**不含地圖**。設計成可以直接放進你自己的
Unity 專案、接上你自己的 3D 地圖使用。

---

## 安裝

把 `Assets/NavigationCore` 整個資料夾複製到你專案的 `Assets/` 底下就好。

**連 `.meta` 檔一起複製**（資料夾裡已經包含了）。少了 `.meta`，Unity 會重新配發 GUID，
字型與圖示的參照會全部斷掉。

### 需要的套件

| 套件 | 用途 |
|---|---|
| `com.unity.textmeshpro` | 導航卡片、速度表、ETA 的文字 |
| `com.unity.ugui` | UI Canvas |
| `com.unity.modules.vehicles` | WheelCollider（車輛物理） |

### ⚠️ 必須先匯入 TMP Essential Resources

選單 **Window → TextMeshPro → Import TMP Essential Resources**。

這一步不能省略。本包附的中文字型（`Art/Fonts/ChineseFont.asset`）內嵌的材質會參照
`TMP_SDF-Mobile.shader`，而**那個 shader 不在 TextMeshPro 套件裡**，是隨 TMP Essential
Resources 匯入到專案的 `Assets/TextMesh Pro/` 底下的。

沒有它的話：**編譯不會報錯**（shader 不是編譯期的東西），但執行時所有中文字會變成洋紅色
或整個看不見。這種「沒有任何錯誤訊息」的失敗最難查，所以請先做這一步。

如果專案裡已經有 `Assets/TextMesh Pro/` 資料夾，代表已經匯入過了，不用重做。

**不需要 URP。** 已在只有上述套件、沒有 URP 的空專案中編譯驗證過。
若你的專案有 URP，材質會自動使用 URP 的 Shader；沒有的話會退回內建管線的 `Standard`
（見 `Editor/NavigationShaderCompat.cs`）。

---

## 快速開始

1. 場景裡準備一個名為 **`PlayerVehicle`** 的空物件，放在你地圖上的道路起點位置
2. 確認場景有一台 Tag 為 `MainCamera` 的攝影機
3. 選單 **Tools → Navigation → 一鍵建立導航場景物件**

這一步會自動建立並接好：車輛物理（WheelCollider ×4）、自動駕駛、駕駛模式切換、
跟車攝影機、導航管理器、導航卡片 UI、速度表、ETA 顯示、機車與車種切換。
**可以重複執行**，已存在的物件不會被重建。

4. 選單 **Tools → Navigation → 車輛外觀 → 建立真實轎車模型**（用程式生成一台低多邊形轎車）
5. 選單 **Tools → Navigation → 車輛操控 → 套用好開的操控設定**

### 操作

| 按鍵 | 功能 |
|---|---|
| `W`/`S` 或 `↑`/`↓` | 加速／煞車 |
| `A`/`D` 或 `←`/`→` | 轉向 |
| 空白鍵 | 煞車 |
| 按住滑鼠右鍵 | 自由環顧，放開歸位 |
| `Tab` | 切換自動駕駛／手動駕駛 |
| `V` | 切換汽車／機車 |

---

## 接上你的地圖

### 1. 地圖必須要有碰撞體

貼地機制全部靠 `Physics.Raycast` 打到地面。**你的地圖網格必須有 MeshCollider**，
否則車子會直接穿過去掉下世界。

如果你的地圖是串流載入的（圖磚、分塊地形），碰撞體通常是非同步生成的 ——
`VehicleTerrainSnap` 已經處理了這件事：開場會凍結車輛、等地表穩定後才放行。

### 2. 設定路線

在 `NavigationManager` 物件的 **Navigation Line Manager** 元件上：

- **`waypoints`** — 路口座標清單（世界座標）。這是導航的主體，`ETADisplayUI` 的剩餘距離、
  `NavigationUIManager` 的轉彎指示都是從這裡算出來的
- **`waypointInfos`** — 每個路口對應的轉彎方向與路名
- **`routePath`** — 畫在地上的導航線折線點。比 `waypoints` 密集，只影響視覺

原專案的座標是台灣景美一帶、由 Cesium 的地理參照系統換算出來的，**對你的地圖沒有意義**，
請換成你自己的。最簡單的做法是在 Scene 視窗把空物件拖到路口位置，再把座標填進去。

### 3. 排除不該被當成地面的東西

`VehicleTerrainSnap` 與 `AutoDriveController` 都有這兩個欄位：

- **`buildingsTileset`** — 指向你的建築物根物件。射線會忽略它（屋頂不是路面）
- **`safetyFloorName`** — 防墜隱形地板的名稱，預設 `NavigationSafetyFloor`，
  由「一鍵建立」自動產生，通常不用改

---

## 這包裡面有什麼

```
Assets/NavigationCore/
├── Scripts/          13 個執行期腳本
├── Editor/           4 個編輯器工具
└── Art/
    ├── Fonts/        中文字型（TMP）
    ├── Icons/        轉彎圖示、UI 底板
    └── Materials/    導航線材質
```

### 執行期腳本

| 腳本 | 功能 |
|---|---|
| `NavigationLineManager` | 路線資料、路口推進判定、剩餘距離計算 |
| `NavigationUIManager` | 導航卡片：轉彎圖示、路名、距離 |
| `ETADisplayUI` | 剩餘距離／預估時間 |
| `SpeedometerUI` | 速度表 |
| `MapLabelBillboard` | 地圖標籤永遠面向攝影機 |
| `GoogleMapCamera` | 第三人稱跟車攝影機、右鍵自由視角 |
| `VehiclePhysicsController` | 手動駕駛（WheelCollider 四驅） |
| `AutoDriveController` | 自動沿路線行進 |
| `DriveModeSwitcher` | 自動／手動切換 |
| `VehicleTerrainSnap` | 開場貼地、行進中防止陷入地形 |
| `ObjectTerrainDrape` | 把場景物件貼合到地形表面 |
| `VehicleBodyRide` | 車身隨懸吊起伏 |
| `VehicleSwitcher` | 多車種切換 |

### 刻意排除的東西

| 排除的 | 原因 |
|---|---|
| `MapStyleSwitcher` | 切換我們那兩種地圖風格（照片級／簡化），對你的地圖沒意義；而且它依賴 URP 的 Volume |
| `VectorLayerDrape`、`VectorLayerSurfaceOffset` | 專門處理我們用 OSM 資料生成的向量圖層 |
| `TempVehicleMover` | 早期測試用的方塊移動腳本，原作者已註明可刪 |
| Cesium 相關的 11 個編輯器方法 | 圖磚串流、ion 資產、地理座標換算，全部綁定 Cesium |
| 後處理設定 | 依賴 URP 的 `UniversalAdditionalCameraData`，拿掉才能不綁算繪管線 |

排除的部分**沒有留下任何編譯期依賴** —— 原本剩下的腳本只在「註解」裡提到它們。

---

## 已知問題

**這些是原專案就存在的問題，不是搬移造成的。**

### 1. 自動駕駛的實際速度只有設定值的四分之一

`AutoDriveController.moveSpeed` 設 8 m/s，但實測跑完 963 公尺花了 511 秒，
平均 **1.9 m/s**（兩次不同長度的測試都是同一個比例，約 23.8%）。
路線會正常跑完，只是比預期慢。**原因尚未查明。**

### 2. 手動駕駛不穩定

改善過（四驅、降扭力、修質心、防打轉），但在照片級地圖上，攝影測量會把路緣、電線桿、
路邊機車都烘進同一張網格，對物理引擎來說是滿地隱形障礙。**建議展示時用自動駕駛。**

如果你的地圖是乾淨的平滑路面，狀況可能會比原專案好很多 —— 這點沒有實測過。

### 3. 車輛模型的輪圈被著色成球

`VehicleModelBuilder` 生成的輪圈共用頂點，`RecalculateNormals` 把 90 度折邊平均掉了。
其他部位已經改成「每個面獨立頂點」，輪圈還沒修。

### 4. 中文字型的原始位置會導致打包後文字消失（本包已修正）

原專案把字型放在 `Editor/` 資料夾底下，被場景引用了 118 次 —— Unity 打包 exe 時會把
`Editor/` 底下的資產整個剝除，Play 模式正常但出包後中文全變空白。
**本包已經把字型與圖示移到 `Art/` 底下**，不要再把它們搬回 `Editor/`。

---

## 授權與資料來源

- 程式碼：世新大學畢業專題「沉浸式導航地圖平台」
- 中文字型：見 `Art/Fonts/` 內附的字型授權
- 這包**不含**任何 API 金鑰、Cesium ion token 或 OpenStreetMap 資料
