using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

public class SceneLoader : MonoBehaviour
{
    void Start()
    {
        SceneManager.LoadSceneAsync("Main", LoadSceneMode.Additive);
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "Main")
        {
            // Находим все источники света в Main сцене
            // и назначаем их на слой Underwater
            GameObject[] roots = scene.GetRootGameObjects();
            foreach (GameObject root in roots)
            {
                Light[] lights = root.GetComponentsInChildren<Light>(true);
                foreach (Light light in lights)
                {
                    light.gameObject.layer = LayerMask.NameToLayer("Underwater");
                    // Для Directional Light убираем влияние на другие слои
                    if (light.type == LightType.Directional)
                    {
                        light.cullingMask = LayerMask.GetMask("Underwater");
                    }
                }
            }
        }
    }
}