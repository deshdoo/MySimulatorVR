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
    public static float currentDraw_A      = 0.0f;     // полный ток нагрузки, А
    public static float auxBusVoltage_V    = 12.0f;    // вспомогательная шина (электроника), В
    public static float reservePercent     = 100.0f;   // резерв (статический «hardcoded» запас)
    public static SystemState powerState   = SystemState.OK;

    // ---------------- ДВИЖИТЕЛИ ----------------
    public static float thrusterTemp_C     = 25.0f;    // температура движителей, °C
    public static float thrusterPower_W    = 0.0f;     // суммарная электрическая мощность движителей, Вт
    public static SystemState thrusterState = SystemState.OK;

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

    // ---------------- КОНТРОЛЬ ТЕЧИ ----------------
    public static float hullPressure_kPa   = 101.3f;   // давление, действующее на корпус
    public static SystemState leakState    = SystemState.OK;

    // ---------------- ЭХОЛОТ ----------------
    public static float altitudeAboveBottom_m = 0.0f;
    public static bool  hasBottomEcho         = false;

    // ---------------- НАВИГАЦИЯ ----------------
    public static float depth_m            = 0.0f;
    public static float heading_deg        = 0.0f;
    public static float pitch_deg          = 0.0f;
    public static float roll_deg           = 0.0f;
    public static float speed_mps          = 0.0f;

    // ---------------- РЕЖИМ УПРАВЛЕНИЯ ----------------
    public static bool  depthHoldActive    = false;
    public static float depthHoldTarget_m  = 0.0f;

    // ---------------- ТАЙМЕР МИССИИ ----------------
    public static float missionTime_s      = 0.0f;

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
