using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Кнопка «ФОТО» (осмотр) — отдельная от RovPokeButton, потому что действие
// МОМЕНТНОЕ, а не защёлка: нажал → кадр с монитора, в него попадают все дефекты в
// радиусе осмотра (RovScanner.Просканировать через статический Instance, кросс-сцена).
// Кнопка кратко вдавливается и возвращается.
//
// Вид строится сам при запуске (КруглаяКрасная): вместо квадратной клавиши — круглый
// красный колпачок, вокруг ободок-индикатор (горит жёлтым, когда на мониторе виден дефект, который можно
// снять) и надпись «ФОТО» на пульте со стороны оператора.
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

    [Header("Вид «ФОТО»")]
    [Tooltip("При запуске заменить квадратную клавишу круглой красной кнопкой с ободком-индикатором и надписью.")]
    public bool КруглаяКрасная = true;
    public string Надпись = "ФОТО";

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
        if (КруглаяКрасная) ПостроитьКруглую();

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
        НаведениеПальцем.Добавить(_interactable);   // руками: подсветка, когда палец ближе 10 см
        УпорРук.Кнопка(_interactable);              // и рука-призрак встаёт на кнопку
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
        ПопробоватьНажать(args.interactorObject);
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        // Продавливание — запасной путь, если касание-hover не сработало; антидребезг
        // не даёт продублировать уже засчитанное касание. См. RovPokeButton.
        if (толькоPoke && !(args.interactorObject is XRPokeInteractor)) return;
        ПопробоватьНажать(args.interactorObject);
    }

    void ПопробоватьНажать(IXRInteractor кто)
    {
        if (Time.time < _следующееКасание_с) return;
        _следующееКасание_с = Time.time + Mathf.Max(0f, антидребезг_с);
        Вибро.Импульс(кто, Вибро.Кнопка);
        Press();
    }

    void Update()
    {
        // Тест с клавиатуры
        if (testKey != Key.None && Keyboard.current != null && Keyboard.current[KeyBindings.K(testKey)].wasPressedThisFrame)
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
            bool hovered = _interactable != null && (_interactable.isHovered || НаведениеПальцем.Наведён(_interactable));
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

    // Круглая красная кнопка поверх места квадратной клавиши: колпачок (едет при нажатии
    // вместе с объектом кнопки), неподвижный ободок-индикатор и надпись. Коллайдер и
    // XR-взаимодействие остаются от исходной клавиши — жмётся там же, где раньше.
    void ПостроитьКруглую()
    {
        var исходный = GetComponent<MeshRenderer>();
        if (исходный == null) return;
        Bounds b = исходный.bounds;
        float d = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.9f, 0.03f, 0.08f);
        const float высота = 0.014f;
        исходный.enabled = false;

        var колпачок = Цилиндр("Колпачок ФОТО", new Vector3(b.center.x, b.max.y - высота * 0.5f + 0.003f, b.center.z),
                               d, высота, new Color(0.78f, 0.05f, 0.04f), 0.6f, transform);
        var ободок = Цилиндр("Ободок ФОТО", new Vector3(b.center.x, b.max.y - высота + 0.002f, b.center.z),
                             d * 1.28f, 0.004f, new Color(0.10f, 0.10f, 0.10f), 0.3f, transform.parent);
        if (transform.parent == null) SceneManager.MoveGameObjectToScene(ободок.gameObject, gameObject.scene);   // а не в активную сцену миссии
        highlightRenderer = колпачок;
        highlightColor = new Color(1f, 0.45f, 0.35f);
        if (indicator == null) indicator = ободок;

        // Надпись лежит на пульте между кнопкой и оператором, читается с его места.
        var кам = Camera.main;
        Vector3 кОператору = кам != null ? Vector3.ProjectOnPlane(кам.transform.position - b.center, Vector3.up) : Vector3.left;
        кОператору = кОператору.sqrMagnitude > 1e-4f ? кОператору.normalized : Vector3.left;
        var go = new GameObject("Надпись ФОТО");
        go.transform.SetParent(transform.parent, false);
        if (transform.parent == null) SceneManager.MoveGameObjectToScene(go, gameObject.scene);
        go.transform.SetPositionAndRotation(new Vector3(b.center.x, b.max.y - высота + 0.003f, b.center.z) + кОператору * (d * 0.64f + 0.014f),
                                            Quaternion.LookRotation(Vector3.down, -кОператору));
        var tm = go.AddComponent<TextMeshPro>();
        foreach (var t in FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
            if (t != tm && t.font != null) { tm.font = t.font; break; }   // шрифт пульта — с кириллицей
        tm.text = "<b>" + Надпись + "</b>";
        tm.fontSize = 0.14f;
        tm.alignment = TextAlignmentOptions.Center;
        tm.textWrappingMode = TextWrappingModes.NoWrap;
        tm.color = new Color(0.92f, 0.92f, 0.9f);
        tm.rectTransform.sizeDelta = new Vector2(0.1f, 0.02f);
    }

    static MeshRenderer Цилиндр(string имя, Vector3 центр, float диаметр, float высота, Color цвет, float гладкость, Transform родитель)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = имя;
        Object.DestroyImmediate(go.GetComponent<Collider>());   // жмётся коллайдер исходной клавиши
        go.transform.position = центр;
        go.transform.localScale = new Vector3(диаметр, высота * 0.5f, диаметр);   // примитив: Ø1, высота 2
        go.transform.SetParent(родитель, true);
        var r = go.GetComponent<MeshRenderer>();
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh) { name = имя };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", цвет);
        m.color = цвет;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", гладкость);
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return r;
    }

    void Press()
    {
        if (RovScanner.Instance != null) RovScanner.Instance.Просканировать();
        else Debug.LogWarning("[RovScanButton] RovScanner не найден (Instance == null) — миссия/НПА не запущены?");

        _pressUntil = Time.time + pressPulse;
        onPressed?.Invoke();
    }
}
