using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

// Значок приложения («ТНПА симулятор») — ставится сам после компиляции, без пунктов меню.
// Исходник — Tools/icon/AppIcon_Original.webp:
//   AppIcon.png — он же 1024×1024 (по умолчанию для всех платформ, обычный и круглый
//                 значок Android);
//   AppIcon_Background/Foreground.png — слои адаптивного значка Android: тёмный фон и
//                 картинка на 64 % в центре, чтобы лаунчер, обрезая кругом, не срезал надпись.
// Если значки в настройках уже стоят те же — ничего не трогает.
[InitializeOnLoad]
static class ЗначокПриложения
{
    const string Папка = "Assets/Textures/AppIcon/";

    static ЗначокПриложения() => EditorApplication.delayCall += Поставить;

    static void Поставить()
    {
        var полный = AssetDatabase.LoadAssetAtPath<Texture2D>(Папка + "AppIcon.png");
        var фон = AssetDatabase.LoadAssetAtPath<Texture2D>(Папка + "AppIcon_Background.png");
        var передний = AssetDatabase.LoadAssetAtPath<Texture2D>(Папка + "AppIcon_Foreground.png");
        if (полный == null) return;   // картинки ещё не импортированы — поставится при следующей компиляции

        bool изменено = false;
        var поУмолчанию = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
        if (поУмолчанию.Length == 0 || поУмолчанию[0] != полный)
        {
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { полный }, IconKind.Any);
            изменено = true;
        }

        изменено |= Задать(AndroidPlatformIconKind.Legacy, полный);
        изменено |= Задать(AndroidPlatformIconKind.Round, полный);
        if (фон != null && передний != null) изменено |= Задать(AndroidPlatformIconKind.Adaptive, фон, передний);

        if (изменено)
        {
            AssetDatabase.SaveAssets();
            Debug.Log("[Значок] Значок приложения «ТНПА симулятор» поставлен (Android: обычный, круглый, адаптивный).");
        }
    }

    // Все размеры значка этого вида — одной картинкой (Unity сам уменьшит).
    static bool Задать(PlatformIconKind вид, params Texture2D[] слои)
    {
        var значки = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, вид);
        bool изменено = false;
        foreach (var з in значки)
        {
            var было = з.GetTextures();
            bool те = было.Length >= слои.Length;
            for (int i = 0; те && i < слои.Length; i++) те = было[i] == слои[i];
            if (те) continue;
            з.SetTextures(слои);
            изменено = true;
        }
        if (изменено) PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, вид, значки);
        return изменено;
    }
}
