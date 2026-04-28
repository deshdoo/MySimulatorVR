using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class RovController : MonoBehaviour
{
    [Header("Тяга")]
    public float thrustForward = 5f;
    public float thrustVertical = 4f;
    public float torqueYaw = 0.5f;

    private Rigidbody _rb;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.linearDamping = 2f;
        _rb.angularDamping = 3f;
    }

    void FixedUpdate()
    {
        Debug.Log($"Forward: {DroneInput.forward} Vertical: {DroneInput.vertical} Yaw: {DroneInput.yaw}");

        _rb.AddRelativeForce(new Vector3(
            0f,
            DroneInput.vertical * thrustVertical,
            DroneInput.forward * thrustForward
        ), ForceMode.Force);

        _rb.AddRelativeTorque(
            Vector3.up * DroneInput.yaw * torqueYaw,
            ForceMode.Force
        );
    }
}