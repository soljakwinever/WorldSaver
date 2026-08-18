using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Project.Scripts;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Project.UI.MainMenu
{
    /// <summary>
    /// Standalone entry scene for creating/loading worlds and editing player settings.
    /// The UI is intentionally generated at runtime so the scene has no fragile object references.
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        public const string ActiveWorldKey = "WorldSaver.ActiveWorld";
        public const string ActiveSeedKey = "WorldSaver.ActiveSeed";
        public const string ActivePresetIdKey = "WorldSaver.ActivePresetId";
        public const string ActivePresetVersionKey = "WorldSaver.ActivePresetVersion";
        public const string BindingOverridesKey = "WorldSaver.BindingOverrides";

        private const string GameScene = "SampleScene";
        private const int WindowWidth = 620;
        private const int WindowHeight = 680;

        private enum Page { Main, NewWorld, LoadWorld, Options }

        [Serializable]
        private sealed class WorldManifest
        {
            public string name;
            public int seed;
            public string createdUtc;
            public string presetId;
            public int presetVersion = 1;
            public string characterId;
        }

        private Page _page;
        private string _newWorldName = string.Empty;
        private string _message = string.Empty;
        private Vector2 _scroll;
        private Resolution[] _resolutions;
        private int _resolutionIndex;
        private int _qualityIndex;
        private bool _fullScreen;
        private bool _vSync;
        private bool _showFps;
        private Inputs _inputs;
        private InputActionRebindingExtensions.RebindingOperation _rebind;
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _messageStyle;
        private WorldGenerationPresetCatalogData _presetCatalog;
        private WorldGenerationPresetData[] _newWorldPresets =
            Array.Empty<WorldGenerationPresetData>();
        private int _newWorldPresetIndex;
        private CharacterCreationCatalogData _characterCatalog;
        [SerializeField] private MainMenuWorldPreviewController worldPreview;
        private IReadOnlyList<CharacterProfile> _characters = Array.Empty<CharacterProfile>();
        private int _selectedCharacterIndex = -1;
        private bool _creatingCharacter;
        private string _characterName = string.Empty;
        private CharacterGender _characterGender = CharacterGender.Other;
        private int _speciesIndex;
        private int _classIndex;
        private int _bodyIndex;
        private int _hairIndex;
        private int _hornsIndex;
        private int _clothesIndex;
        private int _wingsIndex;
        private string _skinColorA = "#D1A680";
        private string _skinColorB = "#9E6B4D";
        private string _eyeColor = "#3388FF";
        private string _clothingColorA = "#808080";
        private string _clothingColorB = "#202020";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RestoreDisplaySettings()
        {
            QualitySettings.vSyncCount = PlayerPrefs.GetInt("WorldSaver.VSync", 1);
            int quality = PlayerPrefs.GetInt("WorldSaver.Quality", QualitySettings.GetQualityLevel());
            QualitySettings.SetQualityLevel(Mathf.Clamp(quality, 0, QualitySettings.names.Length - 1), true);
        }

        private void Awake()
        {
            _inputs = new Inputs();
            string overrides = PlayerPrefs.GetString(BindingOverridesKey, string.Empty);
            if (!string.IsNullOrEmpty(overrides))
                _inputs.asset.LoadBindingOverridesFromJson(overrides);

            _resolutions = Screen.resolutions
                .GroupBy(r => (r.width, r.height))
                .Select(group => group.Last())
                .ToArray();
            if (_resolutions.Length == 0)
                _resolutions = new[] { Screen.currentResolution };

            _resolutionIndex = FindCurrentResolution();
            _qualityIndex = QualitySettings.GetQualityLevel();
            _fullScreen = Screen.fullScreen;
            _vSync = QualitySettings.vSyncCount > 0;
            _showFps = PlayerPrefs.GetInt("WorldSaver.ShowFps", 1) != 0;
            _presetCatalog =
                Resources.Load<WorldGenerationPresetCatalogData>(
                    "WorldGeneration/PresetCatalog");
            _newWorldPresets = (_presetCatalog?.presets ??
                                Array.Empty<WorldGenerationPresetData>())
                .Where(preset => preset != null && preset.AvailableForNewWorlds)
                .OrderBy(preset => preset.DisplayName, StringComparer.Ordinal)
                .ToArray();
            if (_newWorldPresets.Length == 0 &&
                _presetCatalog?.newWorldDefault != null)
            {
                _newWorldPresets = new[] { _presetCatalog.newWorldDefault };
            }
            _characterCatalog = CharacterCreationCatalogData.LoadOrFallback();
            RefreshCharacters();

            worldPreview ??= GetComponent<MainMenuWorldPreviewController>();
            worldPreview?.Initialize(_presetCatalog?.newWorldDefault);
        }

        private void OnDestroy()
        {
            _rebind?.Cancel();
            _rebind?.Dispose();
            _inputs?.Dispose();
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawBackdrop();

            Rect window = new(
                (Screen.width - WindowWidth) * 0.5f,
                (Screen.height - WindowHeight) * 0.5f,
                WindowWidth,
                WindowHeight);
            GUILayout.BeginArea(window, GUI.skin.window);
            GUILayout.Space(18);
            GUILayout.Label("MY WORLD", _titleStyle);
            GUILayout.Label(Application.version, _subtitleStyle);
            GUILayout.Label(PageTitle(), _subtitleStyle);
            GUILayout.Space(20);

            switch (_page)
            {
                case Page.Main: DrawMain(); break;
                case Page.NewWorld: DrawNewWorld(); break;
                case Page.LoadWorld: DrawLoadWorld(); break;
                case Page.Options: DrawOptions(); break;
            }

            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(_message))
                GUILayout.Label(_message, _messageStyle);
            GUILayout.Space(10);
            GUILayout.EndArea();
        }

        private void DrawMain()
        {
            if (MenuButton("NEW GAME")) Open(Page.NewWorld);
            if (MenuButton("LOAD WORLD")) Open(Page.LoadWorld);
            if (MenuButton("OPTIONS")) Open(Page.Options);
            GUILayout.Space(18);
            if (MenuButton("QUIT")) Application.Quit();
        }

        private void DrawNewWorld()
        {
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(500));
            GUILayout.Label("WORLD NAME");
            GUI.SetNextControlName("WorldName");
            _newWorldName = GUILayout.TextField(_newWorldName, 48, GUILayout.Height(38));
            GUILayout.Space(8);
            GUILayout.Label("The world name is also used to create a deterministic generation seed.");
            if (_newWorldPresets.Length > 0)
            {
                GUILayout.Space(12);
                GUILayout.Label("WORLD TYPE");
                string[] names = _newWorldPresets
                    .Select(preset => preset.DisplayName)
                    .ToArray();
                _newWorldPresetIndex = GUILayout.SelectionGrid(
                    Mathf.Clamp(_newWorldPresetIndex, 0, names.Length - 1),
                    names,
                    1);
            }
            GUILayout.Space(14);
            DrawCharacterSelection();
            GUILayout.Space(14);

            GUI.enabled = !string.IsNullOrWhiteSpace(_newWorldName) &&
                          (_selectedCharacterIndex >= 0 ||
                           (_creatingCharacter && CanCreateCharacter()));
            if (MenuButton("CREATE WORLD"))
                CreateWorld(_newWorldName);
            GUI.enabled = true;
            BackButton();
            GUILayout.EndScrollView();
        }

        private void DrawCharacterSelection()
        {
            GUILayout.Label("CHARACTER");
            if (!_creatingCharacter)
            {
                if (_characters.Count > 0)
                {
                    string[] names = _characters.Select(x => $"{x.name}  ({DisplaySpecies(x.speciesId)} {DisplayClass(x.classId)})").ToArray();
                    _selectedCharacterIndex = GUILayout.SelectionGrid(
                        Mathf.Clamp(_selectedCharacterIndex, 0, names.Length - 1), names, 1);
                }
                else GUILayout.Label("No characters created yet.");
                if (GUILayout.Button("CREATE NEW CHARACTER", GUILayout.Height(34)))
                {
                    _creatingCharacter = true;
                    _selectedCharacterIndex = -1;
                }
                return;
            }

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("NAME");
            _characterName = GUILayout.TextField(_characterName, 48, GUILayout.Height(30));
            GUILayout.Label("GENDER");
            _characterGender = (CharacterGender)GUILayout.SelectionGrid((int)_characterGender, Enum.GetNames(typeof(CharacterGender)), 3);
            DrawDefinitionGrid("SPECIES", _characterCatalog.species?.Select(x => x.displayName).ToArray(), ref _speciesIndex);

            string[] classNames = (_characterCatalog.classes ?? Array.Empty<CharacterClassDefinition>())
                .Select(x => x.available ? x.displayName : $"{x.displayName} (Coming Soon)").ToArray();
            DrawDefinitionGrid("CLASS", classNames, ref _classIndex);

            CharacterSpeciesDefinition species = SelectedSpecies();
            GUILayout.Label("APPEARANCE (SPRITES COMING SOON)");
            DrawAppearanceGrid("Body", _characterCatalog.bodyTypes, ref _bodyIndex);
            DrawAppearanceGrid("Hair", _characterCatalog.hair, ref _hairIndex);
            DrawAppearanceGrid("Clothes", _characterCatalog.clothes, ref _clothesIndex);
            if (species?.supportsHorns == true) DrawAppearanceGrid("Horns", _characterCatalog.horns, ref _hornsIndex);
            if (species?.supportsWings == true) DrawAppearanceGrid("Wings", _characterCatalog.wings, ref _wingsIndex);
            _skinColorA = DrawColorField("Skin A", _skinColorA);
            if (species?.supportsSecondarySkinColor == true)
                _skinColorB = DrawColorField("Skin B", _skinColorB);
            _eyeColor = DrawColorField("Eyes", _eyeColor);
            _clothingColorA = DrawColorField("Clothing A", _clothingColorA);
            _clothingColorB = DrawColorField("Clothing B", _clothingColorB);
            if (GUILayout.Button("CANCEL NEW CHARACTER")) _creatingCharacter = false;
            GUILayout.EndVertical();
        }

        private static void DrawDefinitionGrid(string label, string[] names, ref int index)
        {
            if (names == null || names.Length == 0) return;
            GUILayout.Label(label);
            index = GUILayout.SelectionGrid(Mathf.Clamp(index, 0, names.Length - 1), names, Mathf.Min(3, names.Length));
        }

        private static void DrawAppearanceGrid(string label, CharacterAppearanceOption[] options, ref int index)
        {
            if (options == null || options.Length == 0) { GUILayout.Label($"{label}: Default"); return; }
            string[] names = options.Select(x => string.IsNullOrWhiteSpace(x.displayName) ? x.id : x.displayName).ToArray();
            DrawDefinitionGrid(label, names, ref index);
        }

        private static string DrawColorField(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(100));
            value = GUILayout.TextField(value ?? string.Empty, 9);
            GUILayout.EndHorizontal();
            return value;
        }

        private bool CanCreateCharacter()
        {
            CharacterClassDefinition selectedClass = SelectedClass();
            return !string.IsNullOrWhiteSpace(_characterName) && SelectedSpecies() != null &&
                   selectedClass?.available == true &&
                   TryParseColor(_skinColorA, out _) && TryParseColor(_skinColorB, out _) &&
                   TryParseColor(_eyeColor, out _) && TryParseColor(_clothingColorA, out _) &&
                   TryParseColor(_clothingColorB, out _);
        }

        private CharacterSpeciesDefinition SelectedSpecies() =>
            _characterCatalog.species != null && _characterCatalog.species.Length > 0
                ? _characterCatalog.species[Mathf.Clamp(_speciesIndex, 0, _characterCatalog.species.Length - 1)] : null;

        private CharacterClassDefinition SelectedClass() =>
            _characterCatalog.classes != null && _characterCatalog.classes.Length > 0
                ? _characterCatalog.classes[Mathf.Clamp(_classIndex, 0, _characterCatalog.classes.Length - 1)] : null;

        private string DisplaySpecies(string id) => _characterCatalog.TryGetSpecies(id, out CharacterSpeciesDefinition value) ? value.displayName : id;
        private string DisplayClass(string id) => _characterCatalog.TryGetClass(id, out CharacterClassDefinition value) ? value.displayName : id;

        private void RefreshCharacters()
        {
            _characters = CharacterProfileStore.ReadAll();
            _selectedCharacterIndex = _characters.Count > 0 ? 0 : -1;
        }

        private CharacterProfile CreatePendingCharacter()
        {
            CharacterSpeciesDefinition species = SelectedSpecies();
            CharacterClassDefinition selectedClass = SelectedClass();
            CharacterProfile profile = new()
            {
                name = _characterName,
                gender = _characterGender,
                speciesId = species.id,
                classId = selectedClass.id,
                growthRanks = selectedClass.growthRanks,
                appearance = new CharacterAppearance
                {
                    body = OptionId(_characterCatalog.bodyTypes, _bodyIndex),
                    hair = OptionId(_characterCatalog.hair, _hairIndex),
                    horns = species.supportsHorns ? OptionId(_characterCatalog.horns, _hornsIndex) : string.Empty,
                    clothes = OptionId(_characterCatalog.clothes, _clothesIndex),
                    wings = species.supportsWings ? OptionId(_characterCatalog.wings, _wingsIndex) : string.Empty,
                    skinColorA = ParseColor(_skinColorA),
                    skinColorB = ParseColor(_skinColorB),
                    eyeColor = ParseColor(_eyeColor),
                    clothingColorA = ParseColor(_clothingColorA),
                    clothingColorB = ParseColor(_clothingColorB)
                }
            };
            return CharacterProfileStore.Create(profile);
        }

        private static string OptionId(CharacterAppearanceOption[] options, int index) =>
            options != null && options.Length > 0 ? options[Mathf.Clamp(index, 0, options.Length - 1)]?.id ?? string.Empty : string.Empty;

        private static bool TryParseColor(string value, out Color color) =>
            ColorUtility.TryParseHtmlString(value?.Trim(), out color);

        private static Color ParseColor(string value) =>
            TryParseColor(value, out Color color) ? color : Color.white;

        private void DrawLoadWorld()
        {
            List<WorldManifest> worlds = ReadWorlds();
            if (worlds.Count == 0)
            {
                GUILayout.Label("No saved worlds yet.", _subtitleStyle);
            }
            else
            {
                _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(460));
                foreach (WorldManifest world in worlds)
                {
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label(world.name, _subtitleStyle);
                    GUILayout.Label($"Seed: {world.seed}");
                    bool canResolve = TryResolveManifestPreset(
                        world,
                        out WorldGenerationPresetData preset);
                    GUILayout.Label(
                        canResolve
                            ? $"World Type: {preset.DisplayName}"
                            : $"Missing World Type: {world.presetId} v{world.presetVersion}");
                    GUI.enabled = canResolve;
                    if (GUILayout.Button("PLAY", GUILayout.Height(34)))
                        LaunchWorld(
                            world.name,
                            world.seed,
                            preset.PersistentId,
                            preset.Version,
                            string.IsNullOrWhiteSpace(world.characterId)
                                ? "player"
                                : world.characterId);
                    GUI.enabled = true;
                    GUILayout.EndVertical();
                    GUILayout.Space(8);
                }
                GUILayout.EndScrollView();
            }
            BackButton();
        }

        private void DrawOptions()
        {
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(500));
            GUILayout.Label("GRAPHICS", _subtitleStyle);
            _vSync = GUILayout.Toggle(_vSync, " V Sync");
            _fullScreen = GUILayout.Toggle(_fullScreen, " Fullscreen");
            _showFps = GUILayout.Toggle(_showFps, " Show FPS counter");
            GUILayout.Space(8);

            GUILayout.Label($"Resolution: {_resolutions[_resolutionIndex].width} x {_resolutions[_resolutionIndex].height}");
            _resolutionIndex = Mathf.RoundToInt(GUILayout.HorizontalSlider(
                _resolutionIndex, 0, _resolutions.Length - 1));

            GUILayout.Label($"Quality: {QualitySettings.names[_qualityIndex]}");
            _qualityIndex = Mathf.RoundToInt(GUILayout.HorizontalSlider(
                _qualityIndex, 0, QualitySettings.names.Length - 1));
            GUILayout.Space(10);
            if (GUILayout.Button("APPLY GRAPHICS", GUILayout.Height(36)))
                ApplyGraphics();

            GUILayout.Space(24);
            GUILayout.Label("INPUT BINDINGS", _subtitleStyle);
            GUILayout.Label("Select a binding, then press a new key or button. Escape cancels.");
            GUILayout.Space(8);
            DrawBindings();
            GUILayout.Space(8);
            if (GUILayout.Button("RESET ALL BINDINGS", GUILayout.Height(34)))
            {
                _inputs.asset.RemoveAllBindingOverrides();
                SaveBindings();
                _message = "Input bindings restored to defaults.";
            }
            GUILayout.EndScrollView();
            BackButton();
        }

        private void DrawBindings()
        {
            foreach (InputAction action in _inputs.asset.FindActionMap("Player", true).actions)
            {
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    InputBinding binding = action.bindings[i];
                    if (binding.isComposite ||
                        string.Equals(action.name, "MousePosition", StringComparison.Ordinal))
                        continue;

                    string label = binding.isPartOfComposite
                        ? $"{action.name} {binding.name}"
                        : action.name;
                    string value = action.GetBindingDisplayString(i);

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(label, GUILayout.Width(210));
                    GUI.enabled = _rebind == null;
                    if (GUILayout.Button(
                            _rebind != null ? "Waiting for input..." : value,
                            GUILayout.Height(30)))
                        StartRebind(action, i);
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }
            }
        }

        private void StartRebind(InputAction action, int bindingIndex)
        {
            _message = $"Press a new control for {action.name}…";
            action.Disable();
            _rebind = action.PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough("<Keyboard>/escape")
                .OnCancel(operation => FinishRebind(action, operation, false))
                .OnComplete(operation => FinishRebind(action, operation, true))
                .Start();
        }

        private void FinishRebind(
            InputAction action,
            InputActionRebindingExtensions.RebindingOperation operation,
            bool save)
        {
            operation.Dispose();
            _rebind = null;
            action.Enable();
            if (save)
            {
                SaveBindings();
                _message = $"{action.name} binding updated.";
            }
            else
            {
                _message = "Rebinding cancelled.";
            }
        }

        private void SaveBindings()
        {
            PlayerPrefs.SetString(BindingOverridesKey, _inputs.asset.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        private void ApplyGraphics()
        {
            Resolution resolution = _resolutions[_resolutionIndex];
            Screen.SetResolution(resolution.width, resolution.height, _fullScreen);
            QualitySettings.vSyncCount = _vSync ? 1 : 0;
            QualitySettings.SetQualityLevel(_qualityIndex, true);
            PlayerPrefs.SetInt("WorldSaver.VSync", QualitySettings.vSyncCount);
            PlayerPrefs.SetInt("WorldSaver.Quality", _qualityIndex);
            PlayerPrefs.SetInt("WorldSaver.ShowFps", _showFps ? 1 : 0);
            PlayerPrefs.Save();
            _message = "Graphics settings applied.";
        }

        private void CreateWorld(string requestedName)
        {
            string name = SanitizeName(requestedName);
            if (string.IsNullOrWhiteSpace(name))
            {
                _message = "Please enter a valid world name.";
                return;
            }

            string directory = WorldDirectory(name);
            if (Directory.Exists(directory))
            {
                _message = "A world with that name already exists.";
                return;
            }

            int seed = StableSeed(name);
            WorldGenerationPresetData preset =
                _newWorldPresets.Length > 0
                    ? _newWorldPresets[Mathf.Clamp(
                        _newWorldPresetIndex,
                        0,
                        _newWorldPresets.Length - 1)]
                    : _presetCatalog?.newWorldDefault;
            CharacterProfile character;
            try
            {
                character = _creatingCharacter
                    ? CreatePendingCharacter()
                    : _characters[Mathf.Clamp(_selectedCharacterIndex, 0, _characters.Count - 1)];
            }
            catch (Exception exception)
            {
                _message = exception.Message;
                return;
            }
            Directory.CreateDirectory(directory);
            WorldManifest manifest = new()
            {
                name = name,
                seed = seed,
                createdUtc = DateTime.UtcNow.ToString("O"),
                presetId = preset?.PersistentId ?? string.Empty,
                presetVersion = preset?.Version ?? 1,
                characterId = character.id
            };
            File.WriteAllText(
                Path.Combine(directory, "world.json"),
                JsonUtility.ToJson(manifest, true));
            LaunchWorld(
                name,
                seed,
                manifest.presetId,
                manifest.presetVersion,
                manifest.characterId);
        }

        private static void LaunchWorld(
            string name,
            int seed,
            string presetId,
            int presetVersion,
            string characterId = "player")
        {
            PlayerPrefs.SetString(ActiveWorldKey, name);
            PlayerPrefs.SetInt(ActiveSeedKey, seed);
            PlayerPrefs.SetString(ActivePresetIdKey, presetId ?? string.Empty);
            PlayerPrefs.SetInt(
                ActivePresetVersionKey,
                Mathf.Max(1, presetVersion));
            PlayerPrefs.SetString(
                CharacterProfileStore.ActiveCharacterKey,
                string.IsNullOrWhiteSpace(characterId) ? "player" : characterId);
            PlayerPrefs.Save();
            BackfillManifestPreset(name, presetId, presetVersion);
            ScreenFadeController.LoadScene(GameScene);
        }

        private bool TryResolveManifestPreset(
            WorldManifest manifest,
            out WorldGenerationPresetData preset)
        {
            preset = null;
            if (_presetCatalog == null)
                return string.IsNullOrWhiteSpace(manifest.presetId);
            return _presetCatalog.TryResolve(
                manifest.presetId,
                manifest.presetVersion,
                out preset);
        }

        private static void BackfillManifestPreset(
            string worldName,
            string presetId,
            int presetVersion)
        {
            if (string.IsNullOrWhiteSpace(presetId))
                return;

            string path = Path.Combine(WorldDirectory(worldName), "world.json");
            try
            {
                if (!File.Exists(path))
                    return;
                WorldManifest manifest =
                    JsonUtility.FromJson<WorldManifest>(File.ReadAllText(path));
                if (manifest == null || !string.IsNullOrWhiteSpace(manifest.presetId))
                    return;
                manifest.presetId = presetId;
                manifest.presetVersion = Mathf.Max(1, presetVersion);
                File.WriteAllText(path, JsonUtility.ToJson(manifest, true));
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Could not update preset identity for '{worldName}': {exception.Message}");
            }
        }

        private static List<WorldManifest> ReadWorlds()
        {
            string root = Path.Combine(Application.persistentDataPath, "Worlds");
            if (!Directory.Exists(root))
                return new List<WorldManifest>();

            List<WorldManifest> result = new();
            foreach (string directory in Directory.GetDirectories(root))
            {
                string manifestPath = Path.Combine(directory, "world.json");
                try
                {
                    if (File.Exists(manifestPath))
                    {
                        WorldManifest manifest =
                            JsonUtility.FromJson<WorldManifest>(File.ReadAllText(manifestPath));
                        if (manifest != null && !string.IsNullOrWhiteSpace(manifest.name))
                            result.Add(manifest);
                    }
                    else if (File.Exists(Path.Combine(directory, "world.wsw")))
                    {
                        string name = Path.GetFileName(directory);
                        result.Add(new WorldManifest { name = name, seed = StableSeed(name) });
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Could not read world at {directory}: {exception.Message}");
                }
            }
            return result.OrderBy(world => world.name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static int StableSeed(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char character in value.Trim().ToLowerInvariant())
                {
                    hash ^= character;
                    hash *= 16777619;
                }
                return (int)(hash & 0x7fffffff);
            }
        }

        private static string SanitizeName(string value)
        {
            string result = value?.Trim() ?? string.Empty;
            foreach (char invalid in Path.GetInvalidFileNameChars())
                result = result.Replace(invalid, '_');
            return result.Trim('.', ' ');
        }

        private static string WorldDirectory(string name) =>
            Path.Combine(Application.persistentDataPath, "Worlds", SanitizeName(name));

        private int FindCurrentResolution()
        {
            for (int i = 0; i < _resolutions.Length; i++)
                if (_resolutions[i].width == Screen.width &&
                    _resolutions[i].height == Screen.height)
                    return i;
            return _resolutions.Length - 1;
        }

        private void Open(Page page)
        {
            _page = page;
            _message = string.Empty;
            _scroll = Vector2.zero;
        }

        private void BackButton()
        {
            GUILayout.Space(14);
            if (GUILayout.Button("BACK", GUILayout.Height(38)))
                Open(Page.Main);
        }

        private static bool MenuButton(string label) =>
            GUILayout.Button(label, GUILayout.Height(52));

        private string PageTitle() => _page switch
        {
            Page.Main => "Begin your next world",
            Page.NewWorld => "Create a new world",
            Page.LoadWorld => "Choose an existing world",
            Page.Options => "Options",
            _ => string.Empty
        };

        private void EnsureStyles()
        {
            if (_titleStyle != null)
                return;
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 38,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.84f, 0.94f, 0.82f) }
            };
            _subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            _messageStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = new Color(1f, 0.78f, 0.35f) }
            };
        }

        private void DrawBackdrop()
        {
            Color previous = GUI.color;
            Rect screen = new(0f, 0f, Screen.width, Screen.height);
            float alpha = worldPreview != null ? worldPreview.BackdropAlpha : 1f;
            GUI.color = new Color(0.025f, 0.045f, 0.04f, alpha);
            GUI.DrawTexture(screen, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
