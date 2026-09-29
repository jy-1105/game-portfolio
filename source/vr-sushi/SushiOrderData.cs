using UnityEngine;

/// <summary>
/// 1種類の寿司に紐づく注文データ（種類・アイコン・注文ボイス）をまとめたもの。
/// 注文候補は、InspectorでCustomerOrderWithTimerのpossibleOrders配列に登録する。
/// </summary>
[System.Serializable]
public class SushiOrderData
{
    [Tooltip("寿司の種類")]
    public SushiKind kind;

    [Tooltip("注文UIに表示するアイコン（未設定の場合はテキスト表示にフォールバック）")]
    public Sprite icon;

    [Tooltip("注文時に再生するボイス（未設定の場合は無音）")]
    public AudioClip voiceClip;
}
