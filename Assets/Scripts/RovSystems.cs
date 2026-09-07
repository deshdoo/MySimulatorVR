// =============================================================================
// RovSystems.cs
// -----------------------------------------------------------------------------
// Статическая шина состояний бортовых систем НПА.
// Аналог DroneInput, но для телеметрии. Заполняется RovSystemsSimulator,
// читается дисплеями, контроллерами и регистратором данных.
// =============================================================================

using UnityEngine;

public enum SystemState
{
    Off,      // выключено (серый)
    OK,       // норма (зелёный)
    Warning,  // предупреждение (жёлтый)
    Critical, // авария (красный)
    Working   // в работе (циан) — для манипулятора
}

public static class RovSystems
{
    // ---------------- ЭНЕРГОСИСТЕМА ----------------
    public static float batteryCharge_Ah   = 16.0f;    // текущий заряд, А·ч
    public static float batteryVoltage_V   = 25.2f;    // напряжение шины под нагрузкой, В
    public static float batteryPercent     = 100.0f;   // SoC, %
    // currentDraw_A — ПОКАЗАНИЕ датчика тока (шунт/Холл, с погрешностью). Его видят
    // приборы. currentDrawTrue_A — истинный ток (движок), по нему разряжается АКБ:
    // батарея тратит РЕАЛЬНЫЙ ток, а не измеренный. Показание формирует RovCurrentSensor.
    public static float currentDraw_A      = 0.0f;     // полный ток нагрузки, А (показание)
    public static float currentDrawTrue_A  = 0.0f;     // истинный ток нагрузки, А (движок)
    public static float auxBusVoltage_V    = 12.0f;    // вспомогательная шина (электроника), В
    public static float reservePercent     = 100.0f;   // резерв (статический «hardcoded» запас)
    public static SystemState powerState   = SystemState.OK;

    // ---------------- ДВИЖИТЕЛИ ----------------
    public static float thrusterTemp_C     = 25.0f;    // температура движителей, °C
    public static float thrusterPower_W    = 0.0f;     // суммарная электрическая мощность движителей, Вт
    public static SystemState thrusterState = SystemState.OK;  // итоговое (температура + урон)
    public static SystemState thrusterTempState = SystemState.OK; // ТОЛЬКО по температуре (без урона) — для панели

    // ---------------- ПРОЖЕКТОРЫ ----------------
    public static bool lightsOn            = false;
    public static SystemState lightsState  = SystemState.Off;

    // ---------------- КАМЕРА ----------------
    public static SystemState cameraState  = SystemState.OK;

    // ---------------- СВЯЗЬ (умбиликал) ----------------
    public static float communicationDistance_m = 0.0f; // длина выпущенного кабеля от базы
    public static float rssi_dB                  = 0.0f; // условное «качество сигнала»
    public static SystemState communicationState = SystemState.OK;

    // ---------------- МАНИПУЛЯТОР ----------------
    public static SystemState manipulatorState = SystemState.OK;
    public static bool grabberClosed = false;   // клешня сжата (ВКЛ) / разжата (ВЫКЛ) — для панели статуса

    // ---------------- КОНТРОЛЬ ТЕЧИ ----------------
    public static float hullPressure_kPa      = 101.3f;   // ПОКАЗАНИЕ датчика давления (с погрешностью)
    public static float hullPressureTrue_kPa  = 0.0f;     // истинное гидростатическое давление ρgh (для сравнения на графике)
    public static SystemState leakState    = SystemState.OK;

    // ---------------- ЭХОЛОТ ----------------
    // altitudeAboveBottom_m — ПОКАЗАНИЕ эхолота (дальность из времени пробега звука,
    // с погрешностью скорости звука и шумом). altitudeTrue_m — истинная высота над
    // дном (геометрия движка, рейкаст). Показание формирует RovAltimeterSensor.
    // hasBottomEcho — есть ли отражение от дна (в пределах дальности прибора).
    public static float altitudeAboveBottom_m = 0.0f;  // показание, м
    public static float altitudeTrue_m        = 0.0f;  // истина, м
    public static bool  hasBottomEcho         = false;

    // ---------------- НАВИГАЦИЯ ----------------
    // depth_m — ПОКАЗАНИЕ датчика глубины (вычислено из измеренного давления, с погрешностью).
    // depthTrue_m — истинная глубина, которую точно знает движок (для сравнения на графике
    // «истина vs прибор» и для физических расчётов, которым нужна правда, а не показание).
    public static float depth_m            = 0.0f;
    public static float depthTrue_m        = 0.0f;

    // Курс: heading_deg — ПОКАЗАНИЕ курсоуказателя (гироскоп + компас, сведённые
    // комплементарным фильтром). headingTrue_deg — истина (движок). headingGyro_deg —
    // курс по ОДНОМУ гироскопу без коррекции компасом: он дрейфует, и по нему на
    // графике видно, зачем нужна коррекция. Заполняет RovHeadingSensor.
    public static float heading_deg        = 0.0f;
    public static float headingTrue_deg    = 0.0f;
    public static float headingGyro_deg    = 0.0f;
    // Тангаж/крен: pitch_deg/roll_deg — ПОКАЗАНИЕ датчика угла (акселерометр меряет
    // вектор гравитации; при разгоне линейное ускорение подмешивается к наклону).
    // pitchTrue_deg/rollTrue_deg — истина (движок). Показание формирует RovAttitudeSensor.
    public static float pitch_deg          = 0.0f;   // показание, град
    public static float roll_deg           = 0.0f;   // показание, град
    public static float pitchTrue_deg      = 0.0f;   // истина, град
    public static float rollTrue_deg       = 0.0f;   // истина, град
    // Скорость: speed_mps — ПОКАЗАНИЕ доплеровского лага (DVL): работает только при
    // захвате дна, шумит, теряет отсчёт вне дальности. speedTrue_mps — истинный модуль
    // скорости (движок); её читает логика неподвижности миссии (нужна ПРАВДА, не прибор).
    // Показание формирует RovSpeedSensor.
    public static float speed_mps          = 0.0f;   // показание, м/с
    public static float speedTrue_mps      = 0.0f;   // истина, м/с
    public static float currentSpeed_mps   = 0.0f;   // скорость течения в точке аппарата

    // ---------------- РЕЖИМ УПРАВЛЕНИЯ ----------------
    public static bool  depthHoldActive    = false;
    public static float depthHoldTarget_m  = 0.0f;

    // ---------------- ТАЙМЕР МИССИИ ----------------
    public static float missionTime_s      = 0.0f;

    // ---------------- ГОТОВНОСТЬ ТЕЛЕМЕТРИИ ----------------
    // false, пока RovSystemsSimulator не посчитал первый кадр. До этого все
    // поля выше содержат значения по умолчанию (нули), и записывать их в
    // регистратор нельзя — на графике это выглядит как реальный провал в 0.
    public static bool telemetryValid      = false;

    // ---------------- ТОЧКА УПРАВЛЕНИЯ (БАЗА) ----------------
    // Мировые координаты пункта управления (кабины), от которого считается
    // длина умбиликального кабеля для расчёта связи. Записывается скриптом
    // ControlPointMarker, который стоит в сцене Cabin.unity — кабина и НПА
    // (Main.unity) это две разные сцены, объединяемые только во время игры,
    // поэтому прямая ссылка Inspector между ними невозможна, и базовая точка
    // передаётся через эту общую статическую переменную (как и DroneInput).
    public static Vector3 basePosition = Vector3.zero;

    // ---------------- НАКОПИТЕЛЬНЫЙ УРОН (от столкновений) ----------------
    // 0 = нет урона, 1 = Warning, 2 = Critical
    public static int thrusterDamage      = 0;
    public static int cameraDamage        = 0;
    public static int manipulatorDamage   = 0;
    public static int communicationDamage = 0;
    public static int lightsDamage        = 0;

    // Слияние физического состояния с накопленным уроном (берём худшее).
    // Без урона физическое состояние возвращается как есть: Off не должен
    // подменяться на OK только из-за того, что у него меньший ранг в WorseOf.
    public static SystemState ApplyDamage(SystemState physical, int damage)
    {
        if (damage == 0) return physical;

        SystemState damageState = damage == 1 ? SystemState.Warning : SystemState.Critical;
        return WorseOf(physical, damageState);
    }

    static SystemState WorseOf(SystemState a, SystemState b)
    {
        return (int)Rank(a) >= (int)Rank(b) ? a : b;
    }

    static int Rank(SystemState s)
    {
        return s switch
        {
            SystemState.Off      => 0,
            SystemState.OK       => 1,
            SystemState.Working  => 1,
            SystemState.Warning  => 2,
            SystemState.Critical => 3,
            _ => 0
        };
    }
}
