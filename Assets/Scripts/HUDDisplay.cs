using UnityEngine;
using TMPro;

public class HUDDisplay : MonoBehaviour
{
    [Header("Ссылки (можно оставить пустыми — найдёт сам)")]
    public Transform rov;
    public TMP_Text depthText;
    public TMP_Text headingText;
    public TMP_Text forwardText;

    void Start()
    {
        if (rov == null)
        {
            var rovObj = GameObject.Find("ROV");
            if (rovObj != null) rov = rovObj.transform;
        }

        if (depthText   == null) depthText   = FindTMP("Глубина");
        if (headingText == null) headingText  = FindTMP("Курс");
        if (forwardText == null) forwardText  = FindTMP("Тяга");
    }

    TMP_Text FindTMP(string objName)
    {
        var t = transform.Find(objName);
        return t != null ? t.GetComponent<TMP_Text>() : null;
    }

    void Update()
    {
        if (rov == null) return;
        // Глубину берём из общей шины, а не считаем заново: уровень поверхности
        // воды задаётся в RovSystemsSimulator, и вторая формула здесь разошлась бы
        // с приборами и графиками.
        if (depthText   != null) depthText.text   = $"DEPTH  {RovSystems.depth_m:F1} m";
        if (headingText != null) headingText.text  = $"HDG  {rov.eulerAngles.y:F0}°";
        if (forwardText != null) forwardText.text  = $"FWD  {DroneInput.forward * 100f:F0}%";
    }
}
