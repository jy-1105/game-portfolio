using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 一定間隔で客プレハブを生成し、空いている席へ向かわせる。
/// </summary>
public class CustomerSpawner : MonoBehaviour
{
    [Header("客のプレハブ（複数）")]
    [Tooltip("登録順にスポーンする")]
    public GameObject[] customerPrefabs;

    [Header("生成時の効果音")]
    public AudioClip spawnSound;

    [Header("客のスポーン位置（入口など）")]
    public Transform spawnPoint;

    [Header("椅子 SeatPoint 一覧")]
    public SeatPoint[] seats;

    [Header("客の生成間隔（秒）")]
    public float spawnInterval = 30f;

    // 次に生成するプレハブのインデックス
    private int nextPrefabIndex = 0;

    private void Start()
    {
        StartCoroutine(SpawnLoop());
    }

    private IEnumerator SpawnLoop()
    {
        while (true)
        {
            TrySpawnCustomer();
            yield return new WaitForSeconds(spawnInterval);
        }
    }

    /// <summary>
    /// 空席があれば客を1体生成し、対象の席へ向かわせる。
    /// </summary>
    private void TrySpawnCustomer()
    {
        if (spawnPoint == null)
        {
            Debug.LogError("[CustomerSpawner] spawnPoint が未設定です。", this);
            return;
        }

        if (customerPrefabs == null || customerPrefabs.Length == 0)
        {
            Debug.LogError("[CustomerSpawner] customerPrefabs が空です。", this);
            return;
        }

        SeatPoint freeSeat = GetFreeSeat();
        if (freeSeat == null) return; // 空席なし

        GameObject prefab = GetNextCustomerPrefabInOrder();
        if (prefab == null)
        {
            Debug.LogError("[CustomerSpawner] 有効なプレハブがありません。", this);
            return;
        }

        // spawnPoint周辺のNavMesh上に位置を補正してから生成する
        if (!NavMesh.SamplePosition(spawnPoint.position, out NavMeshHit hit, 20.0f, NavMesh.AllAreas))
        {
            Debug.LogError($"[CustomerSpawner] spawnPoint 付近にNavMeshが見つかりません: {spawnPoint.position}", this);
            return;
        }

        Vector3 spawnPos = hit.position;
        GameObject obj = Instantiate(prefab, spawnPos, spawnPoint.rotation);

        if (spawnSound != null)
        {
            AudioSource.PlayClipAtPoint(spawnSound, spawnPos);
        }

        // CustomerSittingがない場合は、警告を出して客を破棄する。
        CustomerSitting customer = obj.GetComponent<CustomerSitting>();
        if (customer == null)
        {
            Debug.LogWarning("[CustomerSpawner] 生成したプレハブに CustomerSitting がアタッチされていません。", obj);
            Destroy(obj);
            return;
        }

        NavMeshAgent agent = obj.GetComponent<NavMeshAgent>();
        if (agent == null)
        {
            Debug.LogError("[CustomerSpawner] 生成したプレハブに NavMeshAgent がありません。", obj);
            Destroy(obj);
            return;
        }

        agent.Warp(spawnPos);

        if (!agent.isOnNavMesh)
        {
            Debug.LogError("[CustomerSpawner] 生成した客がNavMesh上に乗っていません。", obj);
            Destroy(obj);
            return;
        }

        // 移動先の設定に失敗した客を破棄する。
        if (!customer.GoToSeat(freeSeat))
        {
            Destroy(obj);
        }
    }

    /// <summary>
    /// customerPrefabsを先頭から順に返す。null要素はスキップし、末尾に達したら先頭に戻る。
    /// </summary>
    private GameObject GetNextCustomerPrefabInOrder()
    {
        if (customerPrefabs == null || customerPrefabs.Length == 0) return null;

        int checkedCount = 0;
        while (checkedCount < customerPrefabs.Length)
        {
            GameObject prefab = customerPrefabs[nextPrefabIndex];
            nextPrefabIndex = (nextPrefabIndex + 1) % customerPrefabs.Length;

            if (prefab != null) return prefab;
            checkedCount++;
        }

        return null; // 全要素がnull
    }

    /// <summary>
    /// 空席を1つ返す。見つからない場合はnullを返す。
    /// </summary>
    private SeatPoint GetFreeSeat()
    {
        if (seats == null) return null;

        foreach (var seat in seats)
        {
            if (seat != null && !seat.isOccupied)
                return seat;
        }
        return null;
    }
}
