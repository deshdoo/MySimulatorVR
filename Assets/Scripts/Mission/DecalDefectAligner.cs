using UnityEngine;

// Ставится на префаб дефекта-декали (рядом с Decal Projector). Спавнер сажает
// дефект на поверхность и разворачивает его forward НАРУЖУ (от трубы), а декаль
// должна проецироваться ВНУТРЬ трубы. Этот скрипт в Start доворачивает объект,
// чтобы проекция шла в трубу — без ручных поворотов на 180°.
//
// Если декаль всё равно проецируется не на ту сторону (зависит от того, вдоль
// какой оси проецирует конкретный Decal Projector) — поставь галку «Перевернуть».
[DisallowMultipleComponent]
public class DecalDefectAligner : MonoBehaviour
{
    [Tooltip("Если декаль проецируется не на ту сторону трубы — поставь галку.")]
    public bool Перевернуть = false;

    void Start()
    {
        Vector3 f = Перевернуть ? transform.forward : -transform.forward;
        if (f.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.LookRotation(f, transform.up);
    }
}
