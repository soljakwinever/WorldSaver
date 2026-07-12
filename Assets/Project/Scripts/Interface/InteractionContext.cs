using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public readonly struct InteractionContext
    {
        public readonly GameObject user;
        public readonly InteractionType interactionType;
        public readonly IToolData toolData;
        
        public InteractionContext(GameObject user, InteractionType interactionType, IToolData toolData)
        {
            this.user = user;
            this.interactionType = interactionType;
            this.toolData = toolData;
        }
    }

    public enum InteractionType
    {
        Direct,
        Tool
    }
}