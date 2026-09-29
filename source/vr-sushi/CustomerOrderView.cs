using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 客の頭上に表示する注文UI（テキスト・アイコン・残り時間・タイムゲージ）を更新する。
/// 注文の判定や時間管理はCustomerOrderWithTimerが行う。
/// </summary>
public class CustomerOrderView : MonoBehaviour
{
    [Header("注文表示UI")]
    [SerializeField] private GameObject orderCanvas;
    [SerializeField] private TMP_Text orderText;
    [SerializeField] private Image orderImage;
    [SerializeField] private TMP_Text timerText;

    [Header("タイムゲージ")]
    [SerializeField] private Slider timeSlider;
    [SerializeField] private Image timeSliderFill;
    [SerializeField] private Color greenColor = Color.green;
    [SerializeField] private Color yellowColor = Color.yellow;
    [SerializeField] private Color redColor = Color.red;
    [Range(0f, 1f)] [SerializeField] private float yellowThreshold = 0.6f;
    [Range(0f, 1f)] [SerializeField] private float redThreshold = 0.3f;

    private void Awake()
    {
        HideOrder();

        if (timeSlider != null)
        {
            timeSlider.minValue = 0f;
            timeSlider.maxValue = 1f;
            timeSlider.value = 1f;
        }
    }

    private void OnValidate()
    {
        // 赤のしきい値が黄のしきい値を上回らないように補正する
        if (redThreshold > yellowThreshold)
        {
            Debug.LogWarning($"{name}: redThresholdがyellowThresholdより大きいため入れ替えます。", this);
            float temp = redThreshold;
            redThreshold = yellowThreshold;
            yellowThreshold = temp;
        }
    }

    public void ShowOrder(SushiOrderData order)
    {
        if (orderCanvas != null) orderCanvas.SetActive(true);

        UpdateOrderLabel(order);
        ResetGaugeToFull();
    }

    public void HideOrder()
    {
        if (orderCanvas != null) orderCanvas.SetActive(false);
    }

    private void UpdateOrderLabel(SushiOrderData order)
    {
        if (orderText != null)
        {
            orderText.text = order.kind.ToString();
        }

        if (orderImage == null) return;

        Sprite sprite = order.icon;
        orderImage.sprite = sprite;
        orderImage.enabled = (sprite != null);

        // アイコン未設定時はテキスト表示にフォールバック
        if (orderText != null)
            orderText.enabled = (sprite == null);
    }

    public void ResetGaugeToFull()
    {
        if (timeSlider != null)
        {
            timeSlider.value = 1f;
        }
        UpdateGaugeColor(1f);
    }

    public void UpdateRemainingTime(float remainingTime, float timeLimit)
    {
        UpdateTimerLabel(remainingTime);

        float ratio = timeLimit > 0f ? Mathf.Clamp01(remainingTime / timeLimit) : 0f;
        UpdateGauge(ratio);
    }

    private void UpdateTimerLabel(float remainingTime)
    {
        if (timerText == null) return;
        int seconds = Mathf.CeilToInt(remainingTime);
        timerText.text = $"{seconds}s";
    }

    private void UpdateGauge(float ratio)
    {
        if (timeSlider != null)
        {
            timeSlider.value = ratio;
        }
        UpdateGaugeColor(ratio);
    }

    private void UpdateGaugeColor(float ratio)
    {
        if (timeSliderFill == null) return;

        if (ratio <= redThreshold)
            timeSliderFill.color = redColor;
        else if (ratio <= yellowThreshold)
            timeSliderFill.color = yellowColor;
        else
            timeSliderFill.color = greenColor;
    }
}
