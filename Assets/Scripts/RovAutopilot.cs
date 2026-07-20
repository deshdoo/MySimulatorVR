using UnityEngine;

// Автопилот стабилизации ("Удержание") НПА.
// По включению приводит аппарат к ровному, "как при запуске" состоянию и держит его:
//   - ВЫРАВНИВАНИЕ ГОРИЗОНТА: тангаж и крен -> 0 (корпус встаёт прямо),
//     КУРС НЕ ТРОГАЕТСЯ — аппарат остаётся смотреть туда же, куда смотрел;
//   - удержание глубины (ПИД по вертикали);
//   - гашение продольного сноса.
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

        public void Reset() { _integral = 0f; _prevError = 0f; _hasPrev = false; }

        public float Update(float error, float dt)
        {
            _integral = Mathf.Clamp(_integral + error * dt, -integralClamp, integralClamp);
            float deriv = _hasPrev && dt > 0f ? (error - _prevError) / dt : 0f;
            _prevError = error;
            _hasPrev = true;
            return kp * error + ki * _integral + kd * deriv;
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
    public Pid depthPid = new Pid { kp = 0.8f, ki = 0.1f, kd = 0.5f, integralClamp = 2f };

    [Header("Гашение продольного сноса")]
    [Tooltip("Коэффициент торможения продольной скорости. Меньше = допускает лёгкий снос/покачивание.")]
    public float surgeBrakeGain = 0.8f;

    [Header("Мягкость отклика (глубина/снос)")]
    [Range(0.05f, 1f)]
    [Tooltip("Максимум команды тяги (доля полной). Меньше = мягче доводит.")]
    public float maxAuthority = 0.55f;
    [Tooltip("Скорость нарастания команды. Меньше = плавнее восстановление, без рывка.")]
    public float responseSmoothing = 2.5f;

    public bool Engaged { get; private set; }

    private Rigidbody _rb;
    private float _targetWorldY;
    private float _appliedVert, _appliedFwd;

    void Awake()
    {
        Instance = this;
        _rb = GetComponent<Rigidbody>();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    public void Toggle() { if (Engaged) Disengage(); else Engage(); }

    public void Engage()
    {
        Engaged = true;
        _targetWorldY = transform.position.y;   // держим текущую глубину
        depthPid.Reset();
        _appliedVert = DroneInput.vertical;
        _appliedFwd = DroneInput.forward;

        Debug.Log($"[RovAutopilot] ВКЛ. Выравниваю горизонт (тангаж={NormalizeAngle(transform.eulerAngles.x):F1}°, " +
                  $"крен={NormalizeAngle(transform.eulerAngles.z):F1}°), курс оставляю={transform.eulerAngles.y:F1}°, держим Y={_targetWorldY:F2}");
    }

    public void Disengage()
    {
        Engaged = false;
        DroneInput.forward = 0f;
        DroneInput.vertical = 0f;
        DroneInput.yaw = 0f;
    }

    void FixedUpdate()
    {
        if (!Engaged) return;
        float dt = Time.fixedDeltaTime;

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
        _rb.AddTorque(levelTorque, ForceMode.Acceleration);

        // --- Глубина -> _targetWorldY (ПИД) ---
        float yError = _targetWorldY - transform.position.y;
        float vertCmd = Mathf.Clamp(depthPid.Update(yError, dt), -maxAuthority, maxAuthority);

        // --- Гашение продольного сноса ---
        float surgeVel = Vector3.Dot(_rb.linearVelocity, transform.forward);
        float fwdCmd = Mathf.Clamp(-surgeBrakeGain * surgeVel, -maxAuthority, maxAuthority);

        // --- Плавное подведение команд тяги ---
        float s = 1f - Mathf.Exp(-responseSmoothing * dt);
        _appliedVert = Mathf.Lerp(_appliedVert, vertCmd, s);
        _appliedFwd = Mathf.Lerp(_appliedFwd, fwdCmd, s);

        DroneInput.vertical = _appliedVert;
        DroneInput.forward = _appliedFwd;
        DroneInput.yaw = 0f;   // курс не трогаем — рысканье не командуем
    }

    static float NormalizeAngle(float a)
    {
        a %= 360f;
        if (a > 180f) a -= 360f;
        return a;
    }
}
