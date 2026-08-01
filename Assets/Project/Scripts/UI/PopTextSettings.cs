using System;
using TMPro;
using UnityEngine;

namespace Project.Scripts.UI
{
    [Serializable]
    public sealed class PopTextSettings
    {
        [SerializeField] private TMP_FontAsset fontAsset;

        public TMP_FontAsset FontAsset => fontAsset;
    }
}
