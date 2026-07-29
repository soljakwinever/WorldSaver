using Project.Scripts;
using Project.Scripts.DataTypes;
using UnityEngine;
using Zenject;

[CreateAssetMenu(fileName = "DataInstaller", menuName = "Installers/DataInstaller")]
public class DataInstaller : ScriptableObjectInstaller<DataInstaller>
{
    public WorldData WorldData;
    [Tooltip("Optional explicit catalog. When empty, Resources/WorldGeneration/PresetCatalog is used.")]
    public WorldGenerationPresetCatalogData GenerationPresetCatalog;

    public override void InstallBindings()
    {
        int seed = WorldData != null ? WorldData.seed : 0;
        if (PlayerPrefs.HasKey(Project.UI.MainMenu.MainMenuController.ActiveSeedKey))
        {
            seed = PlayerPrefs.GetInt(
                Project.UI.MainMenu.MainMenuController.ActiveSeedKey);
        }

        WorldGenerationPresetCatalogData catalog =
            GenerationPresetCatalog != null
                ? GenerationPresetCatalog
                : Resources.Load<WorldGenerationPresetCatalogData>(
                    "WorldGeneration/PresetCatalog");
        WorldGenerationPresetData preset = ResolvePreset(catalog);
        if (preset == null)
            preset = WorldGenerationPresetDefaults.CreateFromLegacy(WorldData);

        Container.BindInstance(WorldData);
        Container.BindInstance(new WorldGenerationSelection(seed, preset));
        Container.BindInstance(preset);
        if (catalog != null)
            Container.BindInstance(catalog);
    }

    private static WorldGenerationPresetData ResolvePreset(
        WorldGenerationPresetCatalogData catalog)
    {
        if (catalog == null)
            return null;

        string presetId = PlayerPrefs.GetString(
            Project.UI.MainMenu.MainMenuController.ActivePresetIdKey,
            string.Empty);
        int presetVersion = PlayerPrefs.GetInt(
            Project.UI.MainMenu.MainMenuController.ActivePresetVersionKey,
            1);
        if (catalog.TryResolve(presetId, presetVersion, out WorldGenerationPresetData preset))
            return preset;

        throw new System.InvalidOperationException(
            $"World generation preset '{presetId}' version {presetVersion} is not installed.");
    }
}
