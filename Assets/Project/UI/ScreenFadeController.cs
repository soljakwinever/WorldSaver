using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project.UI
{
    /// <summary>
    /// Owns the lightweight loading scene, asynchronously loads the requested scene,
    /// and keeps it covered until the initial world chunks are ready.
    /// </summary>
    public sealed class ScreenFadeController : MonoBehaviour
    {
        public const string LoadingSceneName = "LoadingScene";

        private const string DefaultTargetScene = "SampleScene";
        private const int MinimumLoadedChunks = 6;
        private const float FadeDuration = 0.4f;
        private const float VisibleProgressStart = 0.9f;
        private const float LoadingBackdropOpacity = 0.85f;

        [SerializeField] private ParticleSystem loadingParticles;

        private static string _targetScene;

        private float _fadeAlpha;
        private float _progress;
        private GUIStyle _labelStyle;

        public static void LoadScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogError("Cannot load a scene without a scene name.");
                return;
            }

            _targetScene = sceneName;
            SceneManager.LoadScene(LoadingSceneName);
        }

        private IEnumerator Start()
        {
            PlayLoadingParticles();

            string targetScene = string.IsNullOrEmpty(_targetScene)
                ? DefaultTargetScene
                : _targetScene;
            _targetScene = null;

            yield return FadeOut();

            AsyncOperation loadOperation =
                SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Additive);
            if (loadOperation == null)
            {
                Debug.LogError($"Could not start loading scene '{targetScene}'.");
                yield break;
            }

            loadOperation.allowSceneActivation = false;
            while (loadOperation.progress < 0.9f)
            {
                _progress = Mathf.Clamp01(loadOperation.progress / 0.9f) *
                            VisibleProgressStart;
                yield return null;
            }

            _progress = VisibleProgressStart;
            loadOperation.allowSceneActivation = true;
            while (!loadOperation.isDone)
                yield return null;

            Scene gameScene = SceneManager.GetSceneByName(targetScene);
            if (gameScene.IsValid())
                SceneManager.SetActiveScene(gameScene);

            Chunkloader chunkLoader = null;
            while (chunkLoader == null)
            {
                chunkLoader = FindFirstObjectByType<Chunkloader>();
                yield return null;
            }

            while (chunkLoader.LoadedChunks < MinimumLoadedChunks)
            {
                _progress = Mathf.Lerp(
                    VisibleProgressStart,
                    1f,
                    chunkLoader.LoadedChunks / (float)MinimumLoadedChunks);
                yield return null;
            }

            _progress = 1f;
            yield return StopAndDestroyLoadingParticles();
            yield return FadeIn();

            Scene loadingScene = gameObject.scene;
            if (loadingScene.IsValid() && loadingScene.isLoaded)
                SceneManager.UnloadSceneAsync(loadingScene);
        }

        private void PlayLoadingParticles()
        {
            if (loadingParticles == null)
            {
                Debug.LogWarning(
                    "Loading particle system is not assigned; continuing without it.",
                    this);
                return;
            }

            foreach (ParticleSystem particle in
                     loadingParticles.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particle.main;
                main.useUnscaledTime = true;
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            loadingParticles.Play(withChildren: true);
        }

        private IEnumerator StopAndDestroyLoadingParticles()
        {
            if (loadingParticles == null)
                yield break;

            ParticleSystem[] particles =
                loadingParticles.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem particle in particles)
            {
                ParticleSystem.MainModule main = particle.main;
                main.loop = false;
                main.ringBufferMode = ParticleSystemRingBufferMode.Disabled;
                particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            while (loadingParticles != null && loadingParticles.IsAlive(true))
                yield return null;

            if (loadingParticles != null)
            {
                GameObject particleRoot = loadingParticles.gameObject;
                loadingParticles = null;
                Destroy(particleRoot);
            }
        }

        private IEnumerator FadeOut()
        {
            yield return Fade(0f, 1f);
        }

        private IEnumerator FadeIn()
        {
            yield return Fade(1f, 0f);
        }

        private IEnumerator Fade(float from, float to)
        {
            _fadeAlpha = from;
            float elapsed = 0f;
            while (elapsed < FadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                _fadeAlpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / FadeDuration));
                yield return null;
            }

            _fadeAlpha = to;
        }

        private void OnGUI()
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(
                0.025f, 0.045f, 0.04f,
                _fadeAlpha * LoadingBackdropOpacity);
            GUI.DrawTexture(
                new Rect(0f, 0f, Screen.width, Screen.height),
                Texture2D.whiteTexture);

            if (_fadeAlpha > 0.01f)
            {
                EnsureStyle();

                float width = Mathf.Min(520f, Screen.width * 0.7f);
                float height = 18f;
                float x = (Screen.width - width) * 0.5f;
                float y = Screen.height * 0.72f;

                GUI.color = new Color(1f, 1f, 1f, _fadeAlpha);
                GUI.Label(
                    new Rect(x, y - 42f, width, 30f),
                    "LOADING WORLD",
                    _labelStyle);

                GUI.color = new Color(0.12f, 0.18f, 0.16f, _fadeAlpha);
                GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
                GUI.color = new Color(0.45f, 0.82f, 0.48f, _fadeAlpha);
                float visibleProgress = Mathf.InverseLerp(
                    VisibleProgressStart, 1f, _progress);
                GUI.DrawTexture(
                    new Rect(x + 2f, y + 2f,
                        (width - 4f) * visibleProgress, height - 4f),
                    Texture2D.whiteTexture);
            }

            GUI.color = previousColor;
        }

        private void EnsureStyle()
        {
            if (_labelStyle != null)
                return;

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
        }
    }
}
