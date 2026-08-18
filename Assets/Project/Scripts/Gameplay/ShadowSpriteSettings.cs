using System;
using Project.Scripts.Interface;
using UnityEngine;

namespace Project.Scripts.Gameplay
{
    public sealed class ShadowSpriteSettings : ScriptableObject
    {
        [SerializeField] private Sprite[] shadows = Array.Empty<Sprite>();

        public Sprite GetShadow(ShadowSize size)
        {
            string expectedName = $"Shadows_{(int)size}";
            foreach (Sprite shadow in shadows ?? Array.Empty<Sprite>())
            {
                if (shadow != null && shadow.name == expectedName)
                    return shadow;
            }
            return null;
        }
    }
}
