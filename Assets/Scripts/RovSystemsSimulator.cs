using UnityEngine;

// Симулятор бортовых систем НПА.
// Считает каждое FixedUpdate:
//   1. Ток нагрузки и разряд АКБ (закон Фарадея)
//   2. Напряжение шины (модель Тевенена: U = U_open - I*R), U_open — по нелинейной ОЦХ(SoC)
//   3. Вспомогательная шина 12В — модель DC-DC преобразователя (КПД, выход из регулирования при просадке входа)
//   4. Резервная АКБ — отдельный контур заряда/разряда (закон Фарадея)
//   5. Нагрев движителей (тепловой баланс 1-го порядка)
//   6. Связь по умбиликалу (линейное погонное затухание кабеля)
//   7. Гидростатическое давление и контроль течи (P = rho*g*h)
//   8. Эхолот через Physics.Raycast
//   9. Навигационные параметры
//   10. Таймер миссии
public class RovSystemsSimulator : MonoBehaviour
{
    [Header("Навигация")]
    [Tooltip("Мировая координата Y уровня воды. Глубина = ЭтотУровень − Y аппарата. " +
             "Если сцена построена выше нуля, поставь сюда Y поверхности, иначе глубина всегда будет 0.")]
    public float УровеньПоверхностиВоды = 0f;

    [Header("Аккумуляторная батарея")]
    [Tooltip("Номинальная ёмкость, А·ч")]
    public float ЁмкостьАКБ = 16f;
    [Tooltip("Напряжение полного заряда, В")]
    public float НапряжениеПолногоЗаряда = 25.2f;
    [Tooltip("Напряжение разряженной АКБ, В")]
    public float НапряжениеРазряженной = 21.0f;
    [Tooltip("Внутреннее сопротивление АКБ, Ом")]
    public float ВнутреннееСопротивление = 0.05f;

    [Header("Токи потребления")]
    [Tooltip("Ток в режиме ожидания, А")]
    public float ТокОжидания = 0.8f;
    [Tooltip("Суммарный ток ВСЕХ движителей на полной тяге (все оси одновременно), А. " +
             "Ориентир — Blue Robotics T200 при 16 В: ~17 А на полном ходу, крейсер ~8 А. " +
             "60 А на 6 движителей ≈ 10 А на движитель; из-за закона P∝T^1.5 мягкий крейсер " +
             "(~30% хода) берёт ~7 А — согласуется с паспортным крейсерским током.")]
    public float ТокДвижителейМакс = 60f;
    [Tooltip("Сколько движителей создают горизонтальную тягу (ход вперёд и рысканье). " +
             "В векторной схеме малого ROV это одни и те же 4 движителя.")]
    public int ГоризонтальныхДвижителей = 4;
    [Tooltip("Сколько движителей создают вертикальную тягу")]
    public int ВертикальныхДвижителей = 2;
    [Tooltip("Показатель степени в связи мощности с тягой. У гребного винта тяга T ∝ n², " +
             "мощность P ∝ n³, отсюда P ∝ T^1.5. Значение 1 дало бы нефизичную линейную связь.")]
    public float ПоказательМощностиОтТяги = 1.5f;
    [Tooltip("Ток одного прожектора, А")]
    public float ТокПрожектора = 1.5f;
    [Tooltip("Количество прожекторов")]
    public int КоличествоПрожекторов = 2;

    [Header("Тепловой режим движителей")]
    [Tooltip("Температура окружающей воды, °C")]
    public float ТемператураВоды = 6f;
    [Tooltip("Электрический КПД движителя")]
    [Range(0.1f, 1f)] public float КПДДвижителя = 0.65f;
    [Tooltip("Теплоёмкость движителя, Дж/К")]
    public float Теплоёмкость = 250f;
    [Tooltip("Коэффициент теплоотдачи, Вт/К")]
    public float КоэффТеплоотдачи = 6f;
    [Tooltip("Температура предупреждения, °C")]
    public float ПорогПредупреждения = 80f;
    [Tooltip("Аварийная температура, °C")]
    public float ПорогАварии = 100f;

    [Header("Связь по умбиликалу")]
    [Tooltip("Погонное затухание кабеля, дБ на метр. Затухание в проводном/коаксиальном " +
             "умбиликале линейно по длине (A = α·L), в отличие от радио в свободном " +
             "пространстве. 0.1 дБ/м — характерная величина для коаксиала на видеочастотах; " +
             "у оптоволокна на два-три порядка меньше (тогда предел задают потери на разъёмах).")]
    public float УдельноеЗатуханиеКабеля_дБм = 0.1f;
    [Tooltip("Постоянные потери на разъёмах/вводах кабеля, дБ (не зависят от длины)")]
    public float ПотериНаРазъёмах_дБ = 1f;
    [Tooltip("Точка базы — диспетчерская")]
    public Transform База;
    [Tooltip("RSSI предупреждения, дБ")]
    public float ПорогRSSIПредупр = -20f;
    [Tooltip("RSSI аварии, дБ")]
    public float ПорогRSSIАвария = -30f;

    [Header("Гидростатика")]
    [Tooltip("Плотность воды, кг/м³")]
    public float ПлотностьВоды = 1025f;
    [Tooltip("Максимальная рабочая глубина, м")]
    public float МаксГлубина = 300f;

    [Header("Пороги АКБ")]
    [Tooltip("Напряжение предупреждения, В")]
    public float ПорогНапряженияПредупр = 22.5f;
    [Tooltip("Напряжение аварии, В")]
    public float ПорогНапряженияАвария = 21.5f;

    [Header("Эхолот")]
    [Tooltip("Максимальная дальность эхолота, м")]
    public float ДальностьЭхолота = 50f;

    [Header("Нелинейная ОЦХ АКБ (форма зависимости U_хх от SoC)")]
    [Tooltip("Нормированная кривая: вход — SoC (0..1), выход — доля напряжения (0..1) между «разряжена» и «полный заряд». " +
             "Если кривая пуста — в Awake подставляется типовая Li-ion форма (плато до ~20% SoC, затем резкий спад).")]
    public AnimationCurve ФормаРазрядаОЦХ = new AnimationCurve();

    [Header("Вспомогательная шина (DC-DC преобразователь 25В -> 12В)")]
    [Tooltip("Номинальное регулируемое напряжение шины электроники, В")]
    public float АуксНапряжениеНоминал = 12.0f;
    [Tooltip("КПД DC-DC преобразователя")]
    [Range(0.5f, 1f)] public float АуксКПД = 0.85f;
    [Tooltip("Ток нагрузки электроники на шине 12В, А")]
    public float АуксТокНагрузки = 0.5f;
    [Tooltip("Напряжение выпадения из регулирования (dropout), В — насколько входное напряжение должно превышать номинал 12В, чтобы держать стабильные 12В")]
    public float АуксПадениеРегулирования = 1.5f;

    [Header("Резервная АКБ")]
    [Tooltip("Ёмкость резервной АКБ, А·ч")]
    public float ЁмкостьРезерв = 4.0f;
    [Tooltip("Ток критичных потребителей при работе от резерва (после отказа главной шины), А")]
    public float ТокРезервнойНагрузки = 0.3f;
    [Tooltip("Ток подзарядки резерва от главной шины, когда она исправна, А")]
    public float ТокПодзарядкиРезерва = 0.5f;

    private Rigidbody _rb;
    private float _reserveCharge_Ah;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();

        RovSystems.batteryCharge_Ah = ЁмкостьАКБ;
        RovSystems.batteryVoltage_V = НапряжениеПолногоЗаряда;
        RovSystems.batteryPercent = 100f;
        RovSystems.thrusterTemp_C = ТемператураВоды + 5f;
        RovSystems.missionTime_s = 0f;
        RovSystems.auxBusVoltage_V = АуксНапряжениеНоминал;

        _reserveCharge_Ah = ЁмкостьРезерв;
        RovSystems.reservePercent = 100f;
        RovSystems.telemetryValid = false;   // станет true после первого FixedUpdate

        // Сброс накопленного урона: RovSystems — статический класс, и при
        // выключенном Domain Reload его поля переживают перезапуск Play, из-за
        // чего аппарат стартовал бы с повреждениями от прошлой сессии. Обнуляем,
        // чтобы каждый запуск начинался с исправного НПА.
        RovSystems.thrusterDamage = 0;
        RovSystems.cameraDamage = 0;
        RovSystems.manipulatorDamage = 0;
        RovSystems.communicationDamage = 0;
        RovSystems.lightsDamage = 0;

        // Частая ошибка настройки: сцена построена выше уровня воды, тогда глубина
        // всё время зажата в 0 и приборы/графики выглядят «сломанными». Сообщаем сразу.
        float стартоваяГлубина = УровеньПоверхностиВоды - transform.position.y;
        if (стартоваяГлубина < 0f)
            Debug.LogWarning($"[RovSystemsSimulator] НПА стартует ВЫШЕ уровня воды (Y аппарата = {transform.position.y:F1}, " +
                             $"уровень воды = {УровеньПоверхностиВоды:F1}) — глубина будет всё время 0. " +
                             $"Поставь «Уровень поверхности воды» примерно в {transform.position.y:F0} или выше.", this);

        if (ФормаРазрядаОЦХ == null || ФормаРазрядаОЦХ.length == 0)
        {
            // Типовая форма разряда Li-ion: пологое плато при высоком SoC,
            // резкий спад напряжения после ~20% заряда.
            ФормаРазрядаОЦХ = new AnimationCurve(
                new Keyframe(0.0f, 0.0f),
                new Keyframe(0.2f, 0.62f),
                new Keyframe(0.5f, 0.78f),
                new Keyframe(0.8f, 0.90f),
                new Keyframe(1.0f, 1.0f));
        }
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // 1. Навигация
        Vector3 pos = transform.position;
        // ИСТИННАЯ глубина — то, что движок знает точно. Показание датчика depth_m
        // формирует уже RovDepthSensor (давление → погрешности → обратный расчёт).
        // Здесь ставим depth_m = истине как безопасное значение по умолчанию: если
        // компонента-датчика в сцене нет, всё работает ровно как раньше.
        RovSystems.depthTrue_m = Mathf.Max(0f, УровеньПоверхностиВоды - pos.y);
        RovSystems.depth_m = RovSystems.depthTrue_m;
        Vector3 e = transform.eulerAngles;
        // ИСТИННЫЙ курс — то, что движок знает точно. Показание курсоуказателя
        // heading_deg формирует RovHeadingSensor (гироскоп с дрейфом + компас +
        // фильтр). По умолчанию (без датчика в сцене) показание = истине.
        RovSystems.headingTrue_deg = e.y;
        RovSystems.heading_deg = e.y;
        RovSystems.pitch_deg = NormalizeAngle(e.x);
        RovSystems.roll_deg = NormalizeAngle(e.z);
        RovSystems.speed_mps = _rb != null ? _rb.linearVelocity.magnitude : 0f;

        // 2. Ток нагрузки движителей.
        // Считаем по ГРУППАМ движителей, а не по «средней команде»: ход вперёд и
        // рысканье выполняют одни и те же горизонтальные движители (их спрос
        // складывается и упирается в предел), а вертикальные работают независимо.
        //
        // Внутри группы мощность связана с тягой нелинейно: у гребного винта
        // T ∝ n², P ∝ n³, значит P ∝ T^1.5. Поэтому на половинной тяге движитель
        // потребляет ≈0.35 от максимума, а не половину.
        float спросГоризонт  = Mathf.Clamp01(Mathf.Abs(DroneInput.forward)
                                           + Mathf.Abs(DroneInput.lateral)
                                           + Mathf.Abs(DroneInput.yaw));
        float спросВертикаль = Mathf.Clamp01(Mathf.Abs(DroneInput.vertical));

        int всегоДвижителей = Mathf.Max(1, ГоризонтальныхДвижителей + ВертикальныхДвижителей);
        float I_горизонт  = ТокДвижителейМакс * ГоризонтальныхДвижителей / всегоДвижителей;
        float I_вертикаль = ТокДвижителейМакс * ВертикальныхДвижителей  / всегоДвижителей;

        float I_thrusters = I_горизонт  * Mathf.Pow(спросГоризонт,  ПоказательМощностиОтТяги)
                          + I_вертикаль * Mathf.Pow(спросВертикаль, ПоказательМощностиОтТяги);
        float I_lights = RovSystems.lightsOn ? КоличествоПрожекторов * ТокПрожектора : 0f;

        // Ток, потребляемый DC-DC преобразователем 12В-шины от главной батареи:
        // P_вход = P_выход / КПД, I_вход = P_вход / U_главной_шины (по напряжению прошлого кадра).
        float P_auxOut = АуксНапряжениеНоминал * АуксТокНагрузки;
        float U_mainPrev = Mathf.Max(RovSystems.batteryVoltage_V, 1f);
        float I_aux = (P_auxOut / АуксКПД) / U_mainPrev;

        float I_total = ТокОжидания + I_thrusters + I_lights + I_aux;
        RovSystems.currentDraw_A = I_total;

        // 3. Разряд АКБ (закон Фарадея)
        RovSystems.batteryCharge_Ah = Mathf.Max(0f,
            RovSystems.batteryCharge_Ah - I_total * dt / 3600f);
        RovSystems.batteryPercent = (RovSystems.batteryCharge_Ah / ЁмкостьАКБ) * 100f;

        // 4. Напряжение под нагрузкой (Тевенен), U_хх берётся по нелинейной ОЦХ(SoC)
        float soc = RovSystems.batteryPercent * 0.01f;
        float ocvFraction = ФормаРазрядаОЦХ.Evaluate(soc);
        float Uopen = Mathf.Lerp(НапряжениеРазряженной, НапряжениеПолногоЗаряда, ocvFraction);
        RovSystems.batteryVoltage_V = Mathf.Max(0f,
            Uopen - I_total * ВнутреннееСопротивление);

        if (RovSystems.batteryVoltage_V < ПорогНапряженияАвария || RovSystems.batteryPercent < 10f)
            RovSystems.powerState = SystemState.Critical;
        else if (RovSystems.batteryVoltage_V < ПорогНапряженияПредупр || RovSystems.batteryPercent < 25f)
            RovSystems.powerState = SystemState.Warning;
        else
            RovSystems.powerState = SystemState.OK;

        // 4.1 Вспомогательная шина: DC-DC держит регулировку, пока входное
        // напряжение превышает номинал + dropout, иначе выходит из режима
        // стабилизации и проседает вместе с главной шиной.
        if (RovSystems.batteryVoltage_V >= АуксНапряжениеНоминал + АуксПадениеРегулирования)
            RovSystems.auxBusVoltage_V = АуксНапряжениеНоминал;
        else
            RovSystems.auxBusVoltage_V = Mathf.Max(0f, RovSystems.batteryVoltage_V - АуксПадениеРегулирования);

        // 4.2 Резервная АКБ: подзаряжается от исправной главной шины,
        // разряжается на критичные потребители только при отказе главной (закон Фарадея).
        if (RovSystems.powerState == SystemState.Critical)
        {
            _reserveCharge_Ah = Mathf.Max(0f,
                _reserveCharge_Ah - ТокРезервнойНагрузки * dt / 3600f);
        }
        else
        {
            _reserveCharge_Ah = Mathf.Min(ЁмкостьРезерв,
                _reserveCharge_Ah + ТокПодзарядкиРезерва * dt / 3600f);
        }
        RovSystems.reservePercent = (_reserveCharge_Ah / ЁмкостьРезерв) * 100f;

        // 5. Нагрев движителей
        float P_in = RovSystems.batteryVoltage_V * I_thrusters;
        float P_loss = P_in * (1f - КПДДвижителя);
        float dTdt = (P_loss - КоэффТеплоотдачи *
                     (RovSystems.thrusterTemp_C - ТемператураВоды)) / Теплоёмкость;
        RovSystems.thrusterTemp_C += dTdt * dt;
        RovSystems.thrusterPower_W = P_in;

        SystemState thrusterPhys;
        if (RovSystems.thrusterTemp_C > ПорогАварии)            thrusterPhys = SystemState.Critical;
        else if (RovSystems.thrusterTemp_C > ПорогПредупреждения) thrusterPhys = SystemState.Warning;
        else                                                     thrusterPhys = SystemState.OK;
        RovSystems.thrusterTempState = thrusterPhys;  // только температура — для панели (отделить нагрев от урона)
        RovSystems.thrusterState = RovSystems.ApplyDamage(thrusterPhys, RovSystems.thrusterDamage);

        // 6. Прожекторы
        SystemState lightsPhys = RovSystems.lightsOn ? SystemState.OK : SystemState.Off;
        RovSystems.lightsState = RovSystems.ApplyDamage(lightsPhys, RovSystems.lightsDamage);

        // 7. Связь
        // Если в сцене есть физический трос (UmbilicalCable — Verlet-цепочка,
        // см. Docs/Отчет_Физика_НПА.md), используем его фактическую длину
        // (длину провисающего кабеля, а не прямую дистанцию) — это физически
        // корректнее, так как провисающий трос всегда длиннее прямой линии
        // между его концами. Без троса в сцене (или до его добавления)
        // используем прямую дистанцию как раньше — запасной вариант.
        float L;
        if (UmbilicalCable.Instance != null)
        {
            L = UmbilicalCable.Instance.CableLength;
            // Защита от разлёта joint-цепи троса: длина физически не может
            // превышать полностью вытравленный кабель. Если солвер на миг
            // «взорвал» звено (NaN/бесконечность/гигантское значение) — не
            // пускаем мусор на приборы (иначе RSSI улетает в дикий минус).
            float maxL = UmbilicalCable.Instance.totalCableLength_m * 1.5f;
            if (float.IsNaN(L) || float.IsInfinity(L) || L > maxL) L = maxL;
        }
        else
        {
            // "База" заполняется, только если она назначена вручную в этой же сцене;
            // если нет (как сейчас — кабина в отдельной сцене Cabin.unity, см. ControlPointMarker),
            // берём точку управления из общей переменной RovSystems.basePosition.
            Vector3 baseP = База != null ? База.position : RovSystems.basePosition;
            L = Vector3.Distance(pos, baseP);
        }
        RovSystems.communicationDistance_m = L;
        // Погонное затухание проводного кабеля: потери в дБ линейны по длине
        // (A = α·L + потери_на_разъёмах), а не логарифмичны, как у радиоканала
        // в свободном пространстве (закон Фрииса). RSSI здесь — уровень сигнала
        // относительно уровня на выходе передатчика (0 дБ у самого разъёма минус
        // постоянные потери ввода), поэтому он отрицателен и убывает линейно.
        if (L < 0f) L = 0f;
        RovSystems.rssi_dB = -(УдельноеЗатуханиеКабеля_дБм * L + ПотериНаРазъёмах_дБ);

        SystemState commPhys;
        if (RovSystems.rssi_dB < ПорогRSSIАвария)      commPhys = SystemState.Critical;
        else if (RovSystems.rssi_dB < ПорогRSSIПредупр) commPhys = SystemState.Warning;
        else                                            commPhys = SystemState.OK;
        RovSystems.communicationState = RovSystems.ApplyDamage(commPhys, RovSystems.communicationDamage);

        // 8. Камера
        RovSystems.cameraState = RovSystems.ApplyDamage(SystemState.OK, RovSystems.cameraDamage);

        // 8.1. Манипулятор: зелёный (OK) когда клешня сжата (ВКЛ), серый (Off) когда разжата.
        // Урон, как и у прочих систем, перекрывает цвет на жёлтый/красный.
        SystemState manipPhys = RovSystems.grabberClosed ? SystemState.OK : SystemState.Off;
        RovSystems.manipulatorState = RovSystems.ApplyDamage(manipPhys, RovSystems.manipulatorDamage);

        // 9. Эхолот
        RaycastHit hit;
        if (Physics.Raycast(pos, Vector3.down, out hit, ДальностьЭхолота))
        {
            RovSystems.altitudeAboveBottom_m = hit.distance;
            RovSystems.hasBottomEcho = true;
        }
        else
        {
            RovSystems.altitudeAboveBottom_m = ДальностьЭхолота;
            RovSystems.hasBottomEcho = false;
        }

        // 10. Гидростатическое давление и течь
        // ИСТИННОЕ давление на корпус P = ρ·g·h (по истинной глубине) — это физика,
        // а не показание прибора. Датчик давления (RovDepthSensor) возьмёт эту истину,
        // добавит погрешности и запишет своё показание в hullPressure_kPa. По умолчанию
        // (без датчика в сцене) показание = истине.
        float P_water_Pa = ПлотностьВоды * 9.81f * RovSystems.depthTrue_m;
        RovSystems.hullPressureTrue_kPa = P_water_Pa * 0.001f;
        RovSystems.hullPressure_kPa = RovSystems.hullPressureTrue_kPa;

        // Ранг корпуса по глубине считаем от ИСТИННОЙ глубины: прочность корпуса
        // не зависит от того, что показывает прибор.
        float depthRatio = RovSystems.depthTrue_m / МаксГлубина;
        if (depthRatio > 1.0f) RovSystems.leakState = SystemState.Critical;
        else if (depthRatio > 0.9f) RovSystems.leakState = SystemState.Warning;
        else RovSystems.leakState = SystemState.OK;

        // 11. Таймер
        RovSystems.missionTime_s += dt;

        // Все поля посчитаны — с этого момента телеметрию можно писать в графики.
        RovSystems.telemetryValid = true;
    }

    static float NormalizeAngle(float a)
    {
        a %= 360f;
        if (a > 180f) a -= 360f;
        return a;
    }
}
