using UnityEngine;
using Valve.VR.InteractionSystem;

/// <summary>
/// 投げられる完成済み寿司オブジェクト。客に命中すると注文判定を行い、スコアへ反映する。
/// </summary>
[RequireComponent(typeof(Interactable))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(VelocityEstimator))]
public class SushiThrowable : MonoBehaviour
{
    [Header("Sushi Settings")]
    [Tooltip("この寿司の種類（SushiTypeコンポーネントがあればそちらを優先）")]
    public string sushiType = "Maguro";

    [Header("Throw Settings")]
    [Tooltip("投げる力の倍率")]
    public float throwMultiplier = 1.5f;

    [Tooltip("投げた後に自動的に消えるまでの時間（秒）")]
    public float autoDestroyTime = 10f;

    [Header("Effects")]
    [Tooltip("お客さんに当たったときのエフェクト")]
    public GameObject hitEffect;

    [Tooltip("お客さんに当たったときの効果音")]
    public AudioClip hitSound;

    [Tooltip("間違った寿司が当たったときのエフェクト")]
    public GameObject wrongHitEffect;

    [Tooltip("間違った寿司が当たったときの効果音")]
    public AudioClip wrongHitSound;

    [Tooltip("客以外（床や壁）に当たったときの効果音")]
    public AudioClip splatSound;

    [Tooltip("投げたときの効果音")]
    public AudioClip throwSound;

    private Interactable interactable;
    private Rigidbody rb;
    private VelocityEstimator velocityEstimator;
    private bool hasBeenThrown = false;
    private bool hasHitTarget = false;
    private float throwTime = 0f;
    private bool hasSplatted = false;

    void Awake()
    {
        interactable = GetComponent<Interactable>();
        rb = GetComponent<Rigidbody>();
        velocityEstimator = GetComponent<VelocityEstimator>();

        SushiType sushiTypeComponent = GetComponent<SushiType>();
        if (sushiTypeComponent != null)
        {
            // SushiTypeはSushiKind(enum)を持つため、既存のstring APIに合わせて名前で受け渡す
            sushiType = sushiTypeComponent.GetSushiType().ToString();
        }

        interactable.onAttachedToHand += OnAttachedToHand;
        interactable.onDetachedFromHand += OnDetachedFromHand;

        if (rb != null)
        {
            rb.maxAngularVelocity = 50.0f;
        }
    }

    void OnDestroy()
    {
        if (interactable != null)
        {
            interactable.onAttachedToHand -= OnAttachedToHand;
            interactable.onDetachedFromHand -= OnDetachedFromHand;
        }
    }

    void Update()
    {
        if (!hasBeenThrown) return;

        throwTime += Time.deltaTime;
        if (throwTime >= autoDestroyTime)
        {
            Destroy(gameObject);
        }
    }

    private void OnAttachedToHand(Hand hand)
    {
        hasBeenThrown = false;
        hasSplatted = false;
        throwTime = 0f;

        if (velocityEstimator != null)
        {
            velocityEstimator.BeginEstimatingVelocity();
        }
    }

    private void OnDetachedFromHand(Hand hand)
    {
        if (velocityEstimator != null)
        {
            velocityEstimator.FinishEstimatingVelocity();
        }

        ThrowObject(hand);
    }

    private void ThrowObject(Hand hand)
    {
        hasBeenThrown = true;
        throwTime = 0f;

        if (velocityEstimator != null && rb != null)
        {
            Vector3 velocity = velocityEstimator.GetVelocityEstimate() * throwMultiplier;
            Vector3 angularVelocity = velocityEstimator.GetAngularVelocityEstimate();

            rb.velocity = velocity;
            rb.angularVelocity = angularVelocity;
        }

        if (throwSound != null)
        {
            AudioSource.PlayClipAtPoint(throwSound, transform.position);
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!hasBeenThrown || hasHitTarget) return;

        // 客のコライダーが子オブジェクトに付いている場合もあるため、親も含めて検索する
        CustomerOrderWithTimer customer = collision.gameObject.GetComponentInParent<CustomerOrderWithTimer>();

        // 稀にcontactsが空のままイベントが来ることがあるため、自身の位置でフォールバックする
        Vector3 hitPoint = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;

        if (customer != null)
        {
            TryDeliverTo(customer, hitPoint);
        }
        else if (collision.gameObject.CompareTag("flooring") && !hasSplatted)
        {
            hasSplatted = true; // 床への着地音は1回のみ再生
            PlaySplatSound(hitPoint);
        }
    }

    private void PlaySplatSound(Vector3 position)
    {
        if (splatSound != null)
        {
            AudioSource.PlayClipAtPoint(splatSound, position);
        }
    }

    /// <summary>
    /// 客への受け渡しを試みる唯一の入口。寿司側の衝突経路と客側のトリガー経路の
    /// どちらから呼ばれても、1個の寿司が注文判定に使われるのは1回だけになる。
    /// </summary>
    public bool TryDeliverTo(CustomerOrderWithTimer customer, Vector3 hitPoint)
    {
        // 手に持ったまま接触した場合など、投げていない寿司は受け付けない。
        // hasHitTargetの先勝ちチェックで、複数コライダーや隣接する客との
        // 同一フレーム内の多重接触による二重判定を防ぐ。
        if (!hasBeenThrown || hasHitTarget || customer == null) return false;

        // 正誤判定は注文を管理する客側に委ねる。受付拒否（注文非アクティブ）の場合は
        // 誤答演出を出さず、寿司も消費しない（隣の客に当たり直す余地を残し、
        // 残った寿司はautoDestroyTimeで自然消滅する）。
        if (!customer.TryReceiveSushi(sushiType, out bool isCorrect)) return false;

        // 受付が成立した時点で消費済み扱いにし、以降の接触では判定しない
        hasHitTarget = true;

        PlayHitEffects(hitPoint, isCorrect);
        Destroy(gameObject, 0.1f);
        return true;
    }

    private void PlayHitEffects(Vector3 position, bool isCorrect)
    {
        if (isCorrect)
        {
            if (hitEffect != null)
            {
                GameObject effect = Instantiate(hitEffect, position, Quaternion.identity);
                Destroy(effect, 3f);
            }
            if (hitSound != null)
            {
                AudioSource.PlayClipAtPoint(hitSound, position);
            }
        }
        else
        {
            if (wrongHitEffect != null)
            {
                GameObject effect = Instantiate(wrongHitEffect, position, Quaternion.identity);
                Destroy(effect, 3f);
            }
            if (wrongHitSound != null)
            {
                AudioSource.PlayClipAtPoint(wrongHitSound, position);
            }
        }
    }

    public bool HasBeenThrown()
    {
        return hasBeenThrown;
    }

    public string GetSushiType()
    {
        return sushiType;
    }
}
