using Unity.XR.CoreUtils;
using UnityEngine;

// Вид рук в игре. Модель кисти и её движение — из рига XR Origin Hands (XR Hands двигает
// каждый сустав по отслеживанию гарнитуры); здесь меняется только материал:
//   • идёт видео с камер (ВидеоРук.Активно, только на гарнитуре) — рука становится
//     «окном» в камеру: видны настоящие руки с мягким светящимся краем;
//   • иначе — «призрачная», как в меню Meta: полупрозрачная заливка и светящийся край
//     (шейдер Custom/HandGhost).
// Сетку руки визуализатор создаёт, когда рука впервые появилась, — поэтому раз в
// полсекунды проверяем руки рига и красим новые. У рига скинированные сетки только у
// рук (модели контроллеров — обычные), по этому признаку руки и находятся.
public class РукиПризраки : MonoBehaviour
{
    static Material _призрак;
    float _след;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Создать()
    {
        if (FindFirstObjectByType<РукиПризраки>() != null) return;
        var go = new GameObject("РукиПризраки");
        DontDestroyOnLoad(go);
        go.AddComponent<РукиПризраки>();
    }

    void Update()
    {
        if (Time.unscaledTime < _след) return;
        _след = Time.unscaledTime + 0.5f;
        if (_призрак == null)
        {
            var sh = Shader.Find("Custom/HandGhost");
            if (sh == null) { enabled = false; Debug.LogWarning("[Руки] Нет шейдера Custom/HandGhost — руки остаются как в примере XRI."); return; }
            _призрак = new Material(sh) { name = "Рука-призрак (auto)" };
        }
        var нужный = ВидеоРук.Активно && ВидеоРук.МатРуки != null ? ВидеоРук.МатРуки : _призрак;
        foreach (var риг in FindObjectsByType<XROrigin>(FindObjectsSortMode.None))
            foreach (var r in риг.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.sharedMaterial == нужный) continue;
                var маты = r.sharedMaterials;
                for (int i = 0; i < маты.Length; i++) маты[i] = нужный;
                r.sharedMaterials = маты;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
    }
}
