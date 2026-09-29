using System.Collections;
using UnityEngine;

/// <summary>
/// 客が着席してから退店するまでの注文フローを管理する。
/// 制限時間内に指定された寿司を受け取れなかった場合はタイムアウト扱いとなる。
/// UI表示はCustomerOrderViewに委譲し、このクラスは注文の進行・判定・スコア反映に専念する。
/// </summary>
public class CustomerOrderWithTimer : MonoBehaviour
{
    [Header("注文設定")]
    [Tooltip("客が注文しうる寿司の一覧。種類・アイコン・ボイスをまとめて登録する")]
    [SerializeField] private SushiOrderData[] possibleOrders;
    [Tooltip("1注文あたりの制限時間（秒）")]
    [SerializeField] private float timeLimit = 45f;
    [Tooltip("正解・タイムアウト後、次の注文が始まるまでの待機時間（秒）")]
    [SerializeField] private float nextOrderDelay = 1.0f;

    [Header("UI（頭上のキャンバス）")]
    [Tooltip("注文テキスト・アイコン・タイムゲージの表示を担当するコンポーネント")]
    [SerializeField] private CustomerOrderView orderView;

    [Header("リアクション演出")]
    [SerializeField] private GameObject correctEffect;
    [SerializeField] private GameObject wrongEffect;
    [SerializeField] private AudioClip[] correctSounds;
    [SerializeField] private AudioClip[] wrongSounds;
    [SerializeField] private AudioClip[] timeoutSounds;
    [SerializeField] private Animator animator;
    [SerializeField] private string correctTrigger = "Happy";
    [SerializeField] private string wrongTrigger = "Sad";
    [Tooltip("エフェクトを生成する位置（客のtransform.positionからのオフセット）")]
    [SerializeField] private Vector3 effectSpawnOffset = new Vector3(0f, 2f, 0f);
    [Tooltip("生成したエフェクトを自動破棄するまでの時間（秒）")]
    [SerializeField] private float effectLifetime = 2f;
    [Tooltip("設定するとこのAudioSourceから再生する（未設定の場合はAudioSource.PlayClipAtPointにフォールバック）")]
    [SerializeField] private AudioSource audioSource;

    [Header("スコア設定")]
    [Tooltip("正解時の獲得スコア")]
    [SerializeField] private int correctScore = 100;
    [Tooltip("不正解時の減点スコア（内部で正の値として扱う）")]
    [SerializeField] private int wrongScore = 10;
    [Tooltip("タイムアウト時の減点スコア（内部で正の値として扱う）")]
    [SerializeField] private int timeoutScore = 10;

    private SushiOrderData currentOrder;
    private float remainingTime;
    private bool isOrderActive;
    private bool isWaitingForNextOrder;
    private Coroutine nextOrderRoutine;

    private void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    private void OnValidate()
    {
        ValidateOrderData();

        if (orderView == null)
            Debug.LogWarning($"{name}: orderViewが未設定です。注文UIは表示されません。", this);

        if (timeLimit <= 0f)
        {
            Debug.LogWarning($"{name}: timeLimitは0より大きい値にしてください。1に補正します。", this);
            timeLimit = 1f;
        }

        if (nextOrderDelay < 0f)
        {
            Debug.LogWarning($"{name}: nextOrderDelayは0以上にしてください。0に補正します。", this);
            nextOrderDelay = 0f;
        }

        if (correctScore < 0) correctScore = Mathf.Abs(correctScore);
        if (wrongScore < 0) wrongScore = Mathf.Abs(wrongScore);
        if (timeoutScore < 0) timeoutScore = Mathf.Abs(timeoutScore);
    }

    private void ValidateOrderData()
    {
        if (possibleOrders == null || possibleOrders.Length == 0)
        {
            Debug.LogError($"{name}: possibleOrdersが未設定です。注文を開始できません。", this);
            return;
        }

        for (int i = 0; i < possibleOrders.Length; i++)
        {
            SushiOrderData order = possibleOrders[i];
            if (order == null)
            {
                Debug.LogError($"{name}: possibleOrders[{i}]がnullです。", this);
                continue;
            }

            if (order.icon == null)
                Debug.LogWarning($"{name}: possibleOrders[{i}]（{order.kind}）にiconが設定されていません。テキスト表示にフォールバックします。", this);

            if (order.voiceClip == null)
                Debug.LogWarning($"{name}: possibleOrders[{i}]（{order.kind}）にvoiceClipが設定されていません。", this);
        }
    }

    public void ActivateOrder()
    {
        if (isOrderActive || isWaitingForNextOrder) return;
        StartNewOrder();
    }

    private void Update()
    {
        if (!isOrderActive) return;

        TickTimer(Time.deltaTime);
    }

    private void OnDisable()
    {
        CancelNextOrderRoutine();
    }

    private void OnDestroy()
    {
        CancelNextOrderRoutine();
    }

    // ---- 外部（SushiThrowableなど）から呼び出されるAPI ----

    /// <summary>指定した寿司が現在の注文と一致するかを判定する。</summary>
    public bool WantsSushi(string sushiTypeName)
    {
        if (!isOrderActive || currentOrder == null) return false;
        if (!System.Enum.TryParse(sushiTypeName, out SushiKind kind)) return false;
        return currentOrder.kind == kind;
    }

    /// <summary>
    /// 寿司の受け渡しを試みる。正誤の判定は注文を管理するこのクラス側で行う。
    /// 戻り値がfalseの場合は注文が非アクティブで受付自体が拒否されており、
    /// 正誤処理・スコア反映は一切行われていない。呼び出し側は誤答演出をしないこと。
    /// </summary>
    public bool TryReceiveSushi(string sushiTypeName, out bool isCorrect)
    {
        isCorrect = false;

        // 次の注文までの待機中などは受付そのものを拒否する。
        // ここで誤答扱いにすると、注文がない間の命中が理不尽な減点になるため。
        if (!isOrderActive || currentOrder == null) return false;

        // 外部から渡される判定値には頼らず、必ず現在の注文と照合して判定する
        isCorrect = WantsSushi(sushiTypeName);

        if (isCorrect) HandleCorrectSushi();
        else HandleWrongSushi();

        return true;
    }

    /// <summary>
    /// 旧API互換用。isCorrect引数は互換のために残しているが、
    /// 外部判定値をそのまま信用する事故を防ぐため、内部で再判定する。
    /// </summary>
    public void ReceiveSushi(string sushiTypeName, bool isCorrect)
    {
        TryReceiveSushi(sushiTypeName, out _);
    }

    // ---- 注文の開始・終了 ----

    private void StartNewOrder()
    {
        SushiOrderData order = SelectRandomOrder();
        if (order == null) return;

        BeginOrder(order);
    }

    private SushiOrderData SelectRandomOrder()
    {
        if (possibleOrders == null || possibleOrders.Length == 0)
        {
            Debug.LogError($"{name}: possibleOrdersが空のため注文を開始できません。", this);
            return null;
        }

        int index = Random.Range(0, possibleOrders.Length);
        SushiOrderData order = possibleOrders[index];

        if (order == null)
        {
            Debug.LogError($"{name}: possibleOrders[{index}]がnullのため注文を開始できません。", this);
            return null;
        }

        return order;
    }

    private void BeginOrder(SushiOrderData order)
    {
        currentOrder = order;
        remainingTime = timeLimit;
        isOrderActive = true;

        if (orderView != null)
        {
            orderView.ShowOrder(order);
            orderView.UpdateRemainingTime(remainingTime, timeLimit);
        }

        PlayClip(order.voiceClip);
    }

    private void EndOrder()
    {
        isOrderActive = false;
        if (orderView != null) orderView.HideOrder();
    }

    // ---- タイマー進行 ----

    private void TickTimer(float deltaTime)
    {
        remainingTime -= deltaTime;

        if (remainingTime <= 0f)
        {
            remainingTime = 0f;
            UpdateTimerDisplay();
            HandleTimeout();
            return;
        }

        UpdateTimerDisplay();
    }

    private void UpdateTimerDisplay()
    {
        if (orderView != null)
            orderView.UpdateRemainingTime(remainingTime, timeLimit);
    }

    // ---- 寿司との接触判定（トリガー方式） ----
    // 現状のプレハブは客・寿司ともコライダーが非トリガーのため、実際の判定は
    // SushiThrowable.OnCollisionEnter側の経路で行われる。
    // 客側コライダーをトリガーに変更した構成でも動くようこちらの経路も残すが、
    // 受付・寿司の消費・演出はすべてSushiThrowable.TryDeliverToに集約し、
    // 両経路が同時に成立しても1個の寿司が二重に判定されないようにしている。

    private void OnTriggerEnter(Collider other)
    {
        // 完成寿司（SushiThrowable）以外、たとえばネタ単体などは受け付けない
        SushiThrowable sushi = other.GetComponentInParent<SushiThrowable>();
        if (sushi == null) return;

        sushi.TryDeliverTo(this, other.transform.position);
    }

    // ---- 結果処理 ----

    private void HandleCorrectSushi()
    {
        if (!isOrderActive) return;

        float duration = timeLimit - remainingTime;
        EndOrder();

        PlayReaction(correctEffect, correctSounds, correctTrigger);
        ApplyCorrectResult(duration);
        ScheduleNextOrder();
    }

    private void HandleWrongSushi()
    {
        if (!isOrderActive) return;

        // 誤答時は注文を打ち切らず、同じ注文のままタイマーを継続する
        PlayReaction(wrongEffect, wrongSounds, wrongTrigger);
        ApplyWrongResult();
    }

    private void HandleTimeout()
    {
        if (!isOrderActive) return;

        EndOrder();

        PlayReaction(wrongEffect, timeoutSounds, wrongTrigger);
        ApplyTimeoutResult();
        ScheduleNextOrder();
    }

    private void ApplyCorrectResult(float duration)
    {
        if (ScoreManager.Instance == null) return;
        ScoreManager.Instance.RegisterCorrectOrder(correctScore, duration);
    }

    private void ApplyWrongResult()
    {
        if (ScoreManager.Instance == null) return;
        ScoreManager.Instance.RegisterWrongOrder(wrongScore);
    }

    private void ApplyTimeoutResult()
    {
        if (ScoreManager.Instance == null) return;
        ScoreManager.Instance.RegisterTimeout(timeoutScore);
    }

    // ---- 次の注文への遷移（Coroutine多重起動防止） ----

    private void ScheduleNextOrder()
    {
        if (isWaitingForNextOrder) return;

        isWaitingForNextOrder = true;
        nextOrderRoutine = StartCoroutine(StartNextOrderAfterDelay());
    }

    private IEnumerator StartNextOrderAfterDelay()
    {
        yield return new WaitForSeconds(nextOrderDelay);

        isWaitingForNextOrder = false;
        nextOrderRoutine = null;
        StartNewOrder();
    }

    private void CancelNextOrderRoutine()
    {
        if (nextOrderRoutine != null)
        {
            StopCoroutine(nextOrderRoutine);
            nextOrderRoutine = null;
        }
        isWaitingForNextOrder = false;
    }

    // ---- リアクション演出（正解・誤答・タイムアウト共通処理） ----

    private void PlayReaction(GameObject effectPrefab, AudioClip[] sounds, string animatorTrigger)
    {
        SpawnEffect(effectPrefab);
        PlayRandomSound(sounds);
        PlayAnimatorTrigger(animatorTrigger);
    }

    private void SpawnEffect(GameObject effectPrefab)
    {
        if (effectPrefab == null) return;

        GameObject fx = Instantiate(effectPrefab, GetEffectSpawnPosition(), Quaternion.identity);
        Destroy(fx, effectLifetime);
    }

    private Vector3 GetEffectSpawnPosition()
    {
        return transform.position + effectSpawnOffset;
    }

    private void PlayRandomSound(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return;
        PlayClip(clips[Random.Range(0, clips.Length)]);
    }

    private void PlayAnimatorTrigger(string trigger)
    {
        if (animator != null && !string.IsNullOrEmpty(trigger))
            animator.SetTrigger(trigger);
    }

    private void PlayClip(AudioClip clip)
    {
        if (clip == null) return;

        if (audioSource != null)
            audioSource.PlayOneShot(clip);
        else
            AudioSource.PlayClipAtPoint(clip, transform.position);
    }
}
