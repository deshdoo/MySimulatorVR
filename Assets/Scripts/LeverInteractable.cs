using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Рычаг пульта на штатном XRI-интеракторе (Вариант B).
//
// Отличие от старого _LeverControll_.cs: захват и наведение приходят от
// XR Interaction Toolkit (компонент XR Simple Interactable на этом же объекте),
// поэтому рычаг хватается РУКОЙ (жест щипка) через интеракторы рига
// "XR Origin Hands" — без чтения grip-кнопки и без собственного Physics.Raycast.
// Всё остальное (маппинг угла, нейтральный дедзон, нормализация −1..1 в
// DroneInput, подсветка) перенесено из старого скрипта как есть — физика
// команды тяги не изменилась (см. Docs/Отчет_Физика_НПА.md §2.9, §2.11).
//
// Настройка на объекте рычага:
//   1. Снять/выключить старый LeverController.
//   2. Добавить компонент "XR Simple Interactable" (штатный XRI) + Collider,
//      который он будет использовать (галка Colliders у интерактабла).
//   3. Добавить этот скрипт, задать axis и подставить меши в поля подсветки.
[DisallowMultipleComponent]
public class LeverInteractable : MonoBehaviour
{
    // Planar — двухосевой рычаг-джойстик: вперёд/назад → forward, влево/вправо → lateral.
    public enum LeverAxis { Forward, Vertical, Yaw, Planar }

    public LeverAxis axis = LeverAxis.Forward;

    [Header("Planar (двухосевой джойстик)")]
    [Tooltip("Только для axis = Planar. ВКЛ — рычаг ходит «плюсом»: либо вперёд/назад, либо влево/вправо, " +
             "без диагоналей (крестовина). ВЫКЛ — свободный джойстик во все стороны.")]
    public bool крестоваяТраектория = true;

    [Header("Углы хода рычага")]
    public float minAngle = -45f;
    public float maxAngle = 45f;
    public bool invert = false;

    [Header("VR-захват")]
    [Tooltip("ВЫКЛ (по умолчанию) — рычаг берётся ЛЮБЫМ интерактором: захватом (щипок/grip) И " +
             "касанием пальца-poke. Так рычаг можно двигать рукой даже без жеста grab " +
             "(и в эмуляторе, где poke работает, и на очках). " +
             "ВКЛ — только захват (палец-poke рычаг не цепляет), строгий режим.")]
    public bool игнорироватьPoke = false;
    [Tooltip("ВЫКЛ (по умолчанию) — старое, проверенное управление: угол от линейного смещения " +
             "руки на grabRange метров. ВКЛ — экспериментальный режим «рука держит рукоятку» " +
             "(верх рычага следует за рукой); может потребовать invert per-рычаг.")]
    public bool рукаНаРукоятке = false;
    [Tooltip("Старый режим (рукаНаРукоятке выкл): сколько метров движения руки = полный ход рычага")]
    public float grabRange = 0.25f;
    [Tooltip("Ось смещения руки считать относительно взгляда камеры")]
    public bool useCameraForward = true;

    [Header("Нейтральный дедзон, град")]
    [Tooltip("Если угол ближе к 0, чем это значение — защёлкивается ровно в 0 (ноль тяги)")]
    public float neutralDeadzoneDeg = 6f;

    [Header("Подсветка при наведении/захвате")]
    public Renderer leverRenderer;
    public Renderer leverRenderer2;
    public Color highlightColor = new Color(0.3f, 0.7f, 1f);
    [Range(0f, 3f)] public float highlightIntensity = 1f;

    [Header("Клавиатура (тест в редакторе, только пока наведён)")]
    public Key keyPositive = Key.I;
    // K освобождена под Grab эмулятора (там hand-Grab = K) — тест рычага вниз на F1.
    public Key keyNegative = Key.F1;
    [Tooltip("Только Planar: боковой ход влево/вправо при тесте с клавиатуры.")]
    public Key keyLeft = Key.J;
    public Key keyRight = Key.L;

    private XRBaseInteractable _interactable;
    private float _angle;
    private float _angleLat;          // второй угол (боковой) — только для Planar

    // Состояние захвата
    private Transform _grabHand;      // трансформ интерактора (руки), держащего рычаг; null если свободен
    private Vector3 _grabStartHandPos;
    private float _grabStartAngle;
    private float _grabStartAngleLat; // боковой угол в момент захвата — только для Planar
    private Vector3 _осьЗахвата;      // мировая ось вращения рычага (режим «рука на рукоятке»)
    private Vector3 _рукаRefDir;      // направление pivot→рука в плоскости качания на момент захвата
    private Camera _mainCam;

    private readonly System.Collections.Generic.List<Material> _mats = new();
    private readonly System.Collections.Generic.List<Color> _origEmissions = new();
    private readonly System.Collections.Generic.List<bool> _hadEmissions = new();

    void Awake()
    {
        _mainCam = Camera.main;
        CacheMaterial(leverRenderer);
        CacheMaterial(leverRenderer2);
    }

    void OnEnable()
    {
        _interactable = GetComponent<XRBaseInteractable>();
        if (_interactable == null)
        {
            Debug.LogError($"[LeverInteractable] на {gameObject.name} нет XR Simple Interactable — добавь его.");
            enabled = false;
            return;
        }
        _interactable.selectEntered.AddListener(OnSelectEntered);
        _interactable.selectExited.AddListener(OnSelectExited);
        НаведениеПальцем.Добавить(_interactable);   // руками: подсветка, когда палец ближе 10 см
        if (!Все.Contains(this)) Все.Add(this);
    }

    void OnDisable()
    {
        if (_interactable != null)
        {
            _interactable.selectEntered.RemoveListener(OnSelectEntered);
            _interactable.selectExited.RemoveListener(OnSelectExited);
        }
        _grabHand = null;
        _захватил = null;
        ДержитРукой = -1;
        Все.Remove(this);
    }

    // Рычаги сцены, кто держит каждый и его коллайдеры — для «кисть на рукоятке» (УпорРук).
    public static readonly System.Collections.Generic.List<LeverInteractable> Все = new();
    public IXRInteractor Держит => _захватил;
    public XRBaseInteractable Интерактабл => _interactable;

    // Захват рукой без XRI (ХватРычагаРукой): у рук select от XRI приходит только с профилем
    // Hand Interaction гарнитуры, в эмуляторе — никогда. Рука — точка-ладонь, которую ведёт
    // ХватРычагаРукой; дальше всё как у захвата контроллером. -1 — рука не держит.
    public int ДержитРукой { get; private set; } = -1;

    public void ВзятьРукой(Transform ладонь, int рука)
    {
        if (_grabHand != null) return;   // уже держит контроллер или другая рука
        ДержитРукой = рука;
        НачатьЗахват(ладонь);
    }

    public void ОтпуститьРукой()
    {
        if (ДержитРукой < 0) return;
        ДержитРукой = -1;
        _grabHand = null;
    }

    // Держит ли рычаг эта рука (0 — левая, 1 — правая): своим хватом или XRI-захватом (не тычком).
    public bool ДержитРука(int рука)
    {
        if (ДержитРукой == рука) return true;
        return _захватил != null && !(_захватил is XRPokeInteractor) &&
               _захватил.handedness == (рука == 0 ? InteractorHandedness.Left : InteractorHandedness.Right);
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        // Рычаг тянут захватом, а не тычком: тыкающий палец (poke-интерактор) его не цепляет —
        // иначе случайное касание дёргало бы тягу. Хват идёт от grab/direct/near-интерактора
        // (щипок руки или grip/курок контроллера). Выключается галкой игнорироватьPoke.
        if (игнорироватьPoke && args.interactorObject is XRPokeInteractor) return;
        // В режиме рук рычаг берёт только ХватРычагаРукой. На шлеме у рук есть и захват XRI
        // (профиль Hand Interaction): две системы перехватывали бы рычаг друг у друга, и
        // отпускался бы он по-разному.
        if (GameSettings.РежимВвода == GameSettings.Ввод.Руки) return;
        ДержитРукой = -1;
        _захватил = args.interactorObject;
        Вибро.Импульс(_захватил, Вибро.Захват);
        НачатьЗахват(args.interactorObject.transform);
    }

    void НачатьЗахват(Transform рука)
    {
        _grabHand = рука;
        _grabStartHandPos = _grabHand.position;
        _grabStartAngle = _angle;
        _grabStartAngleLat = _angleLat;
        // Для режима «рука на рукоятке»: запоминаем ось качания и направление на руку.
        _осьЗахвата = ОсьВращенияМир();
        _рукаRefDir = ВПлоскости(_grabHand.position - transform.position, _осьЗахвата);
    }

    // Мировая ось, вокруг которой реально крутится рычаг (согласована с localEulerAngles
    // ниже): Forward/Vertical — локальный Z, Yaw — локальный X, взятые в системе родителя.
    Vector3 ОсьВращенияМир()
    {
        Vector3 л = (axis == LeverAxis.Yaw) ? Vector3.right : Vector3.forward;
        Quaternion баз = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
        return (баз * л).normalized;
    }

    // Захват рукой: на сколько градусов рука повернула рукоятку вокруг оси качания (локальная
    // ось родителя: Z — вперёд/назад, X — вбок) от точки захвата. Рычаг поворачивается на
    // минус этот угол (localEulerAngles = −угол), поэтому рукоятка идёт за рукой по своей
    // дуге, как настоящая. Рука у самой оси (ближе 3 см в плоскости качания) — угол не меняем.
    float ПоворотРукой(Vector3 осьЛок)
    {
        Quaternion баз = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
        Vector3 ось = (баз * осьЛок).normalized;
        Vector3 было = Vector3.ProjectOnPlane(_grabStartHandPos - transform.position, ось);
        Vector3 стало = Vector3.ProjectOnPlane(_grabHand.position - transform.position, ось);
        if (было.magnitude < 0.03f || стало.magnitude < 0.03f) return 0f;
        return Vector3.SignedAngle(было, стало, ось);
    }

    // Проекция вектора на плоскость качания (перпендикулярную оси), нормализованная.
    static Vector3 ВПлоскости(Vector3 v, Vector3 ось)
    {
        Vector3 p = Vector3.ProjectOnPlane(v, ось);
        return p.sqrMagnitude > 1e-6f ? p.normalized : Vector3.zero;
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        if (args.interactorObject != _захватил) return;   // рычаг держит рука своим хватом
        _grabHand = null;
        _захватил = null;
    }

    // Отдача контроллера: упёрся в крайнее положение — сильный толчок, прошёл нейтраль — лёгкий щелчок.
    IXRInteractor _захватил;
    bool _былНаУпоре, _былВНейтрали = true;

    void ОтдачаРычага()
    {
        if (_захватил == null) { _былНаУпоре = false; _былВНейтрали = Mathf.Abs(_angle) <= neutralDeadzoneDeg; return; }
        bool наУпоре = _angle <= minAngle + 0.5f || _angle >= maxAngle - 0.5f;
        bool вНейтрали = Mathf.Abs(_angle) <= neutralDeadzoneDeg;
        if (наУпоре && !_былНаУпоре) Вибро.Импульс(_захватил, Вибро.Упор, 0.06f);
        if (вНейтрали && !_былВНейтрали) Вибро.Импульс(_захватил, Вибро.Нейтраль);
        _былНаУпоре = наУпоре; _былВНейтрали = вНейтрали;
    }

    void Update()
    {
        bool hovered = _interactable != null && _interactable.isHovered;
        ApplyHighlight(hovered || _grabHand != null || НаведениеПальцем.Наведён(_interactable));

        if (axis == LeverAxis.Planar) { ОбновитьПланар(hovered); return; }

        if (_grabHand != null && ДержитРукой >= 0)
        {
            Vector3 осьЛок = axis == LeverAxis.Yaw ? Vector3.right : Vector3.forward;
            _angle = Mathf.Clamp(_grabStartAngle - ПоворотРукой(осьЛок), minAngle, maxAngle);
        }
        else if (_grabHand != null && рукаНаРукоятке)
        {
            // Рука держит рукоятку: угол рычага = стартовый + УГЛОВОЕ смещение руки вокруг
            // оси качания. Верх рычага «догоняет» руку, поэтому рука остаётся на рукоятке.
            Vector3 cur = ВПлоскости(_grabHand.position - transform.position, _осьЗахвата);
            if (cur != Vector3.zero && _рукаRefDir != Vector3.zero)
            {
                float d = Vector3.SignedAngle(_рукаRefDir, cur, _осьЗахвата);
                float dirSign = invert ? -1f : 1f;
                _angle = Mathf.Clamp(_grabStartAngle + d * dirSign, minAngle, maxAngle);
            }
        }
        else if (_grabHand != null)
        {
            // Старый режим: угол = линейное смещение руки вдоль оси взгляда от точки захвата.
            Vector3 delta = _grabHand.position - _grabStartHandPos;

            Vector3 dirAxis;
            if (axis == LeverAxis.Yaw)
                dirAxis = (useCameraForward && _mainCam != null) ? _mainCam.transform.right : Vector3.right;
            else
                dirAxis = (useCameraForward && _mainCam != null) ? _mainCam.transform.forward : Vector3.forward;
            dirAxis.y = 0f;
            dirAxis = dirAxis.sqrMagnitude > 0.001f ? dirAxis.normalized : Vector3.forward;

            float along = Vector3.Dot(delta, dirAxis);
            float t = Mathf.Clamp(along / grabRange, -1f, 1f);
            float dirSign = invert ? -1f : 1f;
            _angle = _grabStartAngle + t * (maxAngle - minAngle) * 0.5f * dirSign;
            _angle = Mathf.Clamp(_angle, minAngle, maxAngle);
        }
        else if (hovered && Keyboard.current != null)
        {
            // Тест в редакторе: клавиши работают только пока рычаг наведён
            float input = 0f;
            if (Keyboard.current[KeyBindings.K(keyPositive)].isPressed) input += 1f;
            if (Keyboard.current[KeyBindings.K(keyNegative)].isPressed) input -= 1f;
            float dirSign = invert ? -1f : 1f;
            _angle += input * 60f * Time.deltaTime * dirSign;
            _angle = Mathf.Clamp(_angle, minAngle, maxAngle);
        }

        // Нейтральный дедзон (детент)
        if (Mathf.Abs(_angle) <= neutralDeadzoneDeg)
            _angle = 0f;

        transform.localEulerAngles = axis == LeverAxis.Yaw
            ? new Vector3(-_angle, 0f, 0f)
            : new Vector3(0f, 0f, -_angle);
        ОтдачаРычага();

        float normalized = Mathf.InverseLerp(minAngle, maxAngle, _angle) * 2f - 1f;
        if (invert) normalized *= -1f;
        WriteToInput(КриваяПульта(normalized));
    }

    // Кривая пульта: команда тяги = ход·|ход|. Тяга винта растёт примерно как квадрат оборотов,
    // а установившаяся скорость — как корень из тяги (сопротивление ~v²), поэтому с такой
    // кривой СКОРОСТЬ аппарата пропорциональна ходу рычага: четверть хода — четверть
    // паспортной скорости, полный ход — паспортная (1 м/с вперёд у «Марлина»). Раньше
    // тяга шла прямо от хода, и уже 10 % хода давали треть скорости — у нуля аппарат
    // дёргался, тихо подойти к трубе было трудно. Только для рычагов: автопилот и помощь
    // пилоту командуют тягой напрямую, их настройка не меняется.
    static float КриваяПульта(float ход) => ход * Mathf.Abs(ход);

    void WriteToInput(float value)
    {
        switch (axis)
        {
            case LeverAxis.Forward:  DroneInput.forward  = value; break;
            case LeverAxis.Vertical: DroneInput.vertical = value; break;
            case LeverAxis.Yaw:      DroneInput.yaw      = value; break;
        }
    }

    // Двухосевой рычаг: вперёд/назад → DroneInput.forward, влево/вправо → DroneInput.lateral.
    // Смещение руки раскладываем на «вперёд» (взгляд камеры) и «вправо» камеры; крестовина
    // (крестоваяТраектория) оставляет только доминирующую ось, чтобы ход был по «плюсу».
    void ОбновитьПланар(bool hovered)
    {
        float halfSpan = (maxAngle - minAngle) * 0.5f;
        float dirSign = invert ? -1f : 1f;
        float fullDefl = Mathf.Max(Mathf.Abs(minAngle), Mathf.Abs(maxAngle));

        if (_grabHand != null && ДержитРукой >= 0)
        {
            // Рукой: рукоятка за рукой по обеим осям (вперёд/назад — вокруг Z, вбок — вокруг X).
            float вперёд = _grabStartAngle - ПоворотРукой(Vector3.forward);
            float вбок = _grabStartAngleLat - ПоворотРукой(Vector3.right);
            if (крестоваяТраектория)
            {
                if (Mathf.Abs(вперёд - _grabStartAngle) >= Mathf.Abs(вбок - _grabStartAngleLat)) вбок = _grabStartAngleLat;
                else вперёд = _grabStartAngle;
            }
            _angle = Mathf.Clamp(вперёд, minAngle, maxAngle);
            _angleLat = Mathf.Clamp(вбок, minAngle, maxAngle);
        }
        else if (_grabHand != null)
        {
            // Прямое отклонение от точки захвата: тянешь руку вперёд/назад — тяга вперёд/назад,
            // тянешь вбок — боковой снос. Ось вперёд/вбок — относительно взгляда камеры.
            Vector3 delta = _grabHand.position - _grabStartHandPos;
            Vector3 fwd   = (useCameraForward && _mainCam != null) ? _mainCam.transform.forward : Vector3.forward;
            Vector3 right = (useCameraForward && _mainCam != null) ? _mainCam.transform.right   : Vector3.right;
            fwd.y = 0f; right.y = 0f;
            fwd   = fwd.sqrMagnitude   > 0.001f ? fwd.normalized   : Vector3.forward;
            right = right.sqrMagnitude > 0.001f ? right.normalized : Vector3.right;

            float tF = Mathf.Clamp(Vector3.Dot(delta, fwd)   / grabRange, -1f, 1f);
            float tR = Mathf.Clamp(Vector3.Dot(delta, right) / grabRange, -1f, 1f);
            // Крестовина: оставляем ось, КУДА СЕЙЧАС ведёт рука (по модулю offset), а не по
            // накопленному углу — иначе «залипший» вперёд глушил бы боковую ось.
            if (крестоваяТраектория)
            {
                if (Mathf.Abs(tF) >= Mathf.Abs(tR)) tR = 0f; else tF = 0f;
            }
            _angle    = Mathf.Clamp(tF * halfSpan * dirSign, minAngle, maxAngle);
            _angleLat = Mathf.Clamp(tR * halfSpan * dirSign, minAngle, maxAngle);
        }
        else
        {
            // Не держим: рычаг-джойстик сам возвращается к центру (отпустил — снос прекратился).
            // Пока наведён — клавиши I/K (вперёд/назад), J/L (влево/вправо) для теста в редакторе.
            float inF = 0f, inR = 0f;
            if (hovered && Keyboard.current != null)
            {
                if (Keyboard.current[KeyBindings.K(keyPositive)].isPressed) inF += 1f;
                if (Keyboard.current[KeyBindings.K(keyNegative)].isPressed) inF -= 1f;
                if (Keyboard.current[KeyBindings.K(keyRight)].isPressed)    inR += 1f;
                if (Keyboard.current[KeyBindings.K(keyLeft)].isPressed)     inR -= 1f;
                if (крестоваяТраектория && (inF != 0f || inR != 0f))
                {
                    if (Mathf.Abs(inF) >= Mathf.Abs(inR)) inR = 0f; else inF = 0f;
                }
            }
            float sp = 300f * Time.deltaTime;   // скорость возврата/набора угла
            _angle    = Mathf.MoveTowards(_angle,    Mathf.Clamp(inF * fullDefl * dirSign, minAngle, maxAngle), sp);
            _angleLat = Mathf.MoveTowards(_angleLat, Mathf.Clamp(inR * fullDefl * dirSign, minAngle, maxAngle), sp);
        }

        // Нейтральный дедзон по каждой оси отдельно.
        if (Mathf.Abs(_angle)    <= neutralDeadzoneDeg) _angle = 0f;
        if (Mathf.Abs(_angleLat) <= neutralDeadzoneDeg) _angleLat = 0f;

        // Наклон рычага: вперёд/назад — вокруг Z (как у обычного Forward), влево/вправо — вокруг X.
        transform.localEulerAngles = new Vector3(-_angleLat, 0f, -_angle);

        float nF = Mathf.InverseLerp(minAngle, maxAngle, _angle)    * 2f - 1f;
        float nL = Mathf.InverseLerp(minAngle, maxAngle, _angleLat) * 2f - 1f;
        if (invert) { nF *= -1f; nL *= -1f; }
        DroneInput.forward = КриваяПульта(nF);
        DroneInput.lateral = КриваяПульта(nL);
    }

    void CacheMaterial(Renderer r)
    {
        if (r == null) return;
        var m = r.material;
        _mats.Add(m);
        _hadEmissions.Add(m.IsKeywordEnabled("_EMISSION"));
        _origEmissions.Add(m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black);
    }

    void ApplyHighlight(bool on)
    {
        for (int i = 0; i < _mats.Count; i++)
        {
            var m = _mats[i];
            if (m == null) continue;
            if (on)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", highlightColor * highlightIntensity);
            }
            else
            {
                if (!_hadEmissions[i]) m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", _origEmissions[i]);
            }
        }
    }
}
