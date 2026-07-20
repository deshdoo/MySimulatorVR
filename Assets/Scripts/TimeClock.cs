using UnityEngine;
using TMPro;

public class SessionTimer : MonoBehaviour
{
    [Header("Настройки")]
    public TextMeshProUGUI targetText; // Ваш текстовый элемент времени
    
    private float elapsedTime = 0f; // Прошедшее время

    void Update()
    {
        // Прибавляем время, прошедшее с последнего кадра
        elapsedTime += Time.deltaTime;
        
        UpdateDisplay();
    }

    void UpdateDisplay()
    {
        int hours = Mathf.FloorToInt(elapsedTime / 3600);
        int minutes = Mathf.FloorToInt((elapsedTime % 3600) / 60);
        int seconds = Mathf.FloorToInt(elapsedTime % 60);

        // Форматирование в ЧЧ:ММ:СС
        string timeString = string.Format("{0:D2}:{1:D2}:{2:D2}", 
                                          hours, minutes, seconds);
        
        targetText.text = timeString;
    }
}