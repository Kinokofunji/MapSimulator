using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 掃描場景中的道路方磚 (SimplePoly City 的 Road 系列 Prefab)，
/// 依照它們的世界座標重建一份「網格道路圖」，並用 A* 在網格上規劃路線。
///
/// 專案裡沒有正式的道路節點圖 (Barmetler RoadSystem 套件雖然有匯入，但沒被用來蓋這座城市)，
/// 所以這裡改用一個實際觀察到的事實反推連通關係：這座城市的道路方磚是整齊排列在
/// 20 世界單位的網格上的。只要兩個「相鄰網格格子」都各自站著一塊道路方磚，就視為彼此相通。
///
/// 這個假設對絕大部分道路成立；只有在轉角/T字路口方磚剛好有一側是「封閉」的、
/// 但緊鄰的格子又剛好也站著別的道路方磚時，可能誤判出實際上不存在的捷徑。
/// 對於導航路線這種視覺參考用途來說，這個精確度已經足夠；如果之後要追求完全精準，
/// 需要改用真正理解每塊方磚朝向與開口方向的道路圖（例如把 Barmetler RoadSystem 真正接上）。
/// </summary>
public class RoadGridPathfinder : MonoBehaviour
{
    [Tooltip("道路方磚彼此之間的網格間距（世界單位）。這座城市目前量測出來是 20")]
    public float gridSize = 20f;

    [Tooltip("GameObject 名稱要符合以下開頭，才會被視為可通行的道路方磚")]
    public string roadNamePrefix = "Road ";

    [Tooltip("即使名稱符合上面的開頭，只要包含以下任一關鍵字就視為不可通行（人行道、標線、廣場地磚等）")]
    public List<string> excludeKeywords = new List<string> { "Sidewalk", "Concrete", "Split Line" };

    private readonly Dictionary<Vector2Int, float> roadCellHeights = new Dictionary<Vector2Int, float>();
    private bool isBuilt = false;

    private Vector3 boundsCenter;
    private float boundsHalfWidth;
    private float boundsHalfDepth;
    private bool hasBounds = false;

    private static readonly Vector2Int[] Neighbors4 =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1)
    };

    /// <summary>
    /// 掃描場景，建立道路網格圖。結果會快取，只需要在第一次用到時做一次（除非場景道路有變動再手動呼叫）。
    /// </summary>
    public void BuildGraph()
    {
        roadCellHeights.Clear();
        hasBounds = false;

        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;

        Transform[] all = FindObjectsOfType<Transform>();
        foreach (Transform t in all)
        {
            string n = t.name;
            if (!n.StartsWith(roadNamePrefix)) continue;

            bool excluded = false;
            foreach (string keyword in excludeKeywords)
            {
                if (n.Contains(keyword))
                {
                    excluded = true;
                    break;
                }
            }
            if (excluded) continue;

            Vector2Int cell = WorldToCell(t.position);
            if (!roadCellHeights.ContainsKey(cell))
            {
                roadCellHeights[cell] = t.position.y;
            }

            if (t.position.x < minX) minX = t.position.x;
            if (t.position.x > maxX) maxX = t.position.x;
            if (t.position.z < minZ) minZ = t.position.z;
            if (t.position.z > maxZ) maxZ = t.position.z;
        }

        if (roadCellHeights.Count > 0)
        {
            boundsCenter = new Vector3((minX + maxX) / 2f, 0f, (minZ + maxZ) / 2f);
            boundsHalfWidth = (maxX - minX) / 2f;
            boundsHalfDepth = (maxZ - minZ) / 2f;
            hasBounds = true;
        }

        isBuilt = true;
    }

    /// <summary>
    /// 取得掃描到的所有道路方磚的世界座標範圍中心點，以及涵蓋整個範圍所需要的正交攝影機大小
    /// （取寬/深較大的一邊，並留一點邊界）。場景裡完全沒有符合條件的道路方磚時回傳 false。
    /// </summary>
    public bool TryGetBoundsFitOrthographicSize(out Vector3 center, out float orthographicSize, float margin = 1.15f)
    {
        if (!isBuilt) BuildGraph();

        center = boundsCenter;
        orthographicSize = Mathf.Max(boundsHalfWidth, boundsHalfDepth, gridSize) * margin;
        return hasBounds;
    }

    private Vector2Int WorldToCell(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.RoundToInt(worldPos.x / gridSize),
            Mathf.RoundToInt(worldPos.z / gridSize));
    }

    private Vector3 CellToWorld(Vector2Int cell)
    {
        float y = 0f;
        roadCellHeights.TryGetValue(cell, out y);
        return new Vector3(cell.x * gridSize, y, cell.y * gridSize);
    }

    /// <summary>
    /// CellToWorld 的公開版本，供外部系統（例如 DestinationSearchController 想知道
    /// 「離某個地點最近的道路在哪裡」）使用，不需要自己重新兜一份格子座標轉換公式。
    /// </summary>
    public Vector3 GetCellWorldPosition(Vector2Int cell)
    {
        if (!isBuilt) BuildGraph();
        return CellToWorld(cell);
    }

    /// <summary>把任意世界座標吸附到最近的道路網格節點；場景附近完全沒有道路方磚才會回傳 false。</summary>
    public bool SnapToNearestRoadCell(Vector3 worldPos, out Vector2Int cell)
    {
        if (!isBuilt) BuildGraph();

        cell = WorldToCell(worldPos);
        if (roadCellHeights.ContainsKey(cell)) return true;

        // 附近沒有正好對齊的格子，一圈一圈往外搜尋最近的道路格子。同一圈裡如果有好幾個
        // 候選格子（例如剛好卡在兩個路口中間），原本的做法是取「迭代順序上第一個找到的」，
        // 不是真正距離最近的，實測就是這樣導致明明隔壁路口比較近，卻被導到差一個路口的
        // 地方。改成同一圈內比較所有候選格子的實際歐氏距離，取真正最近的那一個。
        Vector2Int originCell = cell;
        for (int radius = 1; radius <= 8; radius++)
        {
            bool foundAny = false;
            Vector2Int bestCandidate = default;
            float bestDistanceSqr = float.PositiveInfinity;

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != radius) continue;

                    Vector2Int candidate = new Vector2Int(originCell.x + dx, originCell.y + dz);
                    if (!roadCellHeights.ContainsKey(candidate)) continue;

                    Vector3 candidateWorld = CellToWorld(candidate);
                    float dxWorld = candidateWorld.x - worldPos.x;
                    float dzWorld = candidateWorld.z - worldPos.z;
                    float distanceSqr = dxWorld * dxWorld + dzWorld * dzWorld;

                    if (distanceSqr < bestDistanceSqr)
                    {
                        bestDistanceSqr = distanceSqr;
                        bestCandidate = candidate;
                        foundAny = true;
                    }
                }
            }

            if (foundAny)
            {
                cell = bestCandidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 用 A* 在道路網格上規劃一條路線。blockedEdges 可以傳入「不能走的邊」，
    /// 用來逼演算法繞路，藉此產生跟前一條路線不同的替代路線。
    /// </summary>
    public List<Vector3> FindPath(Vector2Int start, Vector2Int goal, HashSet<(Vector2Int, Vector2Int)> blockedEdges = null)
    {
        if (!isBuilt) BuildGraph();

        var openSet = new List<Vector2Int> { start };
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var gScore = new Dictionary<Vector2Int, float> { [start] = 0f };
        var fScore = new Dictionary<Vector2Int, float> { [start] = Heuristic(start, goal) };

        while (openSet.Count > 0)
        {
            Vector2Int current = openSet[0];
            float currentF = GetOrInfinity(fScore, current);
            foreach (Vector2Int node in openSet)
            {
                float f = GetOrInfinity(fScore, node);
                if (f < currentF)
                {
                    current = node;
                    currentF = f;
                }
            }

            if (current == goal)
            {
                return ReconstructPath(cameFrom, current);
            }

            openSet.Remove(current);
            float currentG = GetOrInfinity(gScore, current);

            foreach (Vector2Int dir in Neighbors4)
            {
                Vector2Int neighbor = current + dir;
                if (!roadCellHeights.ContainsKey(neighbor)) continue;

                if (blockedEdges != null &&
                    (blockedEdges.Contains((current, neighbor)) || blockedEdges.Contains((neighbor, current))))
                {
                    continue;
                }

                float tentativeG = currentG + gridSize;
                if (tentativeG < GetOrInfinity(gScore, neighbor))
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = tentativeG + Heuristic(neighbor, goal);
                    if (!openSet.Contains(neighbor)) openSet.Add(neighbor);
                }
            }
        }

        return null; // 找不到路
    }

    private static float GetOrInfinity(Dictionary<Vector2Int, float> dict, Vector2Int key)
    {
        return dict.TryGetValue(key, out float value) ? value : float.PositiveInfinity;
    }

    private float Heuristic(Vector2Int a, Vector2Int b)
    {
        return (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y)) * gridSize;
    }

    private List<Vector3> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current)
    {
        var path = new List<Vector3> { CellToWorld(current) };
        while (cameFrom.TryGetValue(current, out Vector2Int prev))
        {
            current = prev;
            path.Add(CellToWorld(current));
        }
        path.Reverse();
        return path;
    }

    /// <summary>
    /// 規劃最多 maxRoutes 條彼此明顯不同的候選路線：
    /// 第一條是真正的最短路線，之後每條都會刻意封鎖前一條路線中段的一小段道路，逼演算法改道，
    /// 藉此模擬「最快路線 / 替代路線」的選擇（不是嚴謹的 k-shortest-path 演算法，但足夠實用）。
    /// </summary>
    public List<List<Vector3>> FindMultipleRoutes(
        Vector3 startWorld, Vector3 endWorld, int maxRoutes = 3, Bounds? destinationBounds = null)
    {
        var routes = new List<List<Vector3>>();

        if (!SnapToNearestRoadCell(startWorld, out Vector2Int startCell)) return routes;
        if (!SnapToNearestRoadCell(endWorld, out Vector2Int endCell)) return routes;

        List<Vector3> primary = FindPath(startCell, endCell);
        if (primary == null) return routes;
        routes.Add(primary);

        var blockedEdges = new HashSet<(Vector2Int, Vector2Int)>();

        for (int i = 1; i < maxRoutes; i++)
        {
            List<Vector3> lastRoute = routes[routes.Count - 1];
            if (lastRoute.Count < 3) break;

            int midIndex = lastRoute.Count / 2;
            Vector2Int a = WorldToCell(lastRoute[Mathf.Max(0, midIndex - 1)]);
            Vector2Int b = WorldToCell(lastRoute[Mathf.Min(lastRoute.Count - 1, midIndex)]);
            blockedEdges.Add((a, b));

            List<Vector3> alt = FindPath(startCell, endCell, blockedEdges);
            if (alt == null || RoutesAreSimilar(alt, primary))
            {
                break;
            }

            routes.Add(alt);
        }

        // 每條路線目前的終點都是「離目的地最近的道路格子中心」，不是真正的目的地座標——
        // 格子是 20 公尺一格，兩棟距離很近、共用同一格路網的建築物（例如同一條街上相鄰的
        // 店面）會被導到同一個格子中心，變成「搜尋不同地點卻停在同一個位置」。
        // 這裡在所有替代路線都規劃完、判斷完彼此夠不夠不同之後（避免精確座標的微小差異
        // 干擾 RoutesAreSimilar 的比對），把每條路線的終點都換成真正的目的地座標，
        // 只補這一小段「最後停靠」的精準度，完全不動格子本身的道路規劃邏輯。
        foreach (List<Vector3> route in routes)
        {
            AppendPreciseDestinationIfReasonable(route, endWorld, destinationBounds);
        }

        return routes;
    }

    /// <summary>
    /// 把路線的終點從「格子中心」朝真正的目的地座標拉近一點，不會直接跳到精確座標——
    /// 精確座標可能落在建築物內部，如果直接把它當終點，車輛可能會直接貫穿整棟建築物
    /// 開過去。一律從「車輛實際會開到的最後一個路口」（保證是路網上的真實點，方向
    /// 一定正確，因為就是路徑規劃算出來的終點）朝精確座標移動。
    ///
    /// 有提供 destinationBounds（例如搜尋到的建築物）時，走到邊界框表面就停——這是
    /// 從保證正確的方向逼近，不管建築物離路網多遠、跟其他建築物是不是共用同一個
    /// 最近路網格子，都能精準停在正確的邊上，不會真的開進建築物內部，也不會因為
    /// 固定的距離上限太保守而停在半路、跟旁邊共用同一格路網的鄰居分不出來。
    /// 沒有提供邊界（例如點地圖選的任意座標，沒有對應的實體範圍）時，退回原本
    /// 「最多再靠近半個格子」的保守做法。
    /// </summary>
    private void AppendPreciseDestinationIfReasonable(List<Vector3> route, Vector3 preciseDestination, Bounds? destinationBounds)
    {
        if (route.Count == 0) return;

        Vector3 lastPoint = route[route.Count - 1];
        Vector3 flatLast = new Vector3(lastPoint.x, 0f, lastPoint.z);
        Vector3 flatPrecise = new Vector3(preciseDestination.x, 0f, preciseDestination.z);
        float distance = Vector3.Distance(flatLast, flatPrecise);

        if (distance <= 0.05f)
        {
            return; // 已經在同一個點，不需要調整
        }

        if (distance > gridSize * 1.5f)
        {
            // 路網終點離目的地太遠（隔了不只一個街廓），代表這個路網終點本來就不是
            // 「這棟建築物該從哪裡接近」的合理起點——不管有沒有邊界框，都不要硬拉一條
            // 長直線過去，那條直線很可能會貫穿沿途其他不相干的建築物。維持原本的格子
            // 中心，不做這段「最後停靠」的精準化。
            return;
        }

        Vector3 direction = (flatPrecise - flatLast) / distance; // 已知 distance > 0.05，安全地手動正規化

        float approachDistance = destinationBounds.HasValue
            ? Mathf.Clamp(DistanceToBoundsSurface(destinationBounds.Value, flatLast, direction), 0f, distance)
            : Mathf.Min(distance, gridSize * 0.5f);

        Vector3 finalFlatPoint = flatLast + direction * approachDistance;
        route.Add(new Vector3(finalFlatPoint.x, lastPoint.y, finalFlatPoint.z));
    }

    /// <summary>
    /// 從 rayOriginFlat（保證在 bounds 外面——是道路上的點，不會真的在建築物內部）
    /// 沿著 direction（水平面、已正規化）前進，走到碰到 bounds 表面為止的距離。
    /// 用「原點到中心的距離」減去「中心沿著同一個方向到邊界的半徑」來算：因為方向
    /// 就是指向目的地（邊界框中心）的方向，這條線本來就會通過中心，所以這個算法
    /// 是精確值，不是近似。
    /// </summary>
    private static float DistanceToBoundsSurface(Bounds bounds, Vector3 rayOriginFlat, Vector3 direction)
    {
        float distanceToCenter = Vector2.Distance(
            new Vector2(rayOriginFlat.x, rayOriginFlat.z), new Vector2(bounds.center.x, bounds.center.z));

        float alongX = Mathf.Abs(direction.x) > 0.0001f ? bounds.extents.x / Mathf.Abs(direction.x) : float.PositiveInfinity;
        float alongZ = Mathf.Abs(direction.z) > 0.0001f ? bounds.extents.z / Mathf.Abs(direction.z) : float.PositiveInfinity;
        float halfExtentAlongDirection = Mathf.Min(alongX, alongZ);

        return Mathf.Max(0f, distanceToCenter - halfExtentAlongDirection);
    }

    private bool RoutesAreSimilar(List<Vector3> a, List<Vector3> b)
    {
        int checkLength = Mathf.Min(a.Count, b.Count);
        if (checkLength == 0) return true;

        int sameCount = 0;
        for (int i = 0; i < checkLength; i++)
        {
            if (Vector3.Distance(a[i], b[i]) < 0.1f) sameCount++;
        }

        return (float)sameCount / checkLength > 0.8f;
    }
}
