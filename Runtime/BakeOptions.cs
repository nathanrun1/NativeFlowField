namespace FlowFieldAI
{
    public struct BakeOptions
    {
        public int Iterations;
        public int IterationsPerFrame;
        public bool DiagonalMovement;

        public static BakeOptions Default => new()
        {
            Iterations = 100,
            IterationsPerFrame = 0,
            DiagonalMovement = false,
        };
    }
}
