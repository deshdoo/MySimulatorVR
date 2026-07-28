// =============================================================================
// SystemStatusDisplay.cs
// -----------------------------------------------------------------------------
// Обновление UI первого нижнего монитора по данным из RovSystems.
// =============================================================================

using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SystemStatusDisplay : MonoBehaviour
{
    [System.Serializable]
    public class СтрокаСистемы
    {
        [Tooltip("Кружок ● — Image или TMP_Text")]
        public Graphic Круг;
        [Tooltip("Текстовый индикатор статуса (ОК / ВЫКЛ / АВАРИЯ)")]
        public TMP_Text Индикатор;
    }

    [Header("Индикаторы систем")]
    public СтрокаСистемы Питание;
    public СтрокаСистемы Движители;
    public СтрокаСистемы Манипулятор;
    public СтрокаСистемы Прожекторы;
    public СтрокаСистемы Камера;
    public СтрокаСистемы Связь;

    [Header("Энергосистема (тексты)")]
    public TMP_Text ЗарядПроцент;
    public TMP_Text ГлавнаяШина;
    public TMP_Text ВспомогательнаяШина;
    public TMP_Text Резерв;
    [Tooltip("Буква 'О' (TMP_Text) или Image — меняет цвет по статусу АКБ")]
    public Graphic Кольцо;

    [Header("Режим управления")]
    public TMP_Text КнопкаРучной;
    public TMP_Text КнопкаУдержание;
    [Tooltip("Подложка кнопки «Ручной» — Image (Graphic)")]
    public Graphic ФонРучной;
    [Tooltip("Подложка кнопки «Удержание» — Image (Graphic)")]
    public Graphic ФонУдержание;

    [Header("Цвета статусов")]
    public Color colorOk       = new Color(0.30f, 1.00f, 0.45f);
    public Color colorWarning  = new Color(1.00f, 0.78f, 0.20f);
    public Color colorCritical = new Color(1.00f, 0.30f, 0.30f);
    public Color colorOff      = new Color(0.50f, 0.50f, 0.50f);
    public Color colorWorking  = new Color(0.30f, 0.85f, 1.00f);

    [Header("Цвет активного режима")]
    public Color modeActiveColor   = new Color(0.30f, 0.85f, 1.00f);
    public Color modeInactiveColor = new Color(0.60f, 0.65f, 0.70f);

    [Header("Фон кнопок режима")]
    [Tooltip("Фон активной кнопки — бледно-синий")]
    public Color modeActiveBg   = new Color(0.22f, 0.40f, 0.58f, 1.00f);
    [Tooltip("Фон неактивной кнопки — тёмный")]
    public Color modeInactiveBg = new Color(0.07f, 0.09f, 0.12f, 1.00f);
    [Tooltip("Скорость перетекания цвета, ед/с (0 — переключать мгновенно)")]
    public float modeFadeSpeed = 8f;

    void Update()
    {
        Apply(Питание,     RovSystems.powerState,         StateName(RovSystems.powerState));
        ApplyДвижители();
        Apply(Манипулятор, RovSystems.manipulatorState,   RovSystems.grabberClosed ? "ВКЛ" : "ВЫКЛ");
        Apply(Прожекторы,  RovSystems.lightsState,        RovSystems.lightsOn ? "ВКЛ" : "ВЫКЛ");
        Apply(Камера,      RovSystems.cameraState,        StateName(RovSystems.cameraState));
        Apply(Связь,       RovSystems.communicationState, $"{RovSystems.rssi_dB:F0} дБ");

        // Энергосистема
        if (ЗарядПроцент         != null) ЗарядПроцент.text         = $"{Mathf.RoundToInt(RovSystems.batteryPercent)}";
        if (ГлавнаяШина          != null) ГлавнаяШина.text          = $"Гл шина {RovSystems.batteryVoltage_V:F1} В";
        if (ВспомогательнаяШина  != null) ВспомогательнаяШина.text  = $"Всп шина {RovSystems.auxBusVoltage_V:F1} В";
        if (Резерв               != null) Резерв.text               = $"Резерв {Mathf.RoundToInt(RovSystems.reservePercent)} %";
        if (Кольцо               != null) Кольцо.color              = ColorFor(RovSystems.powerState);

        // Режим
        bool hold = RovSystems.depthHoldActive;
        if (КнопкаРучной    != null) КнопкаРучной.color    = hold ? modeInactiveColor : modeActiveColor;
        if (КнопкаУдержание != null) КнопкаУдержание.color = hold ? modeActiveColor   : modeInactiveColor;

        // Подложки кнопок: активная — бледно-синяя, неактивная — тёмная
        FadeTo(ФонРучной,    hold ? modeInactiveBg : modeActiveBg);
        FadeTo(ФонУдержание, hold ? modeActiveBg   : modeInactiveBg);
    }

    void FadeTo(Graphic g, Color target)
    {
        if (g == null) return;
        g.color = modeFadeSpeed <= 0f
            ? target
            : Color.Lerp(g.color, target, 1f - Mathf.Exp(-modeFadeSpeed * Time.deltaTime));
    }

    void Apply(СтрокаСистемы row, SystemState st, string text)
    {
        if (row == null) return;
        Color c = ColorFor(st);
        if (row.Круг       != null) row.Круг.color = c;
        if (row.Индикатор  != null) { row.Индикатор.color = c; row.Индикатор.text = text; }
    }

    // Движители — особый случай: строка совмещает ДВА разных повода для тревоги —
    // перегрев (температура) и механическое повреждение от удара. Показывать их
    // одним цветом было путано: исправная по нагреву температура (напр. 8°C)
    // краснела из-за урона и читалась как «перегрев». Теперь разделено:
    //   есть урон  -> пишем «ПОВРЕЖД.» цветом урона (не показываем температуру);
    //   урона нет  -> показываем температуру цветом ПО НАГРЕВУ (thrusterTempState).
    void ApplyДвижители()
    {
        if (Движители == null) return;

        SystemState st;
        string text;
        if (RovSystems.thrusterDamage > 0)
        {
            st = RovSystems.thrusterDamage == 1 ? SystemState.Warning : SystemState.Critical;
            text = "ПОВРЕЖД.";
        }
        else
        {
            st = RovSystems.thrusterTempState;
            text = $"{RovSystems.thrusterTemp_C:F0}°C";
        }

        Color c = ColorFor(st);
        if (Движители.Круг      != null) Движители.Круг.color = c;
        if (Движители.Индикатор != null) { Движители.Индикатор.color = c; Движители.Индикатор.text = text; }
    }

    Color ColorFor(SystemState s)
    {
        switch (s)
        {
            case SystemState.OK:       return colorOk;
            case SystemState.Warning:  return colorWarning;
            case SystemState.Critical: return colorCritical;
            case SystemState.Working:  return colorWorking;
            default:                   return colorOff;
        }
    }

    string StateName(SystemState s)
    {
        switch (s)
        {
            case SystemState.OK:       return "ОК";
            case SystemState.Warning:  return "ВНИМ.";
            case SystemState.Critical: return "АВАРИЯ";
            case SystemState.Working:  return "РАБОТА";
            default:                   return "ВЫКЛ";
        }
    }

}
