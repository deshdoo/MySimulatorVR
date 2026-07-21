// =============================================================================
// TelemetryRecorder.cs
// -----------------------------------------------------------------------------
// Регистратор телеметрии ("чёрный ящик") для графиков второго монитора.
// Пишет значения из RovSystems/RovAutopilot в кольцевые буферы с ФИКСИРОВАННЫМ
// шагом дискретизации, а не каждый кадр: иначе ось времени графика "дышала" бы
// вместе с FPS и по картинке нельзя было бы судить о длительности переходного
// процесса.
//
// Один регистратор на сцену (static Instance), графики TelemetryGraph только
// читают. Канал, которого сейчас нет (автопилот выключен), пишет NaN — график
// рисует на этом месте разрыв линии, а не ноль: "данных не было" и "было ноль"
// это разные вещи.
//
// Настройка: пустой GameObject в сцене пульта -> Add Component -> TelemetryRecorder.
// =============================================================================

using UnityEngine;

public enum TelemetryChannel
{
    // Контур регулирования глубины (ПИД) — основное для графиков ТАУ
    Глубина,            // м, факт
    УставкаГлубины,     // м, задание автопилота (NaN когда выключен)
    ОшибкаРегулирования,// м, e = уставка − факт (NaN когда выключен)
    УправлениеВертикаль,// доля −1..1, u на вертикальную тягу

    // Пространственное положение
    Курс,               // град
    Тангаж,             // град
    Крен,               // град
    Скорость,           // м/с
    ВысотаНадДном,      // м

    // Энергетика и нагрев
    ЗарядАКБ,           // %
    НапряжениеШины,     // В
    ТокНагрузки,        // А
    МощностьДвижителей, // Вт
    ТемператураДвижителей, // °C

    // Прочее
    ДавлениеНаКорпус,   // кПа
    КачествоСвязи,      // дБ

    // ВНИМАНИЕ: новые каналы добавлять ТОЛЬКО В КОНЕЦ списка и ничего не
    // переставлять. Unity сохраняет enum в сцене как ЧИСЛО, а не как имя:
    // вставка значения в середину сдвигает все последующие, и уже настроенные
    // в инспекторе графики начинают показывать чужие каналы.
    ВкладП,             // пропорциональная составляющая выхода ПИД глубины
    ВкладИ,             // интегральная составляющая
    ВкладД,             // дифференциальная составляющая
    СкоростьТечения,    // м/с, скорость воды в точке аппарата
}

[DisallowMultipleComponent]
public class TelemetryRecorder : MonoBehaviour
{
    public static TelemetryRecorder Instance { get; private set; }

    [Header("Дискретизация")]
    [Tooltip("Шаг записи, с. 0.05 = 20 Гц — достаточно, чтобы увидеть колебания ПИДа.")]
    public float sampleInterval_s = 0.05f;
    [Tooltip("Глубина истории, с. Столько времени показывает график по горизонтали.")]
    public float historyLength_s = 60f;

    private float[][] _buffers;   // [канал][позиция в кольце]
    private int _capacity;        // размер кольца
    private int _head;            // куда пишем следующий сэмпл
    private int _count;           // сколько накоплено (<= _capacity)
    private float _nextSampleTime;

    /// <summary>Сколько сэмплов доступно (0.._capacity).</summary>
    public int Count => _count;
    /// <summary>Вместимость кольца = сколько сэмплов укладывается в окно истории.</summary>
    public int Capacity => _capacity;
    /// <summary>Шаг по времени между соседними сэмплами, с.</summary>
    public float SampleInterval => sampleInterval_s;

    void Awake()
    {
        Instance = this;
        _capacity = Mathf.Max(2, Mathf.CeilToInt(historyLength_s / Mathf.Max(0.001f, sampleInterval_s)));

        int channels = System.Enum.GetValues(typeof(TelemetryChannel)).Length;
        _buffers = new float[channels][];
        for (int i = 0; i < channels; i++) _buffers[i] = new float[_capacity];

        _head = 0;
        _count = 0;
        _nextSampleTime = 0f;

        Debug.Log($"[TelemetryRecorder] Запущен на «{name}»: {1f / sampleInterval_s:F0} Гц, окно {historyLength_s:F0} с, {_capacity} сэмплов.", this);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        // Пока симулятор не посчитал первый кадр, в RovSystems лежат нули по
        // умолчанию. Записывать их нельзя: на графике это неотличимо от реального
        // провала величины в 0 — именно так линия глубины начиналась «от нуля».
        if (!RovSystems.telemetryValid)
        {
            _nextSampleTime = Time.time;
            return;
        }

        // Догоняем пропущенные шаги, если кадр был длинным: сетка по времени
        // должна остаться равномерной, иначе график врёт по оси X.
        int guard = 0;
        while (Time.time >= _nextSampleTime && guard++ < 16)
        {
            WriteSample();
            _nextSampleTime += sampleInterval_s;
        }
        // Кадр провалился слишком сильно (загрузка сцены и т.п.) — не догоняем вечно.
        if (guard >= 16) _nextSampleTime = Time.time + sampleInterval_s;
    }

    void WriteSample()
    {
        var ap = RovAutopilot.Instance;
        bool hold = RovSystems.depthHoldActive && ap != null;

        Put(TelemetryChannel.Глубина,             RovSystems.depth_m);
        Put(TelemetryChannel.УставкаГлубины,      hold ? ap.TargetDepth_m : float.NaN);
        Put(TelemetryChannel.ОшибкаРегулирования, hold ? ap.DepthError_m  : float.NaN);
        Put(TelemetryChannel.УправлениеВертикаль, hold ? ap.VerticalCommand : DroneInput.vertical);
        Put(TelemetryChannel.ВкладП, hold ? ap.ВкладП : float.NaN);
        Put(TelemetryChannel.ВкладИ, hold ? ap.ВкладИ : float.NaN);
        Put(TelemetryChannel.ВкладД, hold ? ap.ВкладД : float.NaN);

        Put(TelemetryChannel.Курс,          RovSystems.heading_deg);
        Put(TelemetryChannel.Тангаж,        RovSystems.pitch_deg);
        Put(TelemetryChannel.Крен,          RovSystems.roll_deg);
        Put(TelemetryChannel.Скорость,      RovSystems.speed_mps);
        Put(TelemetryChannel.ВысотаНадДном, RovSystems.hasBottomEcho ? RovSystems.altitudeAboveBottom_m : float.NaN);

        Put(TelemetryChannel.ЗарядАКБ,              RovSystems.batteryPercent);
        Put(TelemetryChannel.НапряжениеШины,        RovSystems.batteryVoltage_V);
        Put(TelemetryChannel.ТокНагрузки,           RovSystems.currentDraw_A);
        Put(TelemetryChannel.МощностьДвижителей,    RovSystems.thrusterPower_W);
        Put(TelemetryChannel.ТемператураДвижителей, RovSystems.thrusterTemp_C);

        Put(TelemetryChannel.СкоростьТечения,  RovSystems.currentSpeed_mps);
        Put(TelemetryChannel.ДавлениеНаКорпус, RovSystems.hullPressure_kPa);
        Put(TelemetryChannel.КачествоСвязи,    RovSystems.rssi_dB);

        // Продвигаем кольцо ОДИН раз на весь сэмпл, после записи всех каналов.
        _head = (_head + 1) % _capacity;
        if (_count < _capacity) _count++;
    }

    void Put(TelemetryChannel ch, float value) => _buffers[(int)ch][_head] = value;

    /// <summary>
    /// Сэмпл по индексу: 0 — самый старый в окне, Count−1 — самый свежий.
    /// Может вернуть NaN — значит в этот момент данных по каналу не было.
    /// </summary>
    public float GetSample(TelemetryChannel ch, int index)
    {
        if (_buffers == null || index < 0 || index >= _count) return float.NaN;
        // Самый старый лежит на _head, когда кольцо заполнено; иначе с нуля.
        int start = _count == _capacity ? _head : 0;
        return _buffers[(int)ch][(start + index) % _capacity];
    }

    /// <summary>Последнее записанное значение канала (NaN, если записей ещё нет).</summary>
    public float GetLatest(TelemetryChannel ch) => GetSample(ch, _count - 1);

    /// <summary>Единица измерения канала — для подписей на графике.</summary>
    public static string UnitOf(TelemetryChannel ch)
    {
        switch (ch)
        {
            case TelemetryChannel.Глубина:
            case TelemetryChannel.УставкаГлубины:
            case TelemetryChannel.ОшибкаРегулирования:
            case TelemetryChannel.ВысотаНадДном:          return "м";
            case TelemetryChannel.УправлениеВертикаль:
            case TelemetryChannel.ВкладП:
            case TelemetryChannel.ВкладИ:
            case TelemetryChannel.ВкладД:                 return "";
            case TelemetryChannel.Курс:
            case TelemetryChannel.Тангаж:
            case TelemetryChannel.Крен:                   return "°";
            case TelemetryChannel.Скорость:
            case TelemetryChannel.СкоростьТечения:        return "м/с";
            case TelemetryChannel.ЗарядАКБ:               return "%";
            case TelemetryChannel.НапряжениеШины:         return "В";
            case TelemetryChannel.ТокНагрузки:            return "А";
            case TelemetryChannel.МощностьДвижителей:     return "Вт";
            case TelemetryChannel.ТемператураДвижителей:  return "°C";
            case TelemetryChannel.ДавлениеНаКорпус:       return "кПа";
            case TelemetryChannel.КачествоСвязи:          return "дБ";
            default:                                      return "";
        }
    }
}
