using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlowFieldAI
{
    public class NativeFlowField : IDisposable
    {
        // ─────────────────────────────────────────────────────────────
        // Public Properties
        // ─────────────────────────────────────────────────────────────
        public NativeArray<int> FlowField { get; private set; }
        public RenderTexture HeatMap => heatMapFrontBuffer;

        public readonly int Width;
        public readonly int Height;
        public int Length => Width * Height;

        public int FrameLatency { get; private set; }
        public float TimeLatency { get; private set; }
        public int BuffersActive => bufferPool.RentedOutCount;
        public int BuffersAllocated => bufferPool.Count;
        public int BuffersCapacity => bufferPool.Capacity;

        // ─────────────────────────────────────────────────────────────
        // Constants
        // ─────────────────────────────────────────────────────────────
        public const float ObstacleTile = float.MaxValue;
        public const float FreeTile = float.MinValue;

        private static class ShaderProperties
        {
            public static readonly int Width = Shader.PropertyToID("Width");
            public static readonly int Height = Shader.PropertyToID("Height");
            public static readonly int DiagonalMovement = Shader.PropertyToID("DiagonalMovement");
            public static readonly int InputCosts = Shader.PropertyToID("InputCosts");
            public static readonly int OutputCosts = Shader.PropertyToID("OutputCosts");
            public static readonly int OutputHeatMap = Shader.PropertyToID("OutputHeatMap");
            public static readonly int OutputFlowField = Shader.PropertyToID("OutputFlowField");
        }

        // ─────────────────────────────────────────────────────────────
        // Private Fields
        // ─────────────────────────────────────────────────────────────
        private readonly ComputeShader integrationComputeShader;
        private readonly int integrationComputeShaderKernel;
        private ComputeBuffer integrationFrontBuffer;
        private ComputeBuffer integrationBackBuffer;

        private readonly ComputeShader generateFlowFieldComputeShader;
        private readonly int generateFlowFieldComputeShaderKernel;
        private readonly ComputeBuffer flowFieldBuffer;

        private readonly ComputeShader generateHeatMapComputeShader;
        private readonly int generateHeatMapComputeShaderKernel;
        private readonly bool generateHeatMap;
        private readonly RenderTexture heatMapBackBuffer;
        private readonly RenderTexture heatMapFrontBuffer;

        private readonly NativeArrayPool bufferPool;
        private int frontBufferPoolIndex;

        private BakeContext bakeContext;
        private readonly CommandBuffer commandBuffer = new();

        // ─────────────────────────────────────────────────────────────
        // Constructor
        // ─────────────────────────────────────────────────────────────
        public NativeFlowField(int width, int height, bool generateHeatMap=false, int minBuffers=2, int maxBuffers=5)
        {
            Width = width;
            Height = height;

            this.generateHeatMap = generateHeatMap;

            integrationComputeShader = Resources.Load<ComputeShader>("GenerateIntegrationField");
            integrationComputeShaderKernel = integrationComputeShader.FindKernel("GenerateIntegrationField");

            integrationFrontBuffer = new ComputeBuffer(Length, sizeof(float));
            integrationBackBuffer = new ComputeBuffer(Length, sizeof(float));

            generateFlowFieldComputeShader = Resources.Load<ComputeShader>("GenerateFlowField");
            generateFlowFieldComputeShaderKernel = generateFlowFieldComputeShader.FindKernel("GenerateFlowField");
            flowFieldBuffer = new ComputeBuffer(Length, sizeof(int));

            if (generateHeatMap)
            {
                generateHeatMapComputeShader = Resources.Load<ComputeShader>("GenerateHeatMap");
                generateHeatMapComputeShaderKernel = generateHeatMapComputeShader.FindKernel("GenerateHeatMap");

                heatMapBackBuffer = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat)
                {
                    enableRandomWrite = true,
                    filterMode = FilterMode.Point,
                };
                heatMapFrontBuffer = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat)
                {
                    enableRandomWrite = true,
                    filterMode = FilterMode.Point,
                };
            }

            if (minBuffers < 2)
            {
                throw new ArgumentException("minBuffers must be at least 2");
            }

            if (maxBuffers < minBuffers)
            {
                throw new ArgumentException("maxBuffers must be at least minBuffers");
            }

            bufferPool = new NativeArrayPool(minBuffers, maxBuffers, Length);
        }

        // ─────────────────────────────────────────────────────────────
        // Public Methods
        // ─────────────────────────────────────────────────────────────
        public void Dispose()
        {
            integrationFrontBuffer.Dispose();
            integrationBackBuffer.Dispose();
            flowFieldBuffer.Dispose();
            bufferPool.Dispose();
            heatMapBackBuffer?.Release();
            heatMapFrontBuffer?.Release();
        }

        public AsyncGPUReadbackRequest Bake(NativeArray<float> inputField) => Bake(inputField, BakeOptions.Default);

        public AsyncGPUReadbackRequest Bake(NativeArray<float> inputField, BakeOptions bakeOptions)
        {
            ValidateMatrixDimensions(inputField);

            if (bufferPool.IsExhausted)
            {
                return default;
            }

            // Initialize buffers
            var iterationsThisFrame = InitializeBake(inputField, bakeOptions);

            // All iterations have been dispatched, waiting for readback
            if (iterationsThisFrame <= 0)
            {
                return default;
            }

            // Generate integration field
            PerformIntegration(iterationsThisFrame);

            // Do not finalize flow field and heat map until incremental dispatch is complete.
            if (bakeContext.Options.IsIncrementalBake && bakeContext.CurrentIteration < bakeContext.Options.Iterations)
            {
                return default;
            }

            // Generate flow field
            GenerateFlowField();

            // Generate heat map
            GenerateHeatMap();

            // Prepare async GPU readback
            var poolIndex = bufferPool.Rent();
            var buffer = bufferPool[poolIndex];
            var dispatchFrame = Time.frameCount;
            var dispatchTime = Time.realtimeSinceStartup;

            // Dispatch async readback request
            return AsyncGPUReadback.RequestIntoNativeArray(
                ref buffer,
                flowFieldBuffer,
                Length * sizeof(uint),
                0,
                req => OnReadbackComplete(poolIndex, req, dispatchFrame, dispatchTime));
        }

        // ─────────────────────────────────────────────────────────────
        // Private Methods
        // ─────────────────────────────────────────────────────────────
        private int InitializeBake(NativeArray<float> inputField, BakeOptions bakeOptions)
        {
            // Do not reset bake context if incremental bake is in progress.
            var initializeNewBake = !bakeOptions.IsIncrementalBake || bakeContext == null;
            if (initializeNewBake)
            {
                bakeContext = new BakeContext
                {
                    Options = bakeOptions,
                    CurrentIteration = 0,
                };
            }

            // Calculate iteration count
            var iterationsRemaining = bakeOptions.Iterations - bakeContext.CurrentIteration;
            var iterationsThisFrame = bakeOptions.IsIncrementalBake
                ? Mathf.Min(iterationsRemaining, bakeOptions.IterationsPerFrame)
                : Mathf.Min(iterationsRemaining, bakeOptions.Iterations);

            // Initialize compute buffers
            if (iterationsThisFrame > 0)
            {
                // Initialize graphics command buffer
                commandBuffer.name = "NativeFlowField";
                commandBuffer.Clear();
                commandBuffer.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);

                // Initialize compute buffer with input field
                if (initializeNewBake)
                {
                    commandBuffer.SetBufferData(integrationFrontBuffer, inputField);
                }
            }

            return iterationsThisFrame;
        }

        private void PerformIntegration(int iterationsThisFrame)
        {
            commandBuffer.SetComputeIntParam(integrationComputeShader, ShaderProperties.Width, Width);
            commandBuffer.SetComputeIntParam(integrationComputeShader, ShaderProperties.Height, Height);
            commandBuffer.SetComputeIntParam(integrationComputeShader, ShaderProperties.DiagonalMovement, bakeContext.Options.DiagonalMovement ? 1 : 0);

            for (var i = 0; i < iterationsThisFrame; i++)
            {
                // Assign compute buffers
                commandBuffer.SetComputeBufferParam(integrationComputeShader, integrationComputeShaderKernel, ShaderProperties.InputCosts, integrationFrontBuffer);
                commandBuffer.SetComputeBufferParam(integrationComputeShader, integrationComputeShaderKernel, ShaderProperties.OutputCosts, integrationBackBuffer);

                // Dispatch compute shader
                var threadGroupsX = Mathf.CeilToInt(Width / 8f);
                var threadGroupsY = Mathf.CeilToInt(Height / 8f);
                commandBuffer.DispatchCompute(integrationComputeShader, integrationComputeShaderKernel, threadGroupsX, threadGroupsY, 1);

                // Flip compute buffers
                (integrationFrontBuffer, integrationBackBuffer) = (integrationBackBuffer, integrationFrontBuffer);

                // Increment step counter
                bakeContext.CurrentIteration++;
            }

            Graphics.ExecuteCommandBufferAsync(commandBuffer, bakeContext.Options.ComputeQueueType);
        }

        private void GenerateFlowField()
        {
            var threadGroupsX = Mathf.CeilToInt(Width / 8f);
            var threadGroupsY = Mathf.CeilToInt(Height / 8f);

            commandBuffer.SetComputeIntParam(generateFlowFieldComputeShader, ShaderProperties.Width, Width);
            commandBuffer.SetComputeIntParam(generateFlowFieldComputeShader, ShaderProperties.Height, Height);
            commandBuffer.SetComputeIntParam(generateFlowFieldComputeShader, ShaderProperties.DiagonalMovement, bakeContext.Options.DiagonalMovement ? 1 : 0);
            commandBuffer.SetComputeBufferParam(generateFlowFieldComputeShader, generateFlowFieldComputeShaderKernel, ShaderProperties.InputCosts, integrationFrontBuffer);
            commandBuffer.SetComputeBufferParam(generateFlowFieldComputeShader, generateFlowFieldComputeShaderKernel, ShaderProperties.OutputFlowField, flowFieldBuffer);
            commandBuffer.DispatchCompute(generateFlowFieldComputeShader, generateFlowFieldComputeShaderKernel, threadGroupsX, threadGroupsY, 1);

            Graphics.ExecuteCommandBufferAsync(commandBuffer, bakeContext.Options.ComputeQueueType);
        }

        private void GenerateHeatMap()
        {
            if (!generateHeatMap)
            {
                return;
            }

            var threadGroupsX = Mathf.CeilToInt(Width / 8f);
            var threadGroupsY = Mathf.CeilToInt(Height / 8f);

            commandBuffer.SetComputeIntParam(generateHeatMapComputeShader, ShaderProperties.Width, Width);
            commandBuffer.SetComputeIntParam(generateHeatMapComputeShader, ShaderProperties.Height, Height);
            commandBuffer.SetComputeTextureParam(generateHeatMapComputeShader, generateHeatMapComputeShaderKernel, ShaderProperties.OutputHeatMap, heatMapBackBuffer);
            commandBuffer.SetComputeBufferParam(generateHeatMapComputeShader, generateHeatMapComputeShaderKernel, ShaderProperties.InputCosts, integrationFrontBuffer);
            commandBuffer.DispatchCompute(generateHeatMapComputeShader, generateHeatMapComputeShaderKernel, threadGroupsX, threadGroupsY, 1);

            Graphics.ExecuteCommandBufferAsync(commandBuffer, bakeContext.Options.ComputeQueueType);
        }

        private void OnReadbackComplete(int poolIndex, AsyncGPUReadbackRequest req, int dispatchFrame, float dispatchTime)
        {
            if (req.hasError)
            {
                Debug.LogError("GPU readback error!");
                return;
            }

            // Update latency
            FrameLatency = Time.frameCount - dispatchFrame;
            TimeLatency = Time.realtimeSinceStartup - dispatchTime;

            // Return front buffer to pool
            if (FlowField.IsCreated)
            {
                bufferPool.Return(frontBufferPoolIndex);
            }

            // Present front buffer
            FlowField = bufferPool[poolIndex];
            frontBufferPoolIndex = poolIndex;

            // Present heat map
            if (generateHeatMap && heatMapBackBuffer && heatMapFrontBuffer)
            {
                Graphics.CopyTexture(heatMapBackBuffer, heatMapFrontBuffer);
            }

            // Clear bake context
            bakeContext = null;
        }

        [System.Diagnostics.Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void ValidateMatrixDimensions(NativeArray<float> matrix)
        {
            if (matrix.Length != Length)
            {
                throw new ArgumentException($"NativeArray length ({matrix.Length}) mismatching " +
                                            $"NativeDijkstraMap dimensions ({Width} x {Height}) = {Length}.");
            }
        }
    }
}

