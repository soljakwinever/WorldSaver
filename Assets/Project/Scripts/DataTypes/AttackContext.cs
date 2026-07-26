using System;
using UnityEngine;

namespace Project.Scripts.DataTypes
{
    /// <summary>Describes the source and strength of an attack.</summary>
    public readonly struct AttackContext
    {
        public GameObject Attacker { get; }
        public ToolData Weapon { get; }
        public int Force { get; }

        public AttackContext(GameObject attacker, ToolData weapon, int force)
        {
            if (attacker == null)
                throw new ArgumentNullException(nameof(attacker));
            if (force < 0)
                throw new ArgumentOutOfRangeException(nameof(force));

            Attacker = attacker;
            Weapon = weapon;
            Force = force;
        }
    }
}
