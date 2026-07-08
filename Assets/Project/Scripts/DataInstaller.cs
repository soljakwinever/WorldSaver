using Project.Scripts;
using UnityEngine;
using Zenject;

[CreateAssetMenu(fileName = "DataInstaller", menuName = "Installers/DataInstaller")]
public class DataInstaller : ScriptableObjectInstaller<DataInstaller>
{
    public WorldData WorldData;
    public override void InstallBindings()
    {
        Container.BindInstance(WorldData);
    }
}