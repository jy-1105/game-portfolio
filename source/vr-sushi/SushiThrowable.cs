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
    [Tooltip("投擲時の速度にかける倍率")]
    public float throwMultiplier = 1.5f;

    [Tooltip("投げた後に自動的に消えるまでの時間（秒）")]
    public float autoDestroyTime = 10f;

    [Header("Effects")]
    [Tooltip("注文と一致する寿司が客に当たったときのエフェクト")]
    public GameObject hitEffect;

    [Tooltip("注文と一致する寿司が客に当たったときの効果音")]
    public AudioClip hitSound;

    [Tooltip("間違った寿司が当たったときのエフェクト")]
    public GameObject wrongHitEffect;

    [Tooltip("間違った寿司が当たったときの効果音")]
    public AudioClip wrongHitSound;

    [Tooltip("客以外のflooringタグ付きオブジェクトに当たったときの効果音")]
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

        // 接触点を取得できない場合は、自身の位置を使う
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
    /// 衝突・トリガーの両方から呼び出す、寿司の受け渡し処理。
    /// 受け渡し済みの寿司は判定しない。
    /// </summary>
    public bool TryDeliverTo(CustomerOrderWithTimer customer, Vector3 hitPoint)
    {
        // 投げていない寿司や受け渡し済みの寿司は判定しない。
        if (!hasBeenThrown || hasHitTarget || customer == null) return false;

        // 正誤判定はCustomerOrderWithTimerが行う。
        // 注文を受け付けない客には演出を出さず、寿司も消費しない。
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
