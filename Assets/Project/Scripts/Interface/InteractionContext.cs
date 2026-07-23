using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public readonly struct InteractionContext
    {
        public readonly GameObject user;
        public readonly InteractionType interactionType;
        public readonly ToolData toolData;
        
        public InteractionContext(GameObject user, InteractionType interactionType, ToolData toolData)
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