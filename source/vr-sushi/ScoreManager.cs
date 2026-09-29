using UnityEngine;
using TMPro;

/// <summary>
/// スコア管理システム（シングルトン）。得点の加減算、ハイスコアの保存、
/// リザルト用のリプレイフレーム記録、クレーマー滞在中の継続減点をまとめて扱う。
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [Header("Statistics")]
    public int servedCount = 0;
    public int wrongCount = 0;
    public int missedCount = 0;
    public float totalServiceTime = 0f;
    public float totalAngryTime = 0f;

    [Header("Score Settings")]
    [Tooltip("現在のスコア")]
    public int currentScore = 0;

    [Tooltip("ハイスコア")]
    public int highScore = 0;

    [Header("Score Logic")]
    [Tooltip("クレーマー1人あたりの毎秒減点数")]
    public float penaltyPerCustomerPerSecond = 10.0f;

    [Tooltip("一度に減点する単位（この値が溜まるまで減点処理・音再生を行わない）")]
    public float penaltyStep = 10.0f;

    [Header("UI References")]
    [Tooltip("スコア表示用のテキスト")]
    public TMP_Text scoreText;

    [Tooltip("ハイスコア表示用のテキスト")]
    public TMP_Text highScoreText;

    [Header("Replay Data")]
    [Tooltip("リザルトシーンへ受け渡すリプレイ用スクリーンショット")]
    public System.Collections.Generic.List<Texture2D> replayFrames = new System.Collections.Generic.List<Texture2D>();

    [Header("Recording Settings")]
    public float recordInterval = 0.5f;
    public int maxFrames = 50;

    [Header("Rank Thresholds")]
    public int rankS = 5000;
    public int rankA = 3000;
    public int rankB = 1000;
    public int rankC = 500;
    public int rankD = 100;
    public int rankE = 0;

    [Header("UI Feedback")]
    [Tooltip("スコア増加時の色")]
    public Color positiveColor = Color.green;
    [Tooltip("スコア減少時の色")]
    public Color negativeColor = Color.red;
    [Tooltip("通常時の色")]
    public Color defaultColor = Color.white;
    [Tooltip("色が戻るまでの時間")]
    public float colorResetTime = 0.5f;

    [Header("Effects")]
    [Tooltip("スコア増加時のエフェクト")]
    public GameObject positiveScoreEffect;

    [Tooltip("スコア減少時のエフェクト")]
    public GameObject negativeScoreEffect;

    [Tooltip("スコア獲得時の効果音")]
    public AudioClip scoreSound;

    [Tooltip("スコア減少時の効果音")]
    public AudioClip negativeScoreSound;

    [Tooltip("ハイスコア更新時の効果音")]
    public AudioClip highScoreSound;

    private AudioSource audioSource;
    private int activeAngryCustomerCount = 0;
    private float scoreAccumulator = 0f;
    private Coroutine colorCoroutine;
    private bool isRecording = false;
    private Coroutine recordingCoroutine; // 録画コルーチンだけを個別に停止するための参照

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            // 2周目以降はシーン側の新しいインスタンスを破棄する。
            // ただしUI参照は古いシーンのものが死んでいるため、新しいシーンのものへ引き継ぐ。
            Instance.AdoptSceneReferences(scoreText, highScoreText);
            Destroy(gameObject);
            return;
        }

        audioSource = gameObject.GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        LoadHighScore();
    }

    void Start()
    {
        if (scoreText != null)
        {
            scoreText.color = defaultColor;
        }
        UpdateScoreUI();

        StartRecording();
    }

    void Update()
    {
        // クレーマーが店内にいる間、滞在人数×秒数分をスコアから継続的に減点する
        if (activeAngryCustomerCount > 0)
        {
            totalAngryTime += Time.deltaTime;
            scoreAccumulator += Time.deltaTime * activeAngryCustomerCount * penaltyPerCustomerPerSecond;

            // penaltyStep分溜まるごとに1回だけ減点する（フレームごとの端数減点を避けるため）
            if (scoreAccumulator >= penaltyStep)
            {
                int step = Mathf.Max(1, Mathf.FloorToInt(penaltyStep));
                AddScore(-step);
                scoreAccumulator -= step;
            }
        }
    }

    /// <summary>
    /// ゲームシーンを再ロードした際、破棄される側のインスタンスからUI参照を受け取る。
    /// あわせてリプレイ録画も新しいプレイ用に再開する。
    /// </summary>
    private void AdoptSceneReferences(TMP_Text newScoreText, TMP_Text newHighScoreText)
    {
        // 前のシーンのUIに対する色復帰コルーチンが残っていると、
        // 参照を差し替えた後も古いUI向けの処理が走り続けるため、ここで打ち切る
        if (colorCoroutine != null)
        {
            StopCoroutine(colorCoroutine);
            colorCoroutine = null;
        }

        scoreText = newScoreText;
        highScoreText = newHighScoreText;

        if (scoreText != null)
        {
            scoreText.color = defaultColor;
        }
        UpdateScoreUI();

        StartRecording();
    }

    public void RegisterAngryCustomer()
    {
        activeAngryCustomerCount++;
    }

    public void UnregisterAngryCustomer()
    {
        if (activeAngryCustomerCount > 0)
        {
            activeAngryCustomerCount--;
        }
    }

    /// <summary>
    /// スコアを加算する（負の値を渡すと減点になる）。
    /// </summary>
    public void AddScore(int points)
    {
        if (points == 0) return;

        currentScore += points;

        bool isNewHighScore = false;
        if (currentScore > highScore)
        {
            highScore = currentScore;
            SaveHighScore();
            isNewHighScore = true;
        }

        UpdateScoreUI();

        if (points > 0)
        {
            PlayScoreSound(isNewHighScore);
            PlayEffect(positiveScoreEffect);
            FlashColor(positiveColor);
        }
        else
        {
            PlayNegativeSound();
            PlayEffect(negativeScoreEffect);
            FlashColor(negativeColor);
        }
    }

    // ---- 注文結果の記録（CustomerOrderWithTimerから呼び出される） ----

    /// <summary>正解の寿司を提供できた際の記録とスコア加算をまとめて行う。</summary>
    public void RegisterCorrectOrder(int scoreToAdd, float serviceDuration)
    {
        servedCount++;
        totalServiceTime += serviceDuration;
        AddScore(scoreToAdd);
    }

    /// <summary>誤った寿司を提供した際の記録と減点をまとめて行う。</summary>
    public void RegisterWrongOrder(int penalty)
    {
        wrongCount++;
        AddScore(-Mathf.Abs(penalty));
    }

    /// <summary>制限時間内に提供できなかった際の記録と減点をまとめて行う。</summary>
    public void RegisterTimeout(int penalty)
    {
        missedCount++;
        AddScore(-Mathf.Abs(penalty));
    }

    /// <summary>
    /// スコアと統計を初期化する（リザルトからタイトルへ戻る際に呼ばれる）。
    /// インスタンスがシーンをまたいで残るため、前回プレイの値が持ち越されないようにする。
    /// </summary>
    public void ResetScore()
    {
        currentScore = 0;
        scoreAccumulator = 0f;

        servedCount = 0;
        wrongCount = 0;
        missedCount = 0;
        totalServiceTime = 0f;
        totalAngryTime = 0f;
        activeAngryCustomerCount = 0;

        UpdateScoreUI();
    }

    public int GetCurrentScore()
    {
        return currentScore;
    }

    public int GetHighScore()
    {
        return highScore;
    }

    // ---- リプレイ記録 ----

    public void StartRecording()
    {
        if (isRecording) return; // 二重開始による録画コルーチンの多重起動を防ぐ

        // 前回プレイのフレームはここで完全に破棄する。
        // Texture2Dが抱えるネイティブメモリはGC対象外のため、
        // List.Clear()だけではテクスチャ本体が残り続けてメモリリークになる。
        ClearReplayFrames();

        isRecording = true;
        recordingCoroutine = StartCoroutine(RecordGameLoop());
    }

    public void StopRecording()
    {
        // 録画コルーチンだけを個別に停止する。StopAllCoroutines()では
        // スコア色復帰（ResetColorCoroutine）など無関係なコルーチンまで
        // 巻き込まれてしまうため使わない。
        isRecording = false;
        if (recordingCoroutine != null)
        {
            StopCoroutine(recordingCoroutine);
            recordingCoroutine = null;
        }

        // 注意: ここではreplayFramesを破棄しない。
        // 直後のリザルト画面がリプレイ表示に使うため、破棄は使用が終わる
        // タイミング（次の録画開始時・タイトル復帰時・自身の破棄時）に行う。
    }

    /// <summary>
    /// リプレイ用テクスチャをすべて破棄してリストを空にする。
    /// リザルト画面での表示が終わったタイミングで呼ぶこと（表示中に呼ぶと参照先が消える）。
    /// </summary>
    public void ClearReplayFrames()
    {
        foreach (Texture2D frame in replayFrames)
        {
            if (frame != null) Destroy(frame);
        }
        replayFrames.Clear();
    }

    private void OnDestroy()
    {
        // シングルトン本体の破棄時（アプリ終了など）はテクスチャも合わせて解放する。
        // シーンロード時に即破棄される2個目以降のインスタンスは録画フレームを
        // 持っていないため、本体のリストを誤って消さないようInstance判定で除外する。
        if (Instance == this)
        {
            ClearReplayFrames();
            Instance = null;
        }
    }

    System.Collections.IEnumerator RecordGameLoop()
    {
        while (isRecording)
        {
            yield return new WaitForSeconds(recordInterval);
            yield return new WaitForEndOfFrame();

            Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
            replayFrames.Add(texture);

            // 一定枚数を超えたら古いフレームから破棄する（メモリリーク防止）
            if (replayFrames.Count > maxFrames)
            {
                Texture2D old = replayFrames[0];
                replayFrames.RemoveAt(0);
                Destroy(old);
            }
        }
    }

    // ---- UI・演出 ----

    private void PlayNegativeSound()
    {
        if (audioSource != null && negativeScoreSound != null)
        {
            audioSource.PlayOneShot(negativeScoreSound);
        }
    }

    private void FlashColor(Color targetColor)
    {
        if (scoreText == null) return;

        // 連続で得点・減点があった場合は前の演出を打ち切り、最新の色だけを反映する
        if (colorCoroutine != null) StopCoroutine(colorCoroutine);
        colorCoroutine = StartCoroutine(ResetColorCoroutine(scoreText, targetColor));
    }

    // 色を変えるUIを引数targetで固定する。ScoreManagerはDontDestroyOnLoadで
    // シーンをまたいで生き残るため、待機中にscoreTextが別シーンのUIへ
    // 差し替わっても、このコルーチンは開始時に指定されたUIしか触らない。
    private System.Collections.IEnumerator ResetColorCoroutine(TMP_Text target, Color targetColor)
    {
        // 破棄済みのUIに触れないよう、Unityのnull比較で有効性を確認する
        if (target != null)
        {
            target.color = targetColor;
        }

        yield return new WaitForSeconds(colorResetTime);

        // 待機中のシーン遷移でtargetが破棄されている可能性があるため再確認する
        if (target != null)
        {
            target.color = defaultColor;
        }

        colorCoroutine = null;
    }

    private void PlayEffect(GameObject effectPrefab)
    {
        if (effectPrefab != null && scoreText != null)
        {
            Instantiate(effectPrefab, scoreText.transform.position, Quaternion.identity, scoreText.transform.parent);
        }
    }

    private void UpdateScoreUI()
    {
        if (scoreText != null)
        {
            scoreText.text = $"スコア: {currentScore}";
        }

        if (highScoreText != null)
        {
            highScoreText.text = $"ハイスコア: {highScore}";
        }
    }

    private void PlayScoreSound(bool isHighScore)
    {
        if (audioSource == null) return;

        if (isHighScore && highScoreSound != null)
        {
            audioSource.PlayOneShot(highScoreSound);
        }
        else if (scoreSound != null)
        {
            audioSource.PlayOneShot(scoreSound);
        }
    }

    private void SaveHighScore()
    {
        PlayerPrefs.SetInt("HighScore", highScore);
        PlayerPrefs.Save();
    }

    private void LoadHighScore()
    {
        highScore = PlayerPrefs.GetInt("HighScore", 0);
    }
}
