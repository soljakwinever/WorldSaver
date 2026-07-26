using System;

namespace Project.Scripts.AI.GraphEditor
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class AiNodeAttribute : Attribute
    {
        public string Name { get; set; }
        public string Group { get; set; }

        public AiNodeAttribute(string name = null, string group = "AI")
        {
            Name = name;
            Group = group;
        }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class InputPortAttribute : Attribute
    {
        public string Name { get; }

        public InputPortAttribute(string name = null)
        {
            Name = name;
        }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class OutputPortAttribute : Attribute
    {
        public string Name { get; }

        public OutputPortAttribute(string name = null)
        {
            Name = name;
        }
    }
}
