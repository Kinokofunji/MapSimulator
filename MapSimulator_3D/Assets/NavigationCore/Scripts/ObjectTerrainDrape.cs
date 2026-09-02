using System.Collections;
using UnityEngine;

namespace Navigation
{
    /// <summary>
    /// 把每個子物件整體移動到地形表面上（移動 Transform，不動網格頂點）。
    ///
    /// 樹木不能用 VectorLayerDrape 那種「逐頂點貼地」的做法——那會把樹幹頂端與樹冠尖端
    /// 全部壓到地面高度，整棵樹會變成貼在地上的一張紙。有高度的物件必須整棵一起平移。
    ///
    /// 跟 VectorLayerDrape 一樣採用多輪重試：Cesium 只對相機附近的圖磚生成碰撞網格，
    /// 遠處的樹要等車開過去才貼得上。
    /// </summary>
    [DisallowMultipleComponent]
    public class ObjectTerrainDrape : MonoBehaviour
    {
        [Tooltip("物件底部相對地面的偏移（公尺）。負值可以讓樹根稍微埋進地面，避免看到懸空的底面")]
        [SerializeField] private float surfaceOffset = -0.15f;

        [Tooltip("射線起點在物件上方多少公尺")]
        [SerializeField] private float probeHeight = 400f;

        [Tooltip("射線長度（公尺）")]
        [SerializeField] private float probeDistance = 1200f;

        [Tooltip("每影格處理幾個物件")]
        [SerializeField] private int objectsPerFrame = 200;

        [Tooltip("開場先等幾秒再開始")]
        [SerializeField] private float startDelaySeconds = 3f;

        [Tooltip("每一輪重試之間間隔幾秒")]
        [SerializeField] private float retryIntervalSeconds = 2.5f;

        [Tooltip("最多重試幾輪")]
        [SerializeField] private int maxRetryRounds = 200;

        [Tooltip("這個名稱的物件會被射線忽略（防墜用的隱形地板）")]
        [SerializeField] private string safetyFloorName = "NavigationSafetyFloor";

        private void Start()
        {
            StartCoroutine(DrapeChildren());
        }

        private IEnumerator DrapeChildren()
        {
            yield return new WaitForSeconds(startDelaySeconds);

            int count = transform.childCount;
            Transform[] children = new Transform[count];
            bool[] draped = new bool[count];

            for (int i = 0; i < count; i++)
            {
                children[i] = transform.GetChild(i);
            }

            int totalDraped = 0;
            int processedThisFrame = 0;

            for (int round = 0; round < maxRetryRounds; round++)
            {
                int drapedThisRound = 0;

                for (int i = 0; i < count; i++)
                {
                    if (draped[i])
                    {
                        continue;
                    }

                    if (TryFindGroundY(children[i].position, out float groundY))
                    {
                        Vector3 position = children[i].position;
                        position.y = groundY + surfaceOffset;
                        children[i].position = position;
                        draped[i] = true;
                        drapedThisRound++;
                        totalDraped++;
                    }

                    if (++processedThisFrame >= objectsPerFrame)
                    {
                        processedThisFrame = 0;
                        yield return null;
                    }
                }

                if (totalDraped >= count)
                {
                    Debug.Log($"[ObjectTerrainDrape] 「{name}」全部貼地完成：{totalDraped}/{count}。");
                    yield break;
                }

                if (drapedThisRound > 0)
                {
                    Debug.Log($"[ObjectTerrainDrape] 「{name}」第 {round + 1} 輪：累計 {totalDraped}/{count}。");
                }

                yield return new WaitForSeconds(retryIntervalSeconds);
            }

            Debug.Log($"[ObjectTerrainDrape] 「{name}」重試結束：{totalDraped}/{count} 已貼地。");
        }

        private bool TryFindGroundY(Vector3 worldPosition, out float groundY)
        {
            groundY = 0f;

            Vector3 origin = worldPosition + Vector3.up * probeHeight;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, probeDistance, ~0, QueryTriggerInteraction.Ignore);

            bool found = false;
            float highest = float.MinValue;

            foreach (RaycastHit hit in hits)
            {
                if (hit.transform.name.Trim() == safetyFloorName)
                {
                    continue;
                }

                // 不要打到樹自己，也不要打到別棵樹。
                if (hit.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (hit.point.y > highest)
                {
                    highest = hit.point.y;
                    found = true;
                }
            }

            if (found)
            {
                groundY = highest;
            }

            return found;
        }
    }
}
