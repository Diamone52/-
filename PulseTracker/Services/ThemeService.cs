using System;
using System.IO;
using System.Windows;

namespace PulseTracker.Services
{
    public enum AppTheme
    {
        Dark,
        Light
    }

    /// <summary>
    /// Переключает тему оформления (тёмная/светлая), подменяя объединённый словарь ресурсов
    /// приложения (Themes/DarkTheme.xaml или Themes/LightTheme.xaml). Все цветовые кисти в
    /// App.xaml и окнах подключены через DynamicResource, поэтому подмена словаря сразу же
    /// обновляет уже открытые окна без их пересоздания. Выбор темы сохраняется в текстовом
    /// файле рядом с базой данных, чтобы применяться и при следующем запуске.
    /// </summary>
    public static class ThemeService
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PulseTracker", "theme.txt");

        public static AppTheme Current { get; private set; } = AppTheme.Dark;

        public static event Action<AppTheme>? ThemeChanged;

        public static void Initialize()
        {
            var saved = AppTheme.Dark;
            try
            {
                if (File.Exists(SettingsPath) &&
                    Enum.TryParse<AppTheme>(File.ReadAllText(SettingsPath).Trim(), out var parsed))
                {
                    saved = parsed;
                }
            }
            catch
            {
                // Настройки не критичны — при ошибке чтения просто остаёмся на тёмной теме.
            }

            Apply(saved, save: false);
        }

        public static void Toggle()
        {
            Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark, save: true);
        }

        private static void Apply(AppTheme theme, bool save)
        {
            var uri = new Uri(theme == AppTheme.Dark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml", UriKind.Relative);
            var dict = new ResourceDictionary { Source = uri };

            var merged = Application.Current.Resources.MergedDictionaries;
            if (merged.Count > 0)
                merged[0] = dict;
            else
                merged.Add(dict);

            Current = theme;

            if (save)
            {
                try
                {
                    var folder = Path.GetDirectoryName(SettingsPath)!;
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(SettingsPath, theme.ToString());
                }
                catch
                {
                    // Не удалось сохранить — тема просто не переживёт перезапуск, это не страшно.
                }
            }

            ThemeChanged?.Invoke(theme);
        }
    }
}
