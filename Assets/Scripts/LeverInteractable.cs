using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

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
    public enum LeverAxis { Forward, Vertical, Yaw }

    public LeverAxis axis = LeverAxis.Forward;

    [Header("Углы хода рычага")]
    public float minAngle = -45f;
    public float maxAngle = 45f;
    public bool invert = false;

    [Header("VR-захват")]
    [Tooltip("Сколько метров движения руки = полный ход рычага (minAngle..maxAngle)")]
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
    public Key keyNegative = Key.K;

    private XRBaseInteractable _interactable;
    private float _angle;

    // Состояние захвата
    private Transform _grabHand;      // трансформ интерактора (руки), держащего рычаг; null если свободен
    private Vector3 _grabStartHandPos;
    private float _grabStartAngle;
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
        _grabHand = args.interactorObject.transform;
        _grabStartHandPos = _grabHand.position;
        _grabStartAngle = _angle;
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        _grabHand = null;
    }

    void Update()
    {
        bool hovered = _interactable != null && _interactable.isHovered;
        ApplyHighlight(hovered || _grabHand != null);

        if (_grabHand != null)
        {
            // Прямое VR-управление: угол = функция смещения руки от точки захвата
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
