using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Регистратор телеметрии в CSV — для обработки вне симулятора (Excel, Python) и
// графиков в тексте ВКР. В отличие от TelemetryRecorder (кольцо на 60 с для
// мониторов пульта) пишет ВСЮ миссию целиком в файл:
//   <persistentDataPath>/Телеметрия/миссия_ГГГГ-ММ-ДД_чч-мм-сс.csv
//
// Формат — под русский Excel: разделитель «;», десятичная запятая, UTF-8 с BOM
// (иначе кириллица в заголовках ломается). В Python:
//   pandas.read_csv(путь, sep=';', decimal=',')
//
// Шаг записи фиксированный (по умолчанию 20 Гц), время — время миссии
// (RovSystems.missionTime_s: при «Продолжить» идёт дальше, а не с нуля).
// Рядом с показанием каждого датчика — истина движка: так по файлу видно и
// поведение аппарата, и погрешности приборов. Пустая ячейка — данных не было
// (нет эха дна, автопилот выключен).
//
// Ставится сам на объект НПА (RovController.Start). Пока сцена заморожена
// (меню, окно итогов) — не пишет: компонент выключен заморозкой.
public class TelemetryCsvLogger : MonoBehaviour
{
    public static TelemetryCsvLogger Instance { get; private set; }

    [Tooltip("Шаг записи, с (0.05 = 20 Гц)")]
    public float ШагЗаписи_с = 0.05f;
    [Tooltip("Префикс имени файла: «миссия», а при испытательных манёврах — «испытания»")]
    public string Префикс = "миссия";

    public string Путь { get; private set; }

    static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    StreamWriter _w;
    Rigidbody _rb;
    float _след, _сбросДо;
    readonly StringBuilder _sb = new StringBuilder(512);

    // Колонки: заголовок (с единицей) и как получить значение. NaN → пустая ячейка.
    static readonly (string имя, Func<TelemetryCsvLogger, float> знач)[] Колонки =
    {
        ("время_с",                 _ => RovSystems.missionTime_s),
        ("глубина_м",               _ => RovSystems.depth_m),
        ("глубина_истина_м",        _ => RovSystems.depthTrue_m),
        ("уставка_глубины_м",       _ => RovSystems.depthHoldActive ? RovSystems.depthHoldTarget_m : float.NaN),
        ("курс_град",               _ => RovSystems.heading_deg),
        ("курс_истина_град",        _ => RovSystems.headingTrue_deg),
        ("курс_гиро_град",          _ => RovSystems.headingGyro_deg),
        ("тангаж_град",             _ => RovSystems.pitch_deg),
        ("тангаж_истина_град",      _ => RovSystems.pitchTrue_deg),
        ("крен_град",               _ => RovSystems.roll_deg),
        ("крен_истина_град",        _ => RovSystems.rollTrue_deg),
        ("скорость_мс",             _ => RovSystems.speed_mps),
        ("скорость_истина_мс",      _ => RovSystems.speedTrue_mps),
        ("высота_над_дном_м",       _ => RovSystems.hasBottomEcho ? RovSystems.altitudeAboveBottom_m : float.NaN),
        ("высота_истина_м",         _ => RovSystems.hasBottomEcho ? RovSystems.altitudeTrue_m : float.NaN),
        ("течение_мс",              _ => RovSystems.currentSpeed_mps),
        ("vx_связ_мс",              л => л.Связ().x),   // скорость в осях корпуса: вбок
        ("vy_связ_мс",              л => л.Связ().y),   // вверх
        ("vz_связ_мс",              л => л.Связ().z),   // вперёд
        ("x_м",                     л => л.transform.position.x),
        ("y_м",                     л => л.transform.position.y),
        ("z_м",                     л => л.transform.position.z),
        ("команда_вперёд",          _ => DroneInput.forward),
        ("команда_вертикаль",       _ => DroneInput.vertical),
        ("команда_лаг",             _ => DroneInput.lateral),
        ("команда_рыскание",        _ => DroneInput.yaw),
        ("автопилот",               _ => RovSystems.depthHoldActive ? 1f : 0f),
        ("резерв_АКБ_проц",         _ => RovSystems.batteryPercent),
        ("напряжение_на_аппарате_В",_ => RovSystems.batteryVoltage_V),
        ("ток_кабеля_А",            _ => RovSystems.currentDraw_A),
        ("ток_кабеля_истина_А",     _ => RovSystems.currentDrawTrue_A),
        ("мощность_с_судна_Вт",     _ => RovSystems.linePower_W),
        ("потери_в_кабеле_Вт",      _ => RovSystems.cableLoss_W),
        ("доля_тяги_по_питанию",    _ => RovSystems.powerLimitFactor),
        ("натяжение_кабеля_Н",      _ => UmbilicalCable.Instance != null ? UmbilicalCable.Instance.Натяжение_Н : 0f),
        ("ошибка_курса_град",       _ => RovAutopilot.Instance != null && RovAutopilot.Instance.Engaged ? RovAutopilot.Instance.HeadingError_deg : float.NaN),
        ("мощность_движителей_Вт",  _ => RovSystems.thrusterPower_W),
        ("температура_движителей_С",_ => RovSystems.thrusterTemp_C),
        ("давление_кПа",            _ => RovSystems.hullPressure_kPa),
        ("давление_истина_кПа",     _ => RovSystems.hullPressureTrue_kPa),
        ("связь_дБ",                _ => RovSystems.rssi_dB),
        ("мутность",                _ => RovSystems.turbidity),
        ("свет",                    _ => RovSystems.lightsOn ? 1f : 0f),
        ("клешня",                  _ => RovSystems.grabberClosed ? 1f : 0f),
    };

    Vector3 Связ() => _rb != null ? transform.InverseTransformDirection(_rb.linearVelocity) : Vector3.zero;

    // Задаётся до старта сцены (например, меню перед испытаниями) — имя следующего файла.
    public static string ПрефиксСледующего;

    void Awake()
    {
        Instance = this; _rb = GetComponent<Rigidbody>();
        if (!string.IsNullOrEmpty(ПрефиксСледующего)) { Префикс = ПрефиксСледующего; ПрефиксСледующего = null; }
    }

    void OnEnable() => _след = Time.time;   // после разморозки — без «догоняния» пропущенного

    void Update()
    {
        if (!RovSystems.telemetryValid) { _след = Time.time; return; }
        int защита = 0;
        while (Time.time >= _след && защита++ < 16)
        {
            Записать();
            _след += ШагЗаписи_с;
        }
        if (защита >= 16) _след = Time.time + ШагЗаписи_с;
        // На диск — раз в секунду: при вылете теряется не больше секунды записи.
        if (_w != null && Time.unscaledTime >= _сбросДо) { _w.Flush(); _сбросДо = Time.unscaledTime + 1f; }
    }

    void Открыть()
    {
        try
        {
            string папка = Path.Combine(Application.persistentDataPath, "Телеметрия");
            Directory.CreateDirectory(папка);
            Путь = Path.Combine(папка, $"{Префикс}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv");
            _w = new StreamWriter(Путь, false, new UTF8Encoding(true));
            _sb.Clear();
            for (int i = 0; i < Колонки.Length; i++) { if (i > 0) _sb.Append(';'); _sb.Append(Колонки[i].имя); }
            _w.WriteLine(_sb.ToString());
            Debug.Log($"[Телеметрия] Запись в CSV: {Путь}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Телеметрия] Не удалось открыть CSV: {e.Message}");
            _w = null; enabled = false;
        }
    }

    void Записать()
    {
        if (_w == null) Открыть();
        if (_w == null) return;
        _sb.Clear();
        for (int i = 0; i < Колонки.Length; i++)
        {
            if (i > 0) _sb.Append(';');
            float v = Колонки[i].знач(this);
            if (!float.IsNaN(v) && !float.IsInfinity(v)) _sb.Append(v.ToString("0.####", Ru));
        }
        _w.WriteLine(_sb.ToString());
    }

    void Закрыть()
    {
        if (_w == null) return;
        try { _w.Flush(); _w.Dispose(); } catch { }
        _w = null;
    }

    void OnDestroy() { Закрыть(); if (Instance == this) Instance = null; }
    void OnApplicationQuit() => Закрыть();
}
