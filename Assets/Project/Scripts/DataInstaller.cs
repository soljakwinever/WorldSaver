using Project.Scripts;
using UnityEngine;
using Zenject;

[CreateAssetMenu(fileName = "DataInstaller", menuName = "Installers/DataInstaller")]
public class DataInstaller : ScriptableObjectInstaller<DataInstaller>
{
    public WorldData WorldData;
    public override void InstallBindings()
    {
        if (WorldData != null &&
            PlayerPrefs.HasKey(Project.UI.MainMenu.MainMenuController.ActiveSeedKey))
        {
            WorldData.seed = PlayerPrefs.GetInt(
                Project.UI.MainMenu.MainMenuController.ActiveSeedKey);
        }
        Container.BindInstance(WorldData);
    }
}
