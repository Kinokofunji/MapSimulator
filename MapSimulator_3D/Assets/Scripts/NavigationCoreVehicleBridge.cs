using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 把我們自己的 NavigationLineManager（RoadGridPathfinder / RouteChoiceManager 規劃出來的路線）
/// 同步進 NavigationCore 套件的 Navigation.NavigationLineManager，讓 Navigation.AutoDriveController
/// 可以照著使用者在小地圖/全景地圖上選的同一條路線自動駕駛。
///
/// 兩邊是完全獨立、彼此不相容的資料結構——我們是 List&lt;NavWaypoint&gt;，NavigationCore 是
/// List&lt;Vector3&gt; + List&lt;WaypointInfo&gt; 兩個平行陣列——所以用這個橋接元件轉換，
/// 而不是想辦法共用同一份資料。
///
/// 這個檔案刻意不寫 using Navigation;：NavigationCore 套件自己的 TurnType 跟我們專案原本就有的
/// 全域 TurnType 同名，兩個 using 一起出現會讓編譯器對每一個沒加命名空間前綴的 TurnType 都報
/// 「ambiguous reference」，所以全部用完整路徑 Navigation.xxx 明確指定要哪一邊的型別。
/// </summary>
public class NavigationCoreVehicleBridge : MonoBehaviour
{
    [Tooltip("我們自己的路線資料來源：RoadGridPathfinder / RouteChoiceManager 選好的路線會經過這裡")]
    public NavigationLineManager sourceLineManager;

    [Tooltip("NavigationCore 用來給 AutoDriveController 讀路線的元件（掛在一個獨立、不顯示線的物件上）")]
    public Navigation.NavigationLineManager targetLineManager;

    void OnEnable()
    {
        if (sourceLineManager != null)
        {
            sourceLineManager.OnRouteChanged += HandleRouteChanged;

            // 訂閱當下如果來源已經有路線了（例如場景重新啟用這個橋接元件時），先同步一次，
            // 不用等下一次 SetRoute() 才會有資料。
            if (sourceLineManager.waypoints != null && sourceLineManager.waypoints.Count > 0)
            {
                HandleRouteChanged(sourceLineManager.waypoints);
            }
        }
    }

    void OnDisable()
    {
        if (sourceLineManager != null)
        {
            sourceLineManager.OnRouteChanged -= HandleRouteChanged;
        }
    }

    private void HandleRouteChanged(List<NavWaypoint> newWaypoints)
    {
        if (targetLineManager == null) return;

        var positions = new List<Vector3>(newWaypoints.Count);
        var infos = new List<Navigation.WaypointInfo>(newWaypoints.Count);

        foreach (NavWaypoint wp in newWaypoints)
        {
            positions.Add(wp.position);
            infos.Add(new Navigation.WaypointInfo
            {
                turnType = ConvertTurnType(wp.turnType),
                roadName = wp.roadName
            });
        }

        targetLineManager.SetRoute(positions, infos);
    }

    private static Navigation.TurnType ConvertTurnType(TurnType ourType)
    {
        switch (ourType)
        {
            case TurnType.TurnLeft: return Navigation.TurnType.TurnLeft;
            case TurnType.TurnRight: return Navigation.TurnType.TurnRight;
            case TurnType.UTurn: return Navigation.TurnType.UTurn;
            case TurnType.Straight:
            default: return Navigation.TurnType.Straight;
        }
    }
}
