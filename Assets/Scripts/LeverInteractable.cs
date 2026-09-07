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
    }

    void OnDisable()
    {
        if (_interactable != null)
        {
            _interactable.selectEntered.RemoveListener(OnSelectEntered);
            _interactable.selectExited.RemoveListener(OnSelectExited);
        }
        _grabHand = null;
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        // Рычаг тянут захватом, а не тычком: тыкающий палец (poke-интерактор) его не цепляет —
        // иначе случайное касание дёргало бы тягу. Хват идёт от grab/direct/near-интерактора
        // (щипок руки или grip/курок контроллера). Выключается галкой игнорироватьPoke.
        if (игнорироватьPoke && args.interactorObject is XRPokeInteractor) return;
        _grabHand = args.interactorObject.transform;
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

    // Проекция вектора на плоскость качания (перпендикулярную оси), нормализованная.
    static Vector3 ВПлоскости(Vector3 v, Vector3 ось)
    {
        Vector3 p = Vector3.ProjectOnPlane(v, ось);
        return p.sqrMagnitude > 1e-6f ? p.normalized : Vector3.zero;
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        _grabHand = null;
    }

    void Update()
    {
        bool hovered = _interactable != null && _interactable.isHovered;
        ApplyHighlight(hovered || _grabHand != null);

        if (axis == LeverAxis.Planar) { ОбновитьПланар(hovered); return; }

        if (_grabHand != null && рукаНаРукоятке)
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
            if (Keyboard.current[keyPositive].isPressed) input += 1f;
            if (Keyboard.current[keyNegative].isPressed) input -= 1f;
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

        float normalized = Mathf.InverseLerp(minAngle, maxAngle, _angle) * 2f - 1f;
        if (invert) normalized *= -1f;
        WriteToInput(normalized);
    }

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

        if (_grabHand != null)
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
                if (Keyboard.current[keyPositive].isPressed) inF += 1f;
                if (Keyboard.current[keyNegative].isPressed) inF -= 1f;
                if (Keyboard.current[keyRight].isPressed)    inR += 1f;
                if (Keyboard.current[keyLeft].isPressed)     inR -= 1f;
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
        DroneInput.forward = nF;
        DroneInput.lateral = nL;
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
