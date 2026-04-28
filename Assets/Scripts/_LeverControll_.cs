using UnityEngine;
using UnityEngine.InputSystem;

// Не зависит от XRI — использует собственный Raycast от контроллеров
public class LeverController : MonoBehaviour
{
    public enum LeverAxis { Forward, Vertical, Yaw }

    public LeverAxis axis = LeverAxis.Forward;

    [Header("Angles")]
    public float minAngle = -45f;
    public float maxAngle = 45f;

    [Header("Keyboard (editor test)")]
    public Key keyPositive = Key.I;
    public Key keyNegative = Key.K;

    [Header("Settings")]
    public bool invert = false;

    [Header("VR Highlight")]
    public Renderer leverRenderer;
    public Color highlightColor = new Color(0.3f, 0.7f, 1f);
    [Range(0f, 2f)] public float highlightIntensity = 0.5f;

    [Header("VR Controllers (assign in Inspector)")]
    public Transform leftControllerTransform;
    public Transform rightControllerTransform;
    [Tooltip("Дальность луча от контроллера")]
    public float rayDistance = 5f;

    private float _angle;
    private Material _mat;
    private Color _origEmission;
    private bool _hadEmission;
    private bool _hoveredLeft;
    private bool _hoveredRight;

    private InputAction _leftStick;
    private InputAction _rightStick;

    void Awake()
    {
        _leftStick = new InputAction(binding: "<XRController>{LeftHand}/thumbstick");
        _rightStick = new InputAction(binding: "<XRController>{RightHand}/thumbstick");
        _leftStick.Enable();
        _rightStick.Enable();

        if (leverRenderer != null)
        {
            _mat = leverRenderer.material;
            _hadEmission = _mat.IsKeywordEnabled("_EMISSION");
            _origEmission = _mat.GetColor("_EmissionColor");
        }
    }

    void OnDestroy()
    {
        _leftStick?.Dispose();
        _rightStick?.Dispose();
    }

    void Update()
    {
        // Определяем наведение через Raycast
        _hoveredLeft  = CheckRay(leftControllerTransform);
        _hoveredRight = CheckRay(rightControllerTransform);

        bool hovered = _hoveredLeft || _hoveredRight;
        ApplyHighlight(hovered);

        // Считаем input
        float input = 0f;

        if (Keyboard.current != null)
        {
            if (Keyboard.current[keyPositive].isPressed) input += 1f;
            if (Keyboard.current[keyNegative].isPressed) input -= 1f;
        }

        if (_hoveredLeft)  input += _leftStick.ReadValue<Vector2>().y;
        if (_hoveredRight) input += _rightStick.ReadValue<Vector2>().y;
        input = Mathf.Clamp(input, -1f, 1f);

        float dir = invert ? -1f : 1f;
        _angle += input * 60f * Time.deltaTime * dir;
        _angle = Mathf.Clamp(_angle, minAngle, maxAngle);

        transform.localEulerAngles = axis == LeverAxis.Yaw
            ? new Vector3(-_angle, 0f, 0f)
            : new Vector3(0f, 0f, -_angle);

        float normalized = Mathf.InverseLerp(minAngle, maxAngle, _angle) * 2f - 1f;
        if (invert) normalized *= -1f;
        WriteToInput(normalized);
    }

    bool CheckRay(Transform controller)
    {
        if (controller == null) return false;
        Ray ray = new Ray(controller.position, controller.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, rayDistance))
            return hit.collider != null && hit.collider.transform.IsChildOf(transform.parent ?? transform);
        return false;
    }

    void ApplyHighlight(bool on)
    {
        if (_mat == null) return;
        if (on)
        {
            _mat.EnableKeyword("_EMISSION");
            _mat.SetColor("_EmissionColor", highlightColor * highlightIntensity);
        }
        else
        {
            if (!_hadEmission) _mat.DisableKeyword("_EMISSION");
            _mat.SetColor("_EmissionColor", _origEmission);
        }
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
}
