using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class RovController : MonoBehaviour
{
    [Header("Тяга двигателей")]
    public float thrustForward = 5f;   // вперёд/назад
    public float thrustSide = 5f;      // влево/вправо
    public float thrustVertical = 4f;  // вверх/вниз (правый стик Y)
    public float torqueYaw = 0.5f;     // поворот (правый стик X)

    [Header("VR input (Input Actions)")]
    public InputActionReference moveAction;    // Move (Vector2) = Left stick
    public InputActionReference rotateAction;  // Rotate (Vector2) = Right stick
    public InputActionReference manipulatorOpenAction;
    public InputActionReference manipulatorCloseAction;

    [Header("Tuning")]
    [Range(0f, 0.5f)] public float deadzone = 0.15f;
    public bool invertVertical = false;

    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.linearDamping = 2f;
        _rb.angularDamping = 3f;
    }

    private void OnEnable()
    {
        moveAction?.action.Enable();
        rotateAction?.action.Enable();
        manipulatorOpenAction?.action.Enable();
        manipulatorCloseAction?.action.Enable();
    }

    private void OnDisable()
    {
        moveAction?.action.Disable();
        rotateAction?.action.Disable();
        manipulatorOpenAction?.action.Disable();
        manipulatorCloseAction?.action.Disable();
    }

    private void Update()
    {
        if (manipulatorOpenAction != null && manipulatorOpenAction.action.WasPressedThisFrame())
        {
            Debug.Log("Manipulator OPEN (G)");
            OpenManipulator();
        }

        if (manipulatorCloseAction != null && manipulatorCloseAction.action.WasPressedThisFrame())
        {
            Debug.Log("Manipulator CLOSE (F)");
            CloseManipulator();
        }
    }

    private void FixedUpdate()
    {
        Vector2 move = moveAction != null ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;
        Vector2 rot  = rotateAction != null ? rotateAction.action.ReadValue<Vector2>() : Vector2.zero;

        move = ApplyDeadzone(move, deadzone);
        rot  = ApplyDeadzone(rot, deadzone);

        float side = move.x;        // левый стик X: влево/вправо
        float forward = move.y;     // левый стик Y: вперёд/назад

        float yaw = rot.x;          // правый стик X: поворот
        float up = invertVertical ? -rot.y : rot.y; // правый стик Y: вверх/вниз

        _rb.AddRelativeForce(new Vector3(
            side * thrustSide,
            up * thrustVertical,
            forward * thrustForward
        ), ForceMode.Force);

        _rb.AddRelativeTorque(Vector3.up * yaw * torqueYaw, ForceMode.Force);

        Debug.Log($"Move: {move} Rotate: {rot}");
    }

    private static Vector2 ApplyDeadzone(Vector2 v, float dz)
    {
        if (v.magnitude < dz) return Vector2.zero;
        float m = Mathf.InverseLerp(dz, 1f, v.magnitude);
        return v.normalized * m;
    }

    private void OpenManipulator()
    {
        Debug.Log("OpenManipulator() called");
    }

    private void CloseManipulator()
    {
        Debug.Log("CloseManipulator() called");
    }
}