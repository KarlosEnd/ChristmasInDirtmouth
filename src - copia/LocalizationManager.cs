using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace ChristmasInDirtmouth
{
    public static class LocalizationManager
    {
        public static Dictionary<string, string> CurrentTranslations = new Dictionary<string, string>();
        private static string _lastLoadedLanguage = "";

        public static void LoadTranslations()
        {
            try
            {
                string langCode = Language.Language.CurrentLanguage().ToString().ToUpper();
                _lastLoadedLanguage = langCode;

                string modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";

                // 1. Intentar cargar desde archivo en disco (permite a los usuarios personalizar o añadir idiomas)
                string json = TryLoadFromFile(modDir, langCode);

                // 2. Si no se encuentra en disco, cargar desde los recursos embebidos en la DLL
                if (string.IsNullOrEmpty(json))
                {
                    json = TryLoadFromResource(langCode);
                }

                if (!string.IsNullOrEmpty(json))
                {
                    CurrentTranslations = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) 
                                          ?? new Dictionary<string, string>();
                    Logger.Info($"[ChristmasInDirtmouth] Traducciones cargadas para idioma: {langCode}");
                }
                else
                {
                    CurrentTranslations.Clear();
                    Logger.Warning($"[ChristmasInDirtmouth] No se encontraron archivos de traducción para {langCode}. Se usarán textos por defecto.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[ChristmasInDirtmouth] Error al cargar traducción: {ex}");
            }
        }

        private static string TryLoadFromFile(string modDir, string langCode)
        {
            string baseCode = langCode.Split('_')[0];
            string[] candidates = new string[]
            {
                Path.Combine(modDir, "Lang", $"{langCode}.json"),
                Path.Combine(modDir, $"{langCode}.json"),
                Path.Combine(modDir, "Lang", $"{baseCode}.json"),
                Path.Combine(modDir, $"{baseCode}.json"),
                Path.Combine(modDir, "Lang", "EN.json"),
                Path.Combine(modDir, "EN.json")
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }
            }
            return null;
        }

        private static string TryLoadFromResource(string langCode)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string baseCode = langCode.Split('_')[0];

            string[] candidates = new string[]
            {
                $"ChristmasInDirtmouth.Lang.{langCode}.json",
                $"ChristmasInDirtmouth.Lang.{baseCode}.json",
                "ChristmasInDirtmouth.Lang.EN.json"
            };

            foreach (var resName in candidates)
            {
                using (Stream stream = asm.GetManifestResourceStream(resName))
                {
                    if (stream != null)
                    {
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            return reader.ReadToEnd();
                        }
                    }
                }
            }
            return null;
        }

        public static bool TryGetText(string key, out string text)
        {
            if (string.IsNullOrEmpty(key))
            {
                text = null;
                return false;
            }

            // Detectar si el idioma cambió en los ajustes del juego mientras se juega
            string currentLang = Language.Language.CurrentLanguage().ToString().ToUpper();
            if (currentLang != _lastLoadedLanguage)
            {
                LoadTranslations();
                ChristmasInDirtmouth.Instance?.UpdateNPCMapFromTranslations();
            }

            if (CurrentTranslations != null && CurrentTranslations.TryGetValue(key, out text))
            {
                return true;
            }

            // Fallback a los textos originales en inglés de ModItems si no están en la traducción
            if (ModItems.ShopMap.TryGetValue(key, out text))
            {
                return true;
            }

            if (ModItems.NPCMap.TryGetValue(key, out text))
            {
                return true;
            }

            return false;
        }
    }
}
