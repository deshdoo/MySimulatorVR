using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Кнопка ОСМОТРА (сканирования) — отдельная от RovPokeButton, потому что действие
// МОМЕНТНОЕ, а не защёлка: нажал → сканер засчитывает подсвеченную рядом трещину
// (RovScanner.Просканировать через статический Instance, кросс-сцена). Кнопка кратко
// вдавливается и возвращается.
//
// Логика нажатия и hover-подсветка — как у RovPokeButton (тот же XRI select от руки/
// контроллера, тот же ход и подсветка при наведении). Индикатор (если задан) горит,
// когда рядом есть трещина, готовая к осмотру (RovScanner.ЕстьКандидат).
//
// Настройка на объекте кнопки:
//   1. Меш кнопки с Collider.
//   2. Add Component -> XR Simple Interactable (Collider закинуть в его Colliders).
//   3. Add Component -> RovScanButton, Moving Part = меш кнопки (для хода).
[DisallowMultipleComponent]
public class RovScanButton : MonoBehaviour
{
    [Header("Тест клавишей (в редакторе, без VR)")]
    public Key testKey = Key.None;

    [Tooltip("Доп. действия при нажатии — звук и т.п. (вешай в инспекторе)")]
    public UnityEvent onPressed;

    [Header("Вдавливание (моментное)")]
    [Tooltip("Часть, которая вдавливается. Обычно — сам меш кнопки.")]
    public Transform movingPart;
    [Tooltip("Насколько кнопка уходит вниз при нажатии, м")]
    public float pressDepth = 0.006f;
    [Tooltip("Скорость хода кнопки вверх/вниз")]
    public float moveSpeed = 20f;
    [Tooltip("Если ход идёт не в ту сторону — включи, чтобы инвертировать направление")]
    public bool invertPressDirection = false;
    [Tooltip("Сколько кнопка держится вдавленной после нажатия, с")]
    public float pressPulse = 0.15f;

    [Header("Индикатор «готово к осмотру» (опционально)")]
    [Tooltip("Меш-подсветка: горит, когда рядом есть трещина, готовая к осмотру.")]
    public Renderer indicator;
    public Color indicatorOnColor = new Color(1f, 0.8f, 0.2f);

    [Header("Подсветка при наведении")]
    [Tooltip("Меш кнопки, который подсвечивается под пальцем/лучом. Обычно — сам меш кнопки.")]
    public Renderer highlightRenderer;
    public Color highlightColor = new Color(0.3f, 0.7f, 1f);
    [Range(0f, 3f)] public float highlightIntensity = 1f;

    [Header("Строгий VR-ввод (обычно НЕ нужно)")]
    [Tooltip("ВЫКЛ (по умолчанию) — нажатие по любому select: руками palcem-poke, " +
             "контроллером наведение+курок/grab. ВКЛ — только палец-poke.")]
    public bool толькоPoke = false;
    [Tooltip("ВКЛ (по умолчанию) — жать в момент касания кончиком пальца (без продавливания): касание = нажатие. " +
             "ВЫКЛ — нужно чуть вдавить (poke-select).")]
    public bool нажиматьПриКасании = true;
    [Tooltip("Защита от дребезга при нажатии по касанию: минимум секунд между срабатываниями.")]
    public float антидребезг_с = 0.3f;

    private XRBaseInteractable _interactable;
    private Vector3 _movingPartHome;
    private Material _indicatorMat;
    private Material _highlightMat;
    private bool _highlightHadEmission;
    private Color _highlightOrigEmission;
    private float _pressUntil;
    private float _следующееКасание_с;   // антидребезг нажатия по касанию

    void Awake()
    {
        // Кнопка — неподвижная арматура: если есть Rigidbody, делаем кинематическим.
        var rb = GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }

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
            Debug.LogError($"[RovScanButton] на {gameObject.name} нет XR Simple Interactable — добавь его.");
            enabled = false;
            return;
        }
        _interactable.selectEntered.AddListener(OnSelectEntered);
        _interactable.hoverEntered.AddListener(OnHoverEntered);
    }

    void OnDisable()
    {
        if (_interactable != null)
        {
            _interactable.selectEntered.RemoveListener(OnSelectEntered);
            _interactable.hoverEntered.RemoveListener(OnHoverEntered);
        }
    }

    // Касание кончиком пальца (poke коснулся, ещё не продавил) — жмём сразу, без задержки
    // на глубину. Только poke-интерактор: луч/палец издалека не жмёт. См. RovPokeButton.
    void OnHoverEntered(HoverEnterEventArgs args)
    {
        if (!нажиматьПриКасании) return;
        if (!(args.interactorObject is XRPokeInteractor)) return;
        ПопробоватьНажать();
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        // Продавливание — запасной путь, если касание-hover не сработало; антидребезг
        // не даёт продублировать уже засчитанное касание. См. RovPokeButton.
        if (толькоPoke && !(args.interactorObject is XRPokeInteractor)) return;
        ПопробоватьНажать();
    }

    void ПопробоватьНажать()
    {
        if (Time.time < _следующееКасание_с) return;
        _следующееКасание_с = Time.time + Mathf.Max(0f, антидребезг_с);
        Press();
    }

    void Update()
    {
        // Тест с клавиатуры
        if (testKey != Key.None && Keyboard.current != null && Keyboard.current[testKey].wasPressedThisFrame)
            Press();

        bool pressed = Time.time < _pressUntil;

        // Ход кнопки: вниз пока «нажата» (короткий пульс), затем возврат вверх.
        if (movingPart != null)
        {
            Vector3 axis = movingPart.localRotation * Vector3.up;
            if (invertPressDirection) axis = -axis;
            Vector3 target = pressed ? _movingPartHome - axis * pressDepth : _movingPartHome;
            movingPart.localPosition = Vector3.Lerp(movingPart.localPosition, target, Time.deltaTime * moveSpeed);
        }

        // Индикатор: горит, когда рядом есть трещина, готовая к зачёту.
        if (_indicatorMat != null)
        {
            bool готово = RovScanner.Instance != null && RovScanner.Instance.ЕстьКандидат;
            _indicatorMat.EnableKeyword("_EMISSION");
            _indicatorMat.SetColor("_EmissionColor", готово ? indicatorOnColor : Color.black);
        }

        // Подсветка при наведении (как у RovPokeButton).
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

    void Press()
    {
        if (RovScanner.Instance != null) RovScanner.Instance.Просканировать();
        else Debug.LogWarning("[RovScanButton] RovScanner не найден (Instance == null) — миссия/НПА не запущены?");

        _pressUntil = Time.time + pressPulse;
        onPressed?.Invoke();
    }
}
