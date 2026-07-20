using UnityEngine;

[RequireComponent(typeof(Light))]
public class CookieCausticsMotion : MonoBehaviour
{
    [Header("Амплитуда колебания (в градусах)")]
    public float amplitude = 1f;

    [Header("Скорость колебания (чем меньше, тем медленнее движение)")]
    public float speed = 0.04f;

    [Header("Скорость общего вращения, чтобы рисунок не стоял на месте")]
    public float slowRotationSpeed = 0.3f;

    private Vector3 baseRotation;

    void Start()
    {
        baseRotation = transform.eulerAngles;
    }

    void Update()
    {
        float t = Time.time * speed;

        float noiseX = (Mathf.PerlinNoise(t, 0f) - 0.5f) * 2f;
        float noiseY = (Mathf.PerlinNoise(0f, t) - 0.5f) * 2f;

        float x = baseRotation.x + noiseX * amplitude;
        float y = baseRotation.y + noiseY * amplitude;
        float z = baseRotation.z + Time.time * slowRotationSpeed;

        transform.rotation = Quaternion.Euler(x, y, z);
    }
}
