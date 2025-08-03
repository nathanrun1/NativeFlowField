using NUnit.Framework;
using Unity.Collections;

namespace FlowFieldAI.Tests
{
    public class NativeFlowFieldTests
    {
        private const int Width = 7;
        private const int Height = 3;

        private const string BasicDistanceMap =
            "░ ░ ░ ░ ░ ░ ░" +
            "░ ░ ░ 0 ░ ░ ░" +
            "░ ░ ░ ░ ░ ░ ░";

        private const string ExpectedFlowField =
            "↘ ↘ ↘ ↓ ↙ ↙ ↙" +
            "→ → → · ← ← ←" +
            "↗ ↗ ↗ ↑ ↖ ↖ ↖";

        private const string ExpectedFlowField_ZeroIteration =
            "· · ↘ ↓ ↙ · ·" +
            "· · → · ← · ·" +
            "· · ↗ ↑ ↖ · ·";

        private const string ExpectedFlowField_SingleIteration =
            "· ↘ ↘ ↓ ↙ ↙ ·" +
            "· → → · ← ← ·" +
            "· ↗ ↗ ↑ ↖ ↖ ·";

        [Test]
        public void ShaderApproachMap_20_Iterations_Works()
        {
            var distanceMap = TestUtils.ParseObstacleMapString(BasicDistanceMap, Width, Height, Allocator.Persistent);
            var flowField = new NativeFlowField(Width, Height);
            var bakeOptions = new BakeOptions
            {
                DiagonalMovement = true,
                Iterations = 20,
            };

            try
            {
                flowField.Bake(distanceMap, bakeOptions).WaitForCompletion();
                flowField.FlowField.ShouldBeEqualTo(ExpectedFlowField, Width, Height);
            }
            finally
            {
                distanceMap.Dispose();
                flowField.Dispose();
            }
        }

        [Test]
        public void ShaderApproachMap_Zero_Iteration_Works()
        {
            var distanceMap = TestUtils.ParseObstacleMapString(BasicDistanceMap, Width, Height, Allocator.Persistent);
            var flowField = new NativeFlowField(Width, Height);
            var bakeOptions = new BakeOptions
            {
                DiagonalMovement = true,
                Iterations = 0,
            };

            try
            {
                flowField.Bake(distanceMap, bakeOptions).WaitForCompletion();
                flowField.FlowField.ShouldBeEqualTo(ExpectedFlowField_ZeroIteration, Width, Height);
            }
            finally
            {
                distanceMap.Dispose();
                flowField.Dispose();
            }
        }

        [Test]
        public void ShaderApproachMap_Single_Iteration_Works()
        {
            var distanceMap = TestUtils.ParseObstacleMapString(BasicDistanceMap, Width, Height, Allocator.Persistent);
            var flowField = new NativeFlowField(Width, Height);
            var bakeOptions = new BakeOptions
            {
                DiagonalMovement = true,
                Iterations = 1,
            };

            try
            {
                flowField.Bake(distanceMap, bakeOptions).WaitForCompletion();
                flowField.FlowField.ShouldBeEqualTo(ExpectedFlowField_SingleIteration, Width, Height);
            }
            finally
            {
                distanceMap.Dispose();
                flowField.Dispose();
            }
        }
    }
}
