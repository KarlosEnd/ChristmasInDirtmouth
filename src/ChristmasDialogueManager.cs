using System;
using System.Collections.Generic;
using System.Reflection;
using Modding;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChristmasInDirtmouth
{
    public static class ChristmasDialogueManager
    {
        public const int DecorationCount = 5;

        private static readonly FieldInfo TextMeshField = typeof(DialogueBox).GetField("textMesh", BindingFlags.NonPublic | BindingFlags.Instance);
        public static readonly string[] DecorationNames = new string[]
        {
            "Garland",   // 0
            "Presents",  // 1
            "Candles",   // 2
            "Tree",      // 3
            "Lights"     // 4
        };

        public static void Initialize()
        {
            On.DialogueBox.SetConversation += OnDialogueBoxSetConversation;
            Logger.Info("[ChristmasDialogue] ChristmasDialogueManager inicializado con éxito.");
        }

        public static void Unload()
        {
            On.DialogueBox.SetConversation -= OnDialogueBoxSetConversation;
            Logger.Info("[ChristmasDialogue] ChristmasDialogueManager descargado.");
        }

        public static bool IsDirtmouthScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;

            return sceneName == "Town" ||
                   sceneName == "Room_shop" ||
                   sceneName == "Room_mapper" ||
                   sceneName == "Room_Ouiji" ||
                   sceneName == "Room_Bretta" ||
                   sceneName == "Grimm_Main_Tent" ||
                   sceneName == "Grimm_Divine";
        }

        public static string IdentifyNpc(string convName, string sheetTitle, string sceneName)
        {
            string cUpper = (convName ?? "").ToUpperInvariant();
            string sUpper = (sheetTitle ?? "").ToUpperInvariant();

            // 1. Descartar los objetos del entorno basándonos en los logs
            if (sUpper == "PROMPTS" || cUpper.Contains("DOOR") || cUpper.Contains("KEY") || cUpper.Contains("INSPECT"))
            {
                return null;
            }

            // Los diálogos de la propia vendedora del mod nunca deben ser sustituidos
            if (cUpper.StartsWith("MERRY") || sUpper.StartsWith("MERRY")) return null;

            // 2. EL FILTRO DEFINITIVO: Solo interceptar saludos y diálogos repetitivos de relleno
            if (!cUpper.Contains("GENERIC") && 
                !cUpper.Contains("REPEAT") && 
                !cUpper.Contains("GREET"))
            {
                return null; 
            }

            // 3. Identificación limpia del NPC usando el SheetTitle
            if (sceneName == "Town")
            {
                if (sUpper.Contains("ELDERBUG")) return "ELDERBUG";
                if (sUpper.Contains("ZOTE")) return "ZOTE";
                if (sUpper.Contains("BRETTA")) return "BRETTA";
                if (sUpper.Contains("TISO")) return "TISO";
                if (sUpper.Contains("SLY")) return "SLY";
                return null;
            }

            if (sceneName == "Room_Ouiji")
            {
                if (sUpper.Contains("JINN")) return "JINN";
                if (sUpper.Contains("JIJI")) return "JIJI";
                return null;
            }

            if (sceneName == "Room_mapper")
            {
                if (sUpper.Contains("ISELDA")) return "ISELDA";
                return null;
            }

            if (sceneName == "Room_shop") return "SLY";
            if (sceneName == "Room_Bretta") return "BRETTA";
            
            if (sceneName == "Grimm_Main_Tent") 
            {
                return sUpper.Contains("BRUMM") ? "BRUMM" : null;
            }
            
            if (sceneName == "Grimm_Divine") return "DIVINE";

            return null;
        }

        private static void OnDialogueBoxSetConversation(On.DialogueBox.orig_SetConversation orig, DialogueBox self, string convName, string sheetTitle)
        {
            orig(self, convName, sheetTitle);

            try
            {
                string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

                if (!IsDirtmouthScene(sceneName)) return;

                string npcId = IdentifyNpc(convName, sheetTitle, sceneName);

                if (!string.IsNullOrEmpty(npcId))
                {
                    if (TryGetPendingChristmasDialogue(npcId, out string xmasDialogue))
                    {
                        Logger.Info($"[ChristmasDialogue] Inyectando diálogo festivo para [{npcId}]:\n{xmasDialogue}");
                        TextMeshPro tmp = (TextMeshField?.GetValue(self) as TextMeshPro) ?? self.GetComponentInChildren<TextMeshPro>();
                        if (tmp != null)
                        {
                            tmp.text = xmasDialogue;
                            tmp.ForceMeshUpdate();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[ChristmasDialogue] Error al inyectar diálogo: {ex}");
            }
        }

        private static bool TryGetLine(string localizationKey, out string text)
        {
            text = null;
            return LocalizationManager.TryGetText(localizationKey, out text) && !string.IsNullOrEmpty(text);
        }

        public static bool TryGetPendingChristmasDialogue(string npcId, out string dialogueText)
        {
            dialogueText = null;

            var data = ChristmasInDirtmouth.GlobalData;
            if (data == null || data.HeroInventory == null) return false;

            if (data.SeenChristmasDialogues == null)
            {
                data.SeenChristmasDialogues = new List<string>();
            }

            List<string> seen = data.SeenChristmasDialogues;

            int limit = Math.Min(data.HeroInventory.Length, DecorationCount);
            int activeCount = 0;
            for (int i = 0; i < limit; i++)
            {
                if (data.HeroInventory[i]) activeCount++;
            }
            
            bool allActive = limit == DecorationCount && activeCount == DecorationCount;

            // 1. DIÁLOGO INTRO (Cero decoraciones activas)
            if (activeCount == 0)
            {
                string introId = $"{npcId}_INTRO";
                if (!seen.Contains(introId))
                {
                    // Lo marcamos incluso si no tiene línea definida, para no trabarnos
                    seen.Add(introId);
                    
                    if (TryGetLine($"XMAS_{npcId}_INTRO", out string introText))
                    {
                        dialogueText = introText;
                        return true;
                    }
                }
                return false;
            }

            // 2. DIÁLOGO FINAL / ALL (Todas las decoraciones activas)
            // Evaluamos esto ANTES de los adornos individuales para que, si el jugador
            // enciende todo de golpe (o carga un save tardío), el NPC diga su línea final.
            if (allActive)
            {
                string allId = $"{npcId}_ALL";
                if (!seen.Contains(allId))
                {
                    seen.Add(allId);
                    
                    // Si activa el final, marcamos todos los individuales como vistos
                    // para que no los diga después de forma desordenada.
                    for (int i = 0; i < DecorationCount; i++)
                    {
                        string decorId = $"{npcId}_DECOR_{i}";
                        if (!seen.Contains(decorId)) seen.Add(decorId);
                    }

                    if (TryGetLine($"XMAS_{npcId}_ALL", out string allText))
                    {
                        dialogueText = allText;
                        return true;
                    }
                }
            }

            // 3. DIÁLOGOS DE DECORACIONES INDIVIDUALES (1 por interacción)
            for (int i = 0; i < limit; i++)
            {
                if (!data.HeroInventory[i]) continue;

                string dialogueKeyId = $"{npcId}_DECOR_{i}";
                if (seen.Contains(dialogueKeyId)) continue;

                seen.Add(dialogueKeyId);

                if (TryGetLine($"XMAS_{npcId}_DECOR_{i}", out string text))
                {
                    dialogueText = text;
                    return true;
                }
            }

            return false;
        }
    }
}