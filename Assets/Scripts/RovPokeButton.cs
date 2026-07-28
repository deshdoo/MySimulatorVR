using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Физическая кнопка-защёлка пульта, нажимаемая пальцем (poke) через XR Interaction Toolkit.
// Захват/нажатие приходит от штатного интерактора рига "XR Origin Hands" —
// то же событие XRI select срабатывает и от руки (poke/pinch), и от контроллера,
// поэтому кнопка работает одинаково в симуляторе и на очках (как LeverInteractable).
//
// Поведение защёлки (латч): каждое нажатие переключает состояние.
//   Вкл  -> кнопка вдавлена вниз и остаётся внизу, действие активно (свет горит).
//   Выкл -> кнопка выщелкнута вверх, действие выключено.
// Положение кнопки отражает СОСТОЯНИЕ, а не факт касания.
//
// Встроенное действие ToggleLights дёргает RovSystems.lightsOn — тот же флаг,
// что и клавиша в RovSpotlightsController, поэтому реальные прожекторы
// подхватывают состояние сами (и кнопка синхронна, даже если свет переключили клавишей).
// Для других кнопок используй onPressed (UnityEvent) — вешай что угодно в инспекторе.
//
// Настройка на объекте кнопки:
//   1. Объект-кнопка (маленький бокс/цилиндр) с Collider.
//   2. Add Component -> XR Simple Interactable (Collider закинуть в его Colliders).
//   3. Add Component -> RovPokeButton, Action = ToggleLights, Moving Part = сам меш кнопки.
[DisallowMultipleComponent]
public class RovPokeButton : MonoBehaviour
{
    public enum ButtonAction { None, ToggleLights, ToggleAutopilot, ToggleGrabber }

    [Header("Действие кнопки")]
    [Tooltip("ToggleLights — прожекторы (RovSystems.lightsOn). ToggleAutopilot — удержание (RovAutopilot). ToggleGrabber — клешня (RovSystems.grabberClosed). None — только латч + onPressed.")]
    public ButtonAction action = ButtonAction.ToggleLights;

    [Tooltip("Доп. действия при нажатии — вешай что угодно в инспекторе (для будущих кнопок).")]
    public UnityEvent onPressed;

    [Header("Тест клавишей (в редакторе, без VR)")]
    public Key testKey = Key.L;

    [Header("Вдавливание (защёлка)")]
    [Tooltip("Часть, которая вдавливается. Обычно — сам меш кнопки.")]
    public Transform movingPart;
    [Tooltip("Насколько кнопка уходит вниз во включённом состоянии, м")]
    public float pressDepth = 0.006f;
    [Tooltip("Скорость хода кнопки вверх/вниз")]
    public float moveSpeed = 20f;
    [Tooltip("Если ход кнопки идёт не в ту сторону — включи, чтобы инвертировать направление")]
    public bool invertPressDirection = false;

    [Header("Индикатор (опционально)")]
    [Tooltip("Меш-подсветка: горит, когда действие включено (свет).")]
    public Renderer indicator;
    public Color indicatorOnColor = new Color(0.3f, 1f, 0.4f);

    [Header("Подсветка при наведении")]
    [Tooltip("Меш кнопки, который подсвечивается, когда на него наведён палец/луч. Обычно — сам меш кнопки.")]
    public Renderer highlightRenderer;
    public Color highlightColor = new Color(0.3f, 0.7f, 1f);
    [Range(0f, 3f)] public float highlightIntensity = 1f;

    private XRBaseInteractable _interactable;
    private Vector3 _movingPartHome;
    private Material _indicatorMat;
    private Material _highlightMat;
    private bool _highlightHadEmission;
    private Color _highlightOrigEmission;

    // Латч-состояние для действий, у которых нет внешнего флага (action == None).
    private bool _latchedOn;

    void Awake()
    {
        // Кнопка — неподвижная арматура пульта, а не физическое тело.
        // Если на объекте оказался Rigidbody (например, от XR Grab Interactable),
        // делаем его кинематическим, иначе кнопка падает/улетает под гравитацией.
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // Если ход кнопки не назначен в инспекторе — двигаем сам объект кнопки,
        // чтобы вдавливалась любая кнопка, а не только та, где movingPart задан вручную.
        if (movingPart == null) movingPart = transform;
        _movingPartHome = movingPart.localPosition;
        if (indicator != null) _indicatorMat = indicator.material;
        if (highlightRenderer != null)
        {
            _highlightMat = highlightRenderer.material;
            _highlightHadEmission = _highlightMat.IsKeywordEnabled("_EMISSION");
            _highlightOrigEmission = _highlightMat.HasProperty("_EmissionColor")
                ? _highlightMat.GetColor("_EmissionColor") : Color.black;
        }
    }

    void OnEnable()
    {
        _interactable = GetComponent<XRBaseInteractable>();
        if (_interactable == null)
        {
            Debug.LogError($"[RovPokeButton] на {gameObject.name} нет XR Simple Interactable — добавь его.");
            enabled = false;
            return;
        }
        _interactable.selectEntered.AddListener(OnSelectEntered);
    }

    void OnDisable()
    {
        if (_interactable != null)
            _interactable.selectEntered.RemoveListener(OnSelectEntered);
    }

    void OnSelectEntered(SelectEnterEventArgs args) => Press();

    void Update()
    {
        // Тест с клавиатуры
        if (testKey != Key.None && Keyboard.current != null && Keyboard.current[testKey].wasPressedThisFrame)
            Press();

        bool on = IsOn();

        // Ход кнопки-защёлки: вниз когда включено, вверх когда выключено.
        // Двигаем вдоль собственной оси кнопки (её локальный "верх" в системе
        // родителя), чтобы работало и на наклонной панели, а не только на плоской.
        if (movingPart != null)
        {
            Vector3 axis = movingPart.localRotation * Vector3.up;
            if (invertPressDirection) axis = -axis;
            Vector3 target = on ? _movingPartHome - axis * pressDepth : _movingPartHome;
            movingPart.localPosition = Vector3.Lerp(movingPart.localPosition, target, Time.deltaTime * moveSpeed);
        }

        // Индикатор
        if (_indicatorMat != null)
        {
            _indicatorMat.EnableKeyword("_EMISSION");
            _indicatorMat.SetColor("_EmissionColor", on ? indicatorOnColor : Color.black);
        }

        // Подсветка при наведении (сигнал, что интерактабл ловит палец/луч)
        if (_highlightMat != null)
        {
            bool hovered = _interactable != null && _interactable.isHovered;
            if (hovered)
            {
                _highlightMat.EnableKeyword("_EMISSION");
                _highlightMat.SetColor("_EmissionColor", highlightColor * highlightIntensity);
            }
            else
            {
                if (!_highlightHadEmission) _highlightMat.DisableKeyword("_EMISSION");
                _highlightMat.SetColor("_EmissionColor", _highlightOrigEmission);
            }
        }
    }

    // Текущее состояние защёлки: для света берём реальный флаг (источник истины),
    // чтобы кнопка была синхронна, даже если свет переключили клавишей/другой кнопкой.
    bool IsOn()
    {
        switch (action)
        {
            case ButtonAction.ToggleLights:    return RovSystems.lightsOn;
            case ButtonAction.ToggleAutopilot: return RovAutopilot.Instance != null && RovAutopilot.Instance.Engaged;
            case ButtonAction.ToggleGrabber:   return RovSystems.grabberClosed;
            default:                           return _latchedOn;
        }
    }

    // Одно нажатие = одно переключение (XRI select entered вызывается один раз на poke).
    void Press()
    {
        switch (action)
        {
            case ButtonAction.ToggleLights:
                RovSystems.lightsOn = !RovSystems.lightsOn;
                break;
            case ButtonAction.ToggleAutopilot:
                if (RovAutopilot.Instance != null) RovAutopilot.Instance.Toggle();
                else Debug.LogWarning("[RovPokeButton] RovAutopilot не найден в сцене (Instance == null).");
                break;
            case ButtonAction.ToggleGrabber:
                RovSystems.grabberClosed = !RovSystems.grabberClosed;
                break;
            case ButtonAction.None:
                _latchedOn = !_latchedOn;
                break;
        }
        onPressed?.Invoke();
    }
}
