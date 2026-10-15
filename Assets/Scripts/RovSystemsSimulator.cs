using UnityEngine;

// Симулятор бортовых систем НПА (класс «Марлин-350»: питание и связь — по кабелю-тросу с судна).
// Считает каждое FixedUpdate:
//   1. Мощность нагрузки (движители P∝T^1.5, свет, электроника, инструмент)
//   2. Передача мощности по жилам кабеля: падение напряжения I·R, потери I²R,
//      предел мощности судового блока 3 кВт (паспорт) → ограничение тяги
//   3. Вспомогательная шина 12В — модель DC-DC преобразователя
//   4. Резервная АКБ (единственная на борту) — питает электронику при обрыве кабеля (закон Фарадея)
//   5. Нагрев движителей (тепловой баланс 1-го порядка)
//   6. Связь по оптоволокну в кабеле (затухание ~0,35 дБ/км: на сотнях метров — «есть/обрыв»)
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

    [Header("Питание по кабелю-тросу (паспорт «Марлин-350»: 220 В, 3 кВт с судна)")]
    [Tooltip("Напряжение, которое надводный блок питания подаёт в кабель, В. Паспорт даёт 220 В на ВХОДЕ " +
             "надводного блока; по кабелю передают повышенное постоянное напряжение, чтобы снизить ток и " +
             "потери в тонких жилах. 600 В — допущение (типично для ТНПА этого класса).")]
    public float НапряжениеЛинии_В = 600f;
    [Tooltip("Сечение одной токопроводящей жилы кабеля, мм² (допущение для оптоэлектрического кабеля Ø15 мм).")]
    public float СечениеЖилы_мм2 = 1.5f;
    [Tooltip("Удельное сопротивление меди, Ом·мм²/м.")]
    public float УдельноеСопротивление = 0.0175f;
    [Tooltip("Предельная мощность, которую судовой блок отдаёт в кабель, Вт (паспорт: потребляемая мощность 3 кВт).")]
    public float ПредельнаяМощность_Вт = 3000f;
    [Tooltip("КПД бортового преобразователя (линия → шина движителей и электроники).")]
    [Range(0.5f, 1f)] public float КПДПреобразователя = 0.92f;
    [Tooltip("Разрывное усилие кабеля, Н. Натяжение выше — обрыв: нет питания и связи, аппарат всплывает на резерве.")]
    public float РазрывноеУсилие_Н = 2500f;

    [Header("Мощности потребителей")]
    [Tooltip("Электрическая мощность ОДНОГО движителя на полной тяге, Вт.")]
    public float МощностьДвижителяМакс_Вт = 400f;
    [Tooltip("Сколько движителей создают горизонтальную тягу (векторная схема — одни и те же 4).")]
    public int ГоризонтальныхДвижителей = 4;
    [Tooltip("Сколько движителей создают вертикальную тягу")]
    public int ВертикальныхДвижителей = 2;
    [Tooltip("Показатель степени в связи мощности с тягой. У гребного винта тяга T ∝ n², " +
             "мощность P ∝ n³, отсюда P ∝ T^1.5. Значение 1 дало бы нефизичную линейную связь.")]
    public float ПоказательМощностиОтТяги = 1.5f;
    [Tooltip("Мощность одного светильника, Вт")]
    public float МощностьПрожектора_Вт = 100f;
    [Tooltip("Количество светильников (паспорт: два)")]
    public int КоличествоПрожекторов = 2;
    [Tooltip("Мощность электроники (камеры, гидролокатор, навигация, телеметрия), Вт")]
    public float МощностьЭлектроники_Вт = 150f;

    [Header("Тепловой режим движителей")]
    [Tooltip("Температура окружающей воды, °C")]
    public float ТемператураВоды = 6f;
    [Tooltip("Электрический КПД движителя")]
    [Range(0.1f, 1f)] public float КПДДвижителя = 0.65f;
    [Tooltip("Теплоёмкость группы движителей, Дж/К")]
    public float ТеплоёмкостьДвижителей_ДжК = 600f;
    [Tooltip("Коэффициент теплоотдачи группы движителей в воду, Вт/К (полный ход по всем осям → ~90 °C, предупреждение)")]
    public float ТеплоотдачаДвижителей_ВтК = 10f;
    [Tooltip("Температура предупреждения, °C")]
    public float ПорогПредупреждения = 80f;
    [Tooltip("Аварийная температура, °C")]
    public float ПорогАварии = 100f;

    [Header("Связь по оптоволокну в кабеле-тросе")]
    [Tooltip("Затухание оптоволокна, дБ на километр (одномод ~0,35 дБ/км). На 450 м кабеля «Марлина» это " +
             "доли дБ — сигнал не слабеет с длиной, как у медного провода, связь либо есть, либо обрыв.")]
    public float ЗатуханиеОптики_дБкм = 0.35f;
    [Tooltip("Потери на оптических разъёмах и вращающемся переходе лебёдки, дБ")]
    public float ПотериНаРазъёмахОптики_дБ = 1.5f;
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

    [Header("Пороги напряжения на аппарате (доля от напряжения линии)")]
    [Tooltip("Ниже этой доли — предупреждение (большое падение в кабеле)")]
    public float ДоляНапряженияПредупр = 0.85f;
    [Tooltip("Ниже этой доли — авария")]
    public float ДоляНапряженияАвария = 0.65f;

    [Header("Эхолот")]
    [Tooltip("Максимальная дальность эхолота, м")]
    public float ДальностьЭхолота = 50f;

    [Header("Вспомогательная шина (DC-DC преобразователь бортовой шины -> 12В)")]
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
    [Tooltip("Ток критичных потребителей при работе от резерва (после потери питания по кабелю), А")]
    public float ТокРезервнойНагрузки = 1.5f;
    [Tooltip("Ток подзарядки резерва от главной шины, когда она исправна, А")]
    public float ТокПодзарядкиРезерва = 0.5f;

    private Rigidbody _rb;
    private float _reserveCharge_Ah;

    // Заряд резервной АКБ, А·ч — для сохранения/продолжения миссии.
    public float ЗарядРезерва_Ач
    {
        get => _reserveCharge_Ah;
        set { _reserveCharge_Ah = Mathf.Clamp(value, 0f, ЁмкостьРезерв); RovSystems.reservePercent = _reserveCharge_Ah / ЁмкостьРезерв * 100f; }
    }

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();

        RovSystems.batteryCharge_Ah = ЁмкостьРезерв;
        RovSystems.batteryVoltage_V = НапряжениеЛинии_В;
        RovSystems.batteryPercent = 100f;
        RovSystems.powerLimitFactor = 1f;
        RovSystems.cableBroken = false;
        RovSystems.toolPower_W = 0f;
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

    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // 1. Навигация
        // Позиция и поворот — ФИЗИЧЕСКИЕ (Rigidbody), а не Transform: у аппарата включена
        // интерполяция, и в FixedUpdate Transform хранит сглаженную для экрана позу
        // прошлого кадра — она отстаёт от физики. Датчики, читающие истину отсюда,
        // получали бы лишнее запаздывание (заметно в контуре автопилота глубины).
        Vector3 pos = _rb != null ? _rb.position : transform.position;
        // ИСТИННАЯ глубина — то, что движок знает точно. Показание датчика depth_m
        // формирует уже RovDepthSensor (давление → погрешности → обратный расчёт).
        // Здесь ставим depth_m = истине как безопасное значение по умолчанию: если
        // компонента-датчика в сцене нет, всё работает ровно как раньше.
        RovSystems.уровеньПоверхности_Y = УровеньПоверхностиВоды;
        RovSystems.depthTrue_m = Mathf.Max(0f, УровеньПоверхностиВоды - pos.y);
        RovSystems.depth_m = RovSystems.depthTrue_m;
        Vector3 e = (_rb != null ? _rb.rotation : transform.rotation).eulerAngles;
        // ИСТИННЫЙ курс — то, что движок знает точно. Показание курсоуказателя
        // heading_deg формирует RovHeadingSensor (гироскоп с дрейфом + компас +
        // фильтр). По умолчанию (без датчика в сцене) показание = истине.
        RovSystems.headingTrue_deg = e.y;
        RovSystems.heading_deg = e.y;
        // ИСТИННЫЕ тангаж/крен — движок знает точно. Показания pitch_deg/roll_deg
        // формирует RovAttitudeSensor (акселерометр: гравитация + шум + ошибка от
        // ускорения). По умолчанию (без датчика в сцене) показание = истине.
        RovSystems.pitchTrue_deg = NormalizeAngle(e.x);
        RovSystems.rollTrue_deg  = NormalizeAngle(e.z);
        RovSystems.pitch_deg = RovSystems.pitchTrue_deg;
        RovSystems.roll_deg  = RovSystems.rollTrue_deg;
        // ИСТИННАЯ скорость — модуль вектора скорости из физики. Показание speed_mps
        // формирует RovSpeedSensor (доплеровский лаг). По умолчанию показание = истине.
        RovSystems.speedTrue_mps = _rb != null ? _rb.linearVelocity.magnitude : 0f;
        RovSystems.speed_mps = RovSystems.speedTrue_mps;

        // 2. Мощность нагрузки на борту.
        // Движители — по ГРУППАМ (горизонтальные: ход, лаг и рысканье делают одни и те же
        // 4 винта векторной схемы; вертикальные — отдельно). Внутри группы мощность растёт
        // как T^1.5 (у винта T ∝ n², P ∝ n³): на половинной тяге ≈0,35 от максимума.
        float спросГоризонт  = Mathf.Clamp01(Mathf.Abs(DroneInput.forward)
                                           + Mathf.Abs(DroneInput.lateral)
                                           + Mathf.Abs(DroneInput.yaw));
        float спросВертикаль = Mathf.Clamp01(Mathf.Abs(DroneInput.vertical));
        float P_движМакс = МощностьДвижителяМакс_Вт * ГоризонтальныхДвижителей * Mathf.Pow(спросГоризонт, ПоказательМощностиОтТяги)
                         + МощностьДвижителяМакс_Вт * ВертикальныхДвижителей  * Mathf.Pow(спросВертикаль, ПоказательМощностиОтТяги);
        float P_прочее = (RovSystems.lightsOn ? КоличествоПрожекторов * МощностьПрожектора_Вт : 0f)
                       + МощностьЭлектроники_Вт + Mathf.Max(0f, RovSystems.toolPower_W);

        // Обрыв кабеля: натяжение у заделки выше разрывного (UmbilicalCable считает натяжение).
        if (!RovSystems.cableBroken && UmbilicalCable.Instance != null && UmbilicalCable.Instance.Натяжение_Н > РазрывноеУсилие_Н)
        {
            RovSystems.cableBroken = true;
            Debug.LogWarning($"[Питание] ОБРЫВ КАБЕЛЯ: натяжение {UmbilicalCable.Instance.Натяжение_Н:F0} Н > {РазрывноеУсилие_Н:F0} Н. " +
                             "Нет питания движителей и связи — аппарат всплывает на положительной плавучести, электроника на резерве.");
        }

        float R = СопротивлениеКабеля();
        float U0 = НапряжениеЛинии_В;
        float kТяги = 1f, I_line = 0f, U_rov = 0f, P_сСудна = 0f;
        if (!RovSystems.cableBroken)
        {
            // 2.1 Предел мощности судового блока. Если нагрузка с потерями в кабеле больше
            // 3 кВт — блок управления урезает тягу всех движителей одинаково (направление
            // тяги сохраняется). Мощность движителей ∝ k^1.5 (P ∝ T^1.5) — ищем k бисекцией.
            if (!Передать(P_движМакс + P_прочее, R, U0, out I_line, out U_rov) || U0 * I_line > ПредельнаяМощность_Вт)
            {
                float lo = 0f, hi = 1f;
                for (int i = 0; i < 16; i++)
                {
                    float mid = 0.5f * (lo + hi);
                    bool ok = Передать(P_движМакс * Mathf.Pow(mid, ПоказательМощностиОтТяги) + P_прочее, R, U0, out float Ii, out _)
                              && U0 * Ii <= ПредельнаяМощность_Вт;
                    if (ok) lo = mid; else hi = mid;
                }
                kТяги = lo;
                Передать(P_движМакс * Mathf.Pow(kТяги, ПоказательМощностиОтТяги) + P_прочее, R, U0, out I_line, out U_rov);
            }
            P_сСудна = U0 * I_line;
        }
        else
        {
            kТяги = 0f;
        }
        float P_движ = P_движМакс * Mathf.Pow(kТяги, ПоказательМощностиОтТяги);
        RovSystems.powerLimitFactor = kТяги;
        RovSystems.linePower_W = P_сСудна;
        RovSystems.linePowerLimit_W = ПредельнаяМощность_Вт;
        RovSystems.cableLoss_W = I_line * I_line * R;

        // ИСТИННЫЙ ток в кабеле — по нему считаются потери и падение напряжения. Показание
        // currentDraw_A формирует RovCurrentSensor (шунт/Холл с погрешностью).
        RovSystems.currentDrawTrue_A = I_line;
        RovSystems.currentDraw_A = I_line;
        // «Главная шина» на пульте — напряжение, дошедшее до аппарата по кабелю.
        RovSystems.batteryVoltage_V = U_rov;

        float доляНапр = U0 > 0f ? U_rov / U0 : 0f;
        if (RovSystems.cableBroken || доляНапр < ДоляНапряженияАвария || kТяги < 0.5f)
            RovSystems.powerState = SystemState.Critical;
        else if (доляНапр < ДоляНапряженияПредупр || kТяги < 0.999f)
            RovSystems.powerState = SystemState.Warning;   // упёрлись в предел мощности — тяга урезана
        else
            RovSystems.powerState = SystemState.OK;

        // 3. Вспомогательная шина 12 В: от линии через DC-DC, при обрыве — от резервной АКБ.
        RovSystems.auxBusVoltage_V = (!RovSystems.cableBroken || _reserveCharge_Ah > 0f) ? АуксНапряжениеНоминал : 0f;

        // 4. Резервная АКБ (закон Фарадея): при обрыве питает критичную электронику
        // (навигация, маяк, телеметрия), пока есть линия — подзаряжается.
        if (RovSystems.cableBroken)
            _reserveCharge_Ah = Mathf.Max(0f, _reserveCharge_Ah - ТокРезервнойНагрузки * dt / 3600f);
        else
            _reserveCharge_Ah = Mathf.Min(ЁмкостьРезерв, _reserveCharge_Ah + ТокПодзарядкиРезерва * dt / 3600f);
        RovSystems.reservePercent = (_reserveCharge_Ah / ЁмкостьРезерв) * 100f;
        // «Заряд» на пульте — единственная батарея на борту, резервная.
        RovSystems.batteryCharge_Ah = _reserveCharge_Ah;
        RovSystems.batteryPercent = RovSystems.reservePercent;

        // 5. Нагрев движителей: в тепло уходит (1 − КПД) электрической мощности.
        float P_loss = P_движ * (1f - КПДДвижителя);
        float dTdt = (P_loss - ТеплоотдачаДвижителей_ВтК *
                     (RovSystems.thrusterTemp_C - ТемператураВоды)) / ТеплоёмкостьДвижителей_ДжК;
        RovSystems.thrusterTemp_C += dTdt * dt;
        RovSystems.thrusterPower_W = P_движ;

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
        // Оптоволокно: затухание ~0,35 дБ/км + потери на разъёмах. На длинах кабеля ТНПА
        // это доли дБ, поэтому уровень сигнала почти не зависит от длины (в отличие от
        // медной линии) — связь пропадает только при обрыве.
        if (L < 0f) L = 0f;
        RovSystems.rssi_dB = RovSystems.cableBroken ? -100f
                           : -(ЗатуханиеОптики_дБкм * L / 1000f + ПотериНаРазъёмахОптики_дБ);

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

        // 9. Эхолот. ИСТИННАЯ высота над дном — геометрия (рейкаст вниз). Показание
        // altitudeAboveBottom_m формирует RovAltimeterSensor (дальность из времени
        // пробега звука: ошибка скорости звука + шум). По умолчанию показание = истине.
        // Луч фильтруется (DvlJanus.ЛучДоДна): раньше голый Physics.Raycast упирался в
        // СОБСТВЕННЫЙ КАБЕЛЬ под аппаратом — высота выходила ~полметра, а придонный слой
        // течения (WaterCurrent) гасил поток ниже аппарата до нуля.
        if (DvlJanus.ЛучДоДна(transform, _rb, pos, Vector3.down, ДальностьЭхолота, out float доДна))
        {
            RovSystems.altitudeTrue_m = доДна;
            RovSystems.altitudeAboveBottom_m = доДна;
            RovSystems.hasBottomEcho = true;
        }
        else
        {
            RovSystems.altitudeTrue_m = ДальностьЭхолота;
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

    // Сопротивление двух жил кабеля (туда и обратно), Ом: R = ρ·2L/S. Длина — фактическая
    // длина вытравленного кабеля (UmbilicalCable), иначе — прямая до базы.
    float СопротивлениеКабеля()
    {
        float L = RovSystems.communicationDistance_m;
        return УдельноеСопротивление * 2f * Mathf.Max(1f, L) / Mathf.Max(0.05f, СечениеЖилы_мм2);
    }

    // Передача мощности P_борт (Вт, на борту после преобразователя) по кабелю с
    // сопротивлением R от источника U0. На входе преобразователя нужна P_вх = P_борт/КПД,
    // ток I находится из U0·I − R·I² = P_вх (корень с меньшим током — рабочий режим).
    // Если дискриминант < 0 — линия физически не может отдать столько (P_вх > U0²/4R).
    bool Передать(float P_борт, float R, float U0, out float I, out float U_rov)
    {
        float P_вх = P_борт / КПДПреобразователя;
        float D = U0 * U0 - 4f * R * P_вх;
        if (D < 0f) { I = U0 / (2f * R); U_rov = U0 * 0.5f; return false; }
        I = (U0 - Mathf.Sqrt(D)) / (2f * R);
        U_rov = U0 - I * R;
        return true;
    }

    static float NormalizeAngle(float a)
    {
        a %= 360f;
        if (a > 180f) a -= 360f;
        return a;
    }
}
