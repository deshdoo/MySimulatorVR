using UnityEngine;
using UnityEngine.UI;

// Минимальный скрипт-обёртка: меняет альфу указанного Image (или RawImage)
// в зависимости от уровня повреждения камеры RovSystems.cameraDamage.
//
// Canvas + Image (или RawImage с любой текстурой шума) ставишь сам:
//   1. Создать World Space Canvas перед экраном-монитором кабины.
//   2. Положить туда Image со спрайтом шума ИЛИ RawImage с видео/гифкой.
//   3. Повесить этот скрипт на тот же Image — он сам подхватит.
//   4. Или повесить на любой объект и перетащить Image вручную в поле.
public class MonitorNoiseOverlay : MonoBehaviour
{
    [Header("Image / RawImage с шумом (можно оставить пустым — возьмётся с этого объекта)")]
    public Graphic слойНаложения;

    [Header("Альфа на каждом уровне повреждения")]
    [Range(0f, 1f)] public float альфаПриНорме    = 0f;
    [Range(0f, 1f)] public float альфаПриWarning  = 0.25f;
    [Range(0f, 1f)] public float альфаПриCritical = 0.65f;

    [Header("Скорость перехода (сек)")]
    public float скоростьПерехода = 0.5f;

    private float _currentAlpha;

    void Start()
    {
        if (слойНаложения == null) слойНаложения = GetComponent<Graphic>();
        if (слойНаложения == null)
        {
            Debug.LogWarning("[MonitorNoiseOverlay] Не назначен Image/RawImage");
            enabled = false;
        }
    }

    void Update()
    {
        if (слойНаложения == null) return;

        float targetAlpha = альфаПриНорме;
        switch (RovSystems.cameraDamage)
        {
            case 1: targetAlpha = альфаПриWarning;  break;
            case 2: targetAlpha = альфаПриCritical; break;
        }

        float k = Time.deltaTime / Mathf.Max(0.01f, скоростьПерехода);
        _currentAlpha = Mathf.Lerp(_currentAlpha, targetAlpha, k);

        var c = слойНаложения.color;
        c.a = _currentAlpha;
        слойНаложения.color = c;
    }
}
