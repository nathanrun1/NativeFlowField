using UnityEngine.Rendering;

namespace FlowFieldAI
{
    public struct BakeOptions
    {
        public int Iterations;
        public int IterationsPerFrame;
        public bool DiagonalMovement;
        public ComputeQueueType ComputeQueueType;

        public bool IsIncrementalBake => IterationsPerFrame > 0 && IterationsPerFrame < Iterations;

        public static BakeOptions Default => new()
        {
            Iterations = 100,
            IterationsPerFrame = 0,
            DiagonalMovement = false,
            ComputeQueueType = ComputeQueueType.Background,
        };
    }
}
