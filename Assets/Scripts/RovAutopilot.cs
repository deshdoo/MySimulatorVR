using UnityEngine;

// Автопилот стабилизации ("Удержание") НПА.
// По включению приводит аппарат к ровному, "как при запуске" состоянию и держит его:
//   - ВЫРАВНИВАНИЕ ГОРИЗОНТА: тангаж и крен -> 0 (корпус встаёт прямо),
//     КУРС НЕ ТРОГАЕТСЯ — аппарат остаётся смотреть туда же, куда смотрел;
//   - удержание глубины (ПИД по вертикали);
//   - гашение продольного и бокового сноса.
//
// Удержание — «живое», не мёртвое: держим не точку, а узкую ЗОНУ (мёртвые зоны по
// глубине и сносу). В пределах зоны регулятор молчит, и аппарат свободно
// покачивается в вихрях течения (WaterCurrent.ВихревоеВозмущение) — как настоящий
// ROV, который «дышит» около точки. Парируется лишь заметный уход за зону. Без
// этого аппарат висел бы идеально неподвижно, будто выключили физику.
//
// Почему выравнивание — прямым моментом, а не через DroneInput: команды тяги
// (DroneInput) управляют только продольным/вертикальным движением и рысканьем —
// наклоном (тангаж/крен) они не управляют вовсе. Поэтому крен/тангаж выравниваем
// напрямую моментом на Rigidbody: тянем "верх" корпуса к мировому "верху".
// Ось этого момента = Cross(up, worldUp) — она горизонтальна и НЕ содержит
// компоненты рыскания, поэтому курс остаётся свободным (как и требуется).
//
// Когда включён — пишет вертикаль/снос в DroneInput (перекрывая рычаги, за счёт
// DefaultExecutionOrder(-50) перед RovController) и прикладывает момент выравнивания.
// Выключен — не вмешивается, управление у рычагов.
//
// Ставится на объект НПА (там же, где RovController/Rigidbody), сцена Main.
// Доступ из Cabin (кнопка) — через статический Instance.
[RequireComponent(typeof(Rigidbody))]
[DefaultExecutionOrder(-50)]
public class RovAutopilot : MonoBehaviour
{
    public static RovAutopilot Instance { get; private set; }

    [System.Serializable]
    public class Pid
    {
        public float kp = 1f;
        public float ki = 0f;
        public float kd = 0f;
        [Tooltip("Предел накопленного интеграла (анти-виндап)")]
        public float integralClamp = 1f;

        float _integral, _prevError;
        bool _hasPrev;

        // Вклад каждого звена в последний посчитанный выход — для графиков.
        // Именно по ним видно, кто из трёх звеньев за что отвечает:
        // П тянет к уставке пропорционально ошибке, И добивает статическую
        // ошибку (например, остаточную плавучесть), Д гасит скорость подхода.
        public float ВкладП { get; private set; }
        public float ВкладИ { get; private set; }
        public float ВкладД { get; private set; }

        public void Reset()
        {
            _integral = 0f; _prevError = 0f; _hasPrev = false;
            ВкладП = ВкладИ = ВкладД = 0f;
        }

        public float Update(float error, float dt)
        {
            _integral = Mathf.Clamp(_integral + error * dt, -integralClamp, integralClamp);
            float deriv = _hasPrev && dt > 0f ? (error - _prevError) / dt : 0f;
            _prevError = error;
            _hasPrev = true;

            ВкладП = kp * error;
            ВкладИ = ki * _integral;
            ВкладД = kd * deriv;
            return ВкладП + ВкладИ + ВкладД;
        }
    }

    [Header("Выравнивание горизонта (курс НЕ трогается)")]
    [Tooltip("Сила выравнивания КРЕНА — заваленный вбок горизонт. Больше = резче.")]
    public float rollStrength = 4f;
    [Tooltip("Сила выравнивания ТАНГАЖА — нос вверх/вниз (вертикальная плоскость). Больше = резче.")]
    public float pitchStrength = 4f;
    [Tooltip("Демпфирование колебаний при выравнивании. Больше = плавнее, без перелёта.")]
    public float levelDamping = 2.5f;

    [Header("ПИД глубины (вертикаль)")]
    // ki/integralClamp подняты специально: у корпуса есть ПОСТОЯННАЯ положительная
    // плавучесть (_RovController_.ЧистаяПоложительнаяПлавучесть_Н, ~1 Н вверх). Чтобы
    // висеть ровно, вертикальная тяга должна ПОСТОЯННО чуть поддавливать вниз, а
    // постоянную составляющую даёт только интегральное звено. Слабый И (или большая
    // мёртвая зона, гасящая его вход) не успевает набрать это давление — и аппарат
    // потихоньку всплывает, «сводится наверх». Поэтому И сделан сильнее и с большим
    // запасом накопления.
    public Pid depthPid = new Pid { kp = 1.2f, ki = 0.25f, kd = 0.6f, integralClamp = 4f };

    [Header("Гашение сноса")]
    [Tooltip("Коэффициент торможения ПРОДОЛЬНОЙ скорости. Меньше = допускает лёгкий снос/покачивание.")]
    public float surgeBrakeGain = 0.8f;
    [Tooltip("Коэффициент торможения БОКОВОЙ скорости (лаг). Без него течение под углом к курсу " +
             "сносит аппарат вбок, и парировать это нечем — продольная тяга тут не помогает.")]
    public float swayBrakeGain = 0.8f;

    [Header("Живое удержание (зона, а не точка)")]
    [Tooltip("Мёртвая зона по ГЛУБИНЕ, м. В пределах ±этого автопилот не давит на вертикаль — " +
             "аппарат свободно покачивается в узком слое, как настоящий ROV, а не висит намертво. " +
             "0 — жёсткое удержание в точку (выглядит неживо, «как будто гравитацию выключили»). " +
             "ВАЖНО: зона не должна быть шире миллиметров-сантиметра — внутри неё И-звено ПИД " +
             "не видит ошибку и не может скомпенсировать постоянную плавучесть, поэтому большая " +
             "зона = аппарат всплывает к её верхней кромке и «сводится наверх». " +
             "0.002 = ±2 мм: держит плотно, лёгкое покачивание в вихрях остаётся.")]
    public float depthDeadband_m = 0.002f;
    [Tooltip("Мёртвая зона по СНОСУ, м/с: ниже этой скорости автопилот не тормозит горизонталь. " +
             "Даёт аппарату лениво дрейфовать в вихрях, парируя только заметный снос. " +
             "0 — гасит любое, даже мельчайшее движение.")]
    public float driftDeadband_mps = 0.008f;

    [Header("Выход на режим при включении")]
    [Tooltip("За сколько секунд автопилот выходит на полную власть после нажатия кнопки. " +
             "0 — сразу (рывок). Реальные движители тоже раскручиваются не мгновенно.")]
    public float времяВыходаНаРежим = 2.5f;

    [Header("Мягкость отклика (глубина/снос)")]
    [Range(0.05f, 1f)]
    [Tooltip("Максимум команды тяги (доля полной). Меньше = мягче доводит.")]
    public float maxAuthority = 0.55f;
    [Tooltip("Скорость нарастания команды. Меньше = плавнее восстановление, без рывка.")]
    public float responseSmoothing = 2.5f;

    public bool Engaged { get; private set; }

    // --- Телеметрия контура регулирования (для графиков на втором мониторе) ---
    // Классическая тройка ТАУ: уставка, ошибка регулирования, управляющее воздействие.
    // Уставка глубины, м (то, что держим).
    public float TargetDepth_m => Mathf.Max(0f, -_targetWorldY);
    // Ошибка регулирования e = уставка − факт, м. e > 0 — аппарат всплыл выше уставки.
    public float DepthError_m { get; private set; }
    // Управляющее воздействие u на вертикальную тягу, доля −1..1.
    public float VerticalCommand => _appliedVert;
    // Разложение выхода ПИД глубины на звенья (в тех же единицах, что и u).
    public float ВкладП => depthPid.ВкладП;
    public float ВкладИ => depthPid.ВкладИ;
    public float ВкладД => depthPid.ВкладД;

    private Rigidbody _rb;
    private float _targetWorldY;
    private float _appliedVert, _appliedFwd, _appliedLat;
    private float _рампа;   // 0..1, плавный ввод автопилота в работу после включения

    void Awake()
    {
        Instance = this;
        _rb = GetComponent<Rigidbody>();
        // Статика переживает вход в Play Mode, если отключён Domain Reload — сбрасываем явно.
        RovSystems.depthHoldActive = false;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    public void Toggle() { if (Engaged) Disengage(); else Engage(); }

    public void Engage()
    {
        Engaged = true;
        _targetWorldY = transform.position.y;   // держим текущую глубину
        // Публикуем режим в RovSystems — оттуда его читает UI пульта (SystemStatusDisplay).
        RovSystems.depthHoldActive   = true;
        RovSystems.depthHoldTarget_m = Mathf.Max(0f, -_targetWorldY);
        depthPid.Reset();
        _рампа = 0f;                        // власть автопилота нарастает с нуля
        _appliedVert = DroneInput.vertical; // подхватываем текущее положение рычагов
        _appliedFwd = DroneInput.forward;
        _appliedLat = DroneInput.lateral;

        Debug.Log($"[RovAutopilot] ВКЛ. Выравниваю горизонт (тангаж={NormalizeAngle(transform.eulerAngles.x):F1}°, " +
                  $"крен={NormalizeAngle(transform.eulerAngles.z):F1}°), курс оставляю={transform.eulerAngles.y:F1}°, держим Y={_targetWorldY:F2}");
    }

    public void Disengage()
    {
        Engaged = false;
        RovSystems.depthHoldActive = false;
        DepthError_m = 0f;
        DroneInput.lateral = 0f;
        DroneInput.forward = 0f;
        DroneInput.vertical = 0f;
        DroneInput.yaw = 0f;
    }

    void FixedUpdate()
    {
        if (!Engaged) return;
        float dt = Time.fixedDeltaTime;

        // Плавный ввод в работу. SmoothStep, а не линейный рост: начало и конец
        // разгона получаются мягкими, без ступеньки в момент выхода на полную власть.
        if (времяВыходаНаРежим > 0.01f)
            _рампа = Mathf.Clamp01(_рампа + dt / времяВыходаНаРежим);
        else
            _рампа = 1f;
        float k = Mathf.SmoothStep(0f, 1f, _рампа);

        // --- Выравнивание крена И тангажа прямым моментом (курс не трогаем) ---
        // Cross(up, worldUp): вектор коррекции наклона (|.| = sin наклона), не
        // содержит рыскания. Раскладываем его на две оси корпуса:
        //   вдоль носа (forward) -> крен (заваленный вбок горизонт),
        //   вдоль правого борта (right) -> тангаж (нос вверх/вниз, верт. плоскость),
        // и рулим каждую своим коэффициентом.
        Vector3 axisWorld = Vector3.Cross(transform.up, Vector3.up);
        float rollComp  = Vector3.Dot(axisWorld, transform.forward);
        float pitchComp = Vector3.Dot(axisWorld, transform.right);
        Vector3 levelTorque = transform.forward * (rollComp  * rollStrength)
                            + transform.right   * (pitchComp * pitchStrength);

        // Демпфируем только скорость наклона (тангаж/крен), убрав из угловой
        // скорости вертикальную (рыскание) составляющую — иначе гасили бы поворот.
        Vector3 angVel = _rb.angularVelocity;
        Vector3 tiltRate = angVel - Vector3.Project(angVel, Vector3.up);
        levelTorque -= tiltRate * levelDamping;
        _rb.AddTorque(levelTorque * k, ForceMode.Acceleration);

        // --- Глубина -> _targetWorldY (ПИД) ---
        float yError = _targetWorldY - transform.position.y;
        // В терминах глубины ошибка имеет обратный знак: глубина = −Y.
        // В телеметрию пишем ИСТИННУЮ ошибку (без мёртвой зоны) — чтобы на графике
        // было видно живое покачивание около уставки.
        DepthError_m = -yError;
        // В ПИД подаём ошибку с мёртвой зоной: в пределах ±depthDeadband_m аппарату
        // разрешено свободно покачиваться в вихрях, ПИД молчит. Держим слой, не точку.
        float yErrorCtl = ApplyDeadband(yError, depthDeadband_m);
        float vertCmd = Mathf.Clamp(depthPid.Update(yErrorCtl, dt), -maxAuthority, maxAuthority) * k;

        // --- Гашение сноса по обеим горизонтальным осям ---
        // Течение почти никогда не идёт точно вдоль курса, поэтому гасить только
        // продольную составляющую недостаточно: боковая уводит аппарат вбок.
        // Мёртвая зона driftDeadband_mps: мелкий снос от вихрей не трогаем (аппарат
        // лениво дрейфует), парируем лишь заметный уход — это и есть «дрейф с ПИД».
        float surgeVel = Vector3.Dot(_rb.linearVelocity, transform.forward);
        float swayVel  = Vector3.Dot(_rb.linearVelocity, transform.right);
        float fwdCmd = Mathf.Clamp(-surgeBrakeGain * ApplyDeadband(surgeVel, driftDeadband_mps), -maxAuthority, maxAuthority) * k;
        float latCmd = Mathf.Clamp(-swayBrakeGain  * ApplyDeadband(swayVel,  driftDeadband_mps), -maxAuthority, maxAuthority) * k;

        // --- Плавное подведение команд тяги ---
        float s = 1f - Mathf.Exp(-responseSmoothing * dt);
        _appliedVert = Mathf.Lerp(_appliedVert, vertCmd, s);
        _appliedFwd = Mathf.Lerp(_appliedFwd, fwdCmd, s);
        _appliedLat = Mathf.Lerp(_appliedLat, latCmd, s);

        DroneInput.vertical = _appliedVert;
        DroneInput.forward = _appliedFwd;
        DroneInput.lateral = _appliedLat;
        DroneInput.yaw = 0f;   // курс не трогаем — рысканье не командуем
    }

    static float NormalizeAngle(float a)
    {
        a %= 360f;
        if (a > 180f) a -= 360f;
        return a;
    }

    // Мёртвая зона: в пределах ±band возвращает 0, за ней — плавно растущее от нуля
    // значение (без ступеньки на границе, иначе движители «стучали» бы туда-сюда на
    // самой кромке). Благодаря ей автопилот держит НЕ точку, а узкую зону: аппарат
    // свободно покачивается в вихрях внутри неё и парирует лишь заметный уход.
    static float ApplyDeadband(float x, float band)
    {
        if (band <= 0f) return x;
        if (Mathf.Abs(x) <= band) return 0f;
        return x - Mathf.Sign(x) * band;
    }
}
