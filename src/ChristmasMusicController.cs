using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Modding;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace ChristmasInDirtmouth
{
    /// <summary>
    /// Configuración de la música navideña para Bocasucia (Dirtmouth).
    /// </summary>
    public static class ChristmasMusicConfig
    {
        /// <summary>
        /// Lista de reproducción: rutas a los archivos de audio (.ogg).
        /// Puedes especificar nombres de archivo relativos a la carpeta del mod (ej: "SleighBells.ogg")
        /// o rutas absolutas en el disco (ej: @"C:\MiMusica\CancionNavidena.ogg").
        /// </summary>
        public static List<string> Playlist = new List<string>
        {
            "SleighBells.ogg"
        };

        /// <summary>
        /// Tiempo de espera en segundos antes de reproducir la próxima canción.
        /// Funciona tanto para múltiples canciones como para una sola pista (esperará este tiempo antes de repetirla).
        /// </summary>
        public static float DelayBetweenSongs = 2.0f;

        /// <summary>
        /// Factor de volumen base para las canciones de la lista de reproducción (0.0f a 1.0f).
        /// Los archivos comerciales/externos suelen estar masterizados a niveles más altos que la música
        /// original del juego. Por defecto se establece en 0.5f (50%) para nivelar el volumen.
        /// Puedes ajustarlo al valor que prefieras.
        /// </summary>
        public static float MusicVolume = 0.5f;
    }

    /// <summary>
    /// Controlador encargado de gestionar la música navideña en Bocasucia (Dirtmouth).
    /// Se activa cuando se compra el Árbol de Navidad más 2 objetos adicionales de la tienda.
    /// Emite a través del AudioMixer de la música original del juego, heredando su volumen
    /// y el desvanecimiento progresivo al alejarse del centro de Bocasucia.
    /// </summary>
    public class ChristmasMusicController : MonoBehaviour
    {
        public static ChristmasMusicController Instance { get; private set; }

        private AudioSource audioSource;
        private Coroutine playlistCoroutine;
        private readonly Dictionary<string, AudioClip> audioCache = new Dictionary<string, AudioClip>();

        private static readonly FieldInfo MusicSourcesField = typeof(AudioManager).GetField("musicSources", BindingFlags.NonPublic | BindingFlags.Instance);

        private int currentTrackIndex = 0;
        private bool isInTown = false;
        private bool isTreeCreditsPlaying = false;

        public static void Initialize()
        {
            if (Instance == null)
            {
                GameObject go = new GameObject("ChristmasMusicController");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<ChristmasMusicController>();
                Logger.Info("ChristmasMusicController inicializado");
            }
        }

        public static void Unload()
        {
            if (Instance != null)
            {
                Instance.Cleanup();
                Destroy(Instance.gameObject);
                Instance = null;
                Logger.Info("ChristmasMusicController descargado");
            }
        }

        private void Awake()
        {
            Instance = this;
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f; // Audio 2D estándar, igual que la música del juego

            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnActiveSceneChanged;
            On.AudioManager.BeginApplyMusicCue += OnAudioManagerBeginApplyMusicCue;

            // Comprobar escena actual si ya estábamos en Town
            Scene currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            isInTown = (currentScene.name == "Town");
            if (isInTown && IsChristmasMusicConditionMet())
            {
                StartPlaylist();
            }
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            On.AudioManager.BeginApplyMusicCue -= OnAudioManagerBeginApplyMusicCue;

            StopPlaylist();

            foreach (var clip in audioCache.Values)
            {
                if (clip != null)
                {
                    Destroy(clip);
                }
            }
            audioCache.Clear();
        }

        /// <summary>
        /// Comprueba si se cumple la condición:
        /// Haber comprado el Árbol de Navidad (índice 3 en HeroInventory) + al menos 2 objetos más de la tienda.
        /// </summary>
        public static bool IsChristmasMusicConditionMet()
        {
            if (ChristmasInDirtmouth.GlobalData == null || ChristmasInDirtmouth.GlobalData.HeroInventory == null)
            {
                return false;
            }

            bool[] inv = ChristmasInDirtmouth.GlobalData.HeroInventory;
            if (inv.Length <= 3) return false;

            // El árbol de navidad es el ítem en la posición 3
            if (!inv[3]) return false;

            // Contar cuántos otros ítems han sido comprados (excluyendo el árbol)
            int otherItems = 0;
            for (int i = 0; i < inv.Length; i++)
            {
                if (i != 3 && inv[i])
                {
                    otherItems++;
                }
            }

            return otherItems >= 2;
        }

        private void OnActiveSceneChanged(Scene from, Scene to)
        {
            isInTown = (to.name == "Town");
            isTreeCreditsPlaying = false;

            if (isInTown)
            {
                if (IsChristmasMusicConditionMet())
                {
                    StartPlaylist();
                }
            }
            else
            {
                StopPlaylist();
            }
        }

        private void Update()
        {
            if (audioSource != null)
            {
                audioSource.volume = Mathf.Clamp01(ChristmasMusicConfig.MusicVolume);
            }

            if (!isInTown) return;

            bool shouldPlay = IsChristmasMusicConditionMet() && !isTreeCreditsPlaying;

            if (shouldPlay && playlistCoroutine == null)
            {
                StartPlaylist();
            }
            else if (!shouldPlay && playlistCoroutine != null)
            {
                StopPlaylist();

                // Si la condición dejó de cumplirse (por ejemplo, al reiniciar la tienda), restaurar música original
                if (!isTreeCreditsPlaying && GameManager.instance?.AudioManager != null)
                {
                    MusicRegion mr = FindObjectOfType<MusicRegion>();
                    if (mr != null && mr.enterMusicCue != null)
                    {
                        GameManager.instance.AudioManager.ApplyMusicCue(mr.enterMusicCue, 0f, 1f, false);
                    }
                }
            }
        }

        /// <summary>
        /// Intercepta la llamada a BeginApplyMusicCue de AudioManager.
        /// Si estamos en Bocasucia (Town) y la condición navideña está activa,
        /// reemplazamos el MusicCue por uno vacío para evitar que suene la melodía original.
        /// </summary>
        private IEnumerator OnAudioManagerBeginApplyMusicCue(On.AudioManager.orig_BeginApplyMusicCue orig, AudioManager self, MusicCue musicCue, float delayTime, float transitionTime, bool applySnapshot)
        {
            if (isInTown && IsChristmasMusicConditionMet())
            {
                // Silenciar la música original de Bocasucia
                musicCue = ScriptableObject.CreateInstance<MusicCue>();
            }

            yield return orig(self, musicCue, delayTime, transitionTime, applySnapshot);
        }

        public void StartPlaylist()
        {
            if (playlistCoroutine != null) return;

            AssignMusicMixerGroup();
            playlistCoroutine = StartCoroutine(PlaylistLoop());
        }

        public void StopPlaylist()
        {
            if (playlistCoroutine != null)
            {
                StopCoroutine(playlistCoroutine);
                playlistCoroutine = null;
            }

            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
        }

        /// <summary>
        /// Conecta el AudioSource al AudioMixerGroup de la música del juego.
        /// Esto garantiza que el volumen de la música en las opciones del juego aplique directamente a esta canción,
        /// y que las transiciones de snapshot (fade out al alejarse de Bocasucia) se apliquen de forma idéntica.
        /// </summary>
        private void AssignMusicMixerGroup()
        {
            if (audioSource.outputAudioMixerGroup == null && GameManager.instance?.AudioManager != null)
            {
                AudioSource[] sources = MusicSourcesField?.GetValue(GameManager.instance.AudioManager) as AudioSource[];
                if (sources != null && sources.Length > 0 && sources[0] != null)
                {
                    audioSource.outputAudioMixerGroup = sources[0].outputAudioMixerGroup;
                    Logger.Info("ChristmasMusicController: AudioMixerGroup de música asignado con éxito.");
                }
            }
        }

        public static void SetTreeCreditsPlaying(bool playing)
        {
            if (Instance != null)
            {
                Instance.isTreeCreditsPlaying = playing;
                if (playing)
                {
                    Instance.StopPlaylist();
                }
            }
        }

        private IEnumerator PlaylistLoop()
        {
            while (isInTown && IsChristmasMusicConditionMet() && !isTreeCreditsPlaying)
            {
                AssignMusicMixerGroup();

                if (ChristmasMusicConfig.Playlist == null || ChristmasMusicConfig.Playlist.Count == 0)
                {
                    yield return new WaitForSeconds(1.0f);
                    continue;
                }

                if (currentTrackIndex >= ChristmasMusicConfig.Playlist.Count)
                {
                    currentTrackIndex = 0;
                }

                string trackName = ChristmasMusicConfig.Playlist[currentTrackIndex];
                string resolvedPath = ResolveAudioPath(trackName);

                if (string.IsNullOrEmpty(resolvedPath))
                {
                    Logger.Warning($"ChristmasMusicController: No se pudo encontrar el archivo de audio: '{trackName}'");
                    currentTrackIndex = (currentTrackIndex + 1) % ChristmasMusicConfig.Playlist.Count;
                    yield return new WaitForSeconds(1.0f);
                    continue;
                }

                AudioClip clipToPlay = null;
                yield return LoadAudioClip(resolvedPath, (clip) => clipToPlay = clip);

                if (clipToPlay == null)
                {
                    currentTrackIndex = (currentTrackIndex + 1) % ChristmasMusicConfig.Playlist.Count;
                    yield return new WaitForSeconds(1.0f);
                    continue;
                }

                audioSource.clip = clipToPlay;
                audioSource.loop = false;
                audioSource.volume = Mathf.Clamp01(ChristmasMusicConfig.MusicVolume);
                audioSource.Play();

                Logger.Info($"ChristmasMusicController: Reproduciendo pista [{currentTrackIndex + 1}/{ChristmasMusicConfig.Playlist.Count}]: '{clipToPlay.name}'");

                // Esperar a que la pista termine de sonar
                while (audioSource != null && audioSource.isPlaying && isInTown && IsChristmasMusicConditionMet() && !isTreeCreditsPlaying)
                {
                    yield return null;
                }

                // Si el estado cambió durante la reproducción, detener y salir
                if (!isInTown || !IsChristmasMusicConditionMet() || isTreeCreditsPlaying)
                {
                    if (audioSource != null && audioSource.isPlaying)
                    {
                        audioSource.Stop();
                    }
                    playlistCoroutine = null;
                    yield break;
                }

                // Avanzar a la siguiente pista de la lista de reproducción
                currentTrackIndex = (currentTrackIndex + 1) % ChristmasMusicConfig.Playlist.Count;

                // Esperar el tiempo configurado antes de reproducir la próxima canción (incluso si solo hay una canción)
                if (ChristmasMusicConfig.DelayBetweenSongs > 0f)
                {
                    float elapsed = 0f;
                    while (elapsed < ChristmasMusicConfig.DelayBetweenSongs && isInTown && IsChristmasMusicConditionMet() && !isTreeCreditsPlaying)
                    {
                        elapsed += Time.unscaledDeltaTime;
                        yield return null;
                    }
                }
            }

            playlistCoroutine = null;
        }

        private IEnumerator LoadAudioClip(string filePath, Action<AudioClip> onLoaded)
        {
            if (audioCache.TryGetValue(filePath, out AudioClip cachedClip) && cachedClip != null)
            {
                onLoaded?.Invoke(cachedClip);
                yield break;
            }

            string uri = new Uri(filePath).AbsoluteUri;
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.OGGVORBIS))
            {
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Logger.Error($"ChristmasMusicController: Error al cargar audio desde '{filePath}': {www.error}");
                    onLoaded?.Invoke(null);
                }
                else
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                    if (clip != null)
                    {
                        clip.name = Path.GetFileNameWithoutExtension(filePath);
                        audioCache[filePath] = clip;
                        Logger.Info($"ChristmasMusicController: Clip cargado '{clip.name}' ({clip.length:F1}s)");
                        onLoaded?.Invoke(clip);
                    }
                    else
                    {
                        Logger.Error($"ChristmasMusicController: AudioClip nulo recibido de '{filePath}'");
                        onLoaded?.Invoke(null);
                    }
                }
            }
        }

        /// <summary>
        /// Resuelve la ruta del archivo de audio buscando en múltiples ubicaciones probables:
        /// 1. Ruta absoluta directa
        /// 2. Carpeta de instalación del mod (Managed/Mods/ChristmasInDirtmouth/)
        /// 3. Directorio base del juego
        /// 4. Carpeta de desarrollo / src
        /// </summary>
        public static string ResolveAudioPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            // 1. Ruta absoluta directa
            if (Path.IsPathRooted(path) && File.Exists(path))
            {
                return path;
            }

            // 2. Carpeta de instalación del mod
            string modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(modDir))
            {
                string inModDir = Path.Combine(modDir, path);
                if (File.Exists(inModDir))
                {
                    return inModDir;
                }
            }

            // 3. Directorio base de la aplicación
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir))
            {
                string inBaseDir = Path.Combine(baseDir, path);
                if (File.Exists(inBaseDir))
                {
                    return inBaseDir;
                }
            }

            // 4. Directorio de trabajo actual
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }

            // 5. Búsqueda en carpetas relativas habituales durante desarrollo
            if (!string.IsNullOrEmpty(modDir))
            {
                string devPath = Path.Combine(modDir, "..", "..", "src", path);
                if (File.Exists(devPath))
                {
                    return Path.GetFullPath(devPath);
                }
            }

            return null;
        }
    }
}
