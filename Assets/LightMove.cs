using UnityEngine;

[RequireComponent(typeof(Light))]
public class CookieCausticsMotion : MonoBehaviour
{
    [Header("Амплитуда отклонения (в градусах)")]
    public float amplitude = 2f;

    [Header("Скорость изменения (чем больше, тем быстрее шевелится)")]
    public float speed = 0.2f;

    [Header("Немного общего вращения, чтобы рисунок не был статичным")]
    public float slowRotationSpeed = 2f;  

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