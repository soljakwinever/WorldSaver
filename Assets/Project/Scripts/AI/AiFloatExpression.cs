using System;

namespace Project.Scripts.AI
{
    public enum AiFloatVariable
    {
        MovementSpeed,
        SprintMultiplier,
        Accuracy
    }

    public enum AiFloatOperation
    {
        Add,
        Subtract,
        Multiply,
        Divide
    }

    [Serializable]
    public abstract class AiFloatExpression
    {
        public abstract float Evaluate(Blackboard blackboard);
    }

    [Serializable]
    public sealed class AiFloatConstantExpression : AiFloatExpression
    {
        public float value;

        public AiFloatConstantExpression(float value)
        {
            this.value = value;
        }

        public override float Evaluate(Blackboard blackboard) => value;
    }

    [Serializable]
    public sealed class AiFloatVariableExpression : AiFloatExpression
    {
        public AiFloatVariable variable;

        public AiFloatVariableExpression(AiFloatVariable variable)
        {
            this.variable = variable;
        }

        public override float Evaluate(Blackboard blackboard) =>
            variable switch
            {
                AiFloatVariable.MovementSpeed =>
                    blackboard.GetOrDefault(AiKeys.MovementSpeed),
                AiFloatVariable.SprintMultiplier =>
                    blackboard.GetOrDefault(AiKeys.SprintMultiplier),
                AiFloatVariable.Accuracy =>
                    blackboard.GetOrDefault(AiKeys.Accuracy),
                _ => 0f
            };
    }

    [Serializable]
    public sealed class AiFloatMathExpression : AiFloatExpression
    {
        [UnityEngine.SerializeReference] public AiFloatExpression left;
        [UnityEngine.SerializeReference] public AiFloatExpression right;
        public AiFloatOperation operation;

        public AiFloatMathExpression(
            AiFloatExpression left,
            AiFloatExpression right,
            AiFloatOperation operation)
        {
            this.left = left;
            this.right = right;
            this.operation = operation;
        }

        public override float Evaluate(Blackboard blackboard)
        {
            float a = left?.Evaluate(blackboard) ?? 0f;
            float b = right?.Evaluate(blackboard) ?? 0f;
            return operation switch
            {
                AiFloatOperation.Add => a + b,
                AiFloatOperation.Subtract => a - b,
                AiFloatOperation.Multiply => a * b,
                AiFloatOperation.Divide => Math.Abs(b) > float.Epsilon
                    ? a / b
                    : 0f,
                _ => 0f
            };
        }
    }

    [Serializable]
    public sealed class AiFloatFieldBinding
    {
        public string fieldName;
        [UnityEngine.SerializeReference] public AiFloatExpression expression;

        public AiFloatFieldBinding(
            string fieldName,
            AiFloatExpression expression)
        {
            this.fieldName = fieldName;
            this.expression = expression;
        }
    }
}
