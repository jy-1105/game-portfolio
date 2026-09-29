using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 客が指定された席まで移動し、着席するまでの挙動を制御する。
/// </summary>
public class CustomerSitting : MonoBehaviour
{
    [HideInInspector]
    public SeatPoint currentSeat;

    private SeatPoint targetSeat;
    private NavMeshAgent agent;
    private Animator anim;

    private bool isGoingToSeat = false;
    private bool isSitting = false;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        anim = GetComponentInChildren<Animator>(); // 自身または子オブジェクトのAnimatorを取得する

        if (anim == null)
        {
            Debug.LogError("CustomerSitting: Animator が見つかりません。", this);
        }
        else
        {
            anim.applyRootMotion = false;
        }

        if (agent != null)
        {
            agent.updateRotation = true;
        }
    }

    /// <summary>
    /// 指定した席を移動先に設定し、設定要求が成功したら席を予約してtrueを返す。
    /// 経路が席まで到達できるかどうかは、この時点では確認していない。
    /// </summary>
    public bool GoToSeat(SeatPoint seat)
    {
        if (seat == null) return false;

        if (agent == null)
        {
            Debug.LogError("CustomerSitting: NavMeshAgent がないため席へ移動できません。", this);
            return false;
        }

        agent.enabled = true;

        // NavMesh上にいない場合や移動先の設定要求に失敗した場合は、席を予約しない。
        if (!agent.isOnNavMesh || !agent.SetDestination(seat.transform.position))
        {
            Debug.LogError("CustomerSitting: 席への経路を設定できませんでした。", this);
            return false;
        }

        // 移動先の設定要求が成功したら席を予約する
        targetSeat = seat;
        targetSeat.isOccupied = true;

        isGoingToSeat = true;
        isSitting = false;
        agent.isStopped = false;

        if (anim != null)
        {
            anim.SetBool("IsWalking", true); // Animator Controller側のパラメータ名と一致させること
        }

        return true;
    }

    void Update()
    {
        if (!isGoingToSeat || isSitting || agent == null || !agent.enabled) return;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.05f)
        {
            ArriveAndSit();
        }
    }

    /// <summary>
    /// 席への到着を検知した際の着席処理。
    /// </summary>
    private void ArriveAndSit()
    {
        isGoingToSeat = false;
        isSitting = true;

        // 予約状態から着席状態へ移行する（席の参照は常にどちらか一方だけが持つ）
        currentSeat = targetSeat;
        targetSeat = null;

        if (agent != null)
        {
            agent.isStopped = true;
            agent.enabled = false; // 着席後は経路探索が不要なため無効化
        }

        // 位置と向きをSeatPointに合わせる
        transform.position = currentSeat.transform.position;
        transform.rotation = currentSeat.transform.rotation;

        if (anim != null)
        {
            anim.SetBool("IsWalking", false);
            anim.SetTrigger("Sit");
        }

        CustomerOrderWithTimer orderScript = GetComponent<CustomerOrderWithTimer>();
        if (orderScript != null)
        {
            orderScript.ActivateOrder();
        }
    }

    /// <summary>
    /// 客を退店させ、席を解放してオブジェクトを破棄する。
    /// </summary>
    public void Leave()
    {
        ReleaseSeat();
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // Leaveを経由せずDestroyされた場合（移動中の強制退店・シーン遷移など）でも
        // 予約席が使用中のまま残らないようにする
        ReleaseSeat();
    }

    /// <summary>
    /// 予約中（targetSeat）または着席中（currentSeat）の席を解放する。
    /// 解放と同時に参照をnullにするため、重複して呼ばれても安全であり、
    /// 解放後に他の客へ再割り当てされた席を誤って解放し直すこともない。
    /// </summary>
    private void ReleaseSeat()
    {
        if (currentSeat != null)
        {
            currentSeat.isOccupied = false;
            currentSeat = null;
        }

        if (targetSeat != null)
        {
            targetSeat.isOccupied = false;
            targetSeat = null;
        }
    }
}
