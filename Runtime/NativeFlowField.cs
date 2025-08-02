using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlowFieldAI
{
    public class NativeFlowField : IDisposable
    {
        public const float Obstacle = float.MaxValue;
        public const float Free = float.MinValue;

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
        public NativeArray<int> FlowField { get; private set; }
        public RenderTexture HeatMap => heatMapFrontBuffer;

        public readonly int Width;
        public readonly int Height;
        private int Length => Width * Height;

        public int Iterations { get; set; } = 100;
        public int MaxIterationsPerFrame { get; set; }
        public bool DiagonalMovement { get; set; }
        public ComputeQueueType ComputeQueueType { get; set; } = ComputeQueueType.Background;

        public int FrameLatency { get; private set; }
        public float TimeLatency { get; private set; }
        public int BuffersActive => bufferPool.RentedOutCount;
        public int BuffersAllocated => bufferPool.Count;
        public int BuffersCapacity => bufferPool.Capacity;

        private int currentStep;
        private readonly CommandBuffer commandBuffer = new();

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

        public AsyncGPUReadbackRequest Bake(NativeArray<float> distanceMap)
        {
            ValidateMatrixDimensions(distanceMap);

            if (bufferPool.IsExhausted)
            {
                return default;
            }

            // Generate integration field
            PerformIntegration(distanceMap);

            // Do not finalize flow field and heat map until incremental dispatch is complete.
            if (currentStep < Iterations - 1)
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

            return AsyncGPUReadback.RequestIntoNativeArray(
                ref buffer,
                flowFieldBuffer,
                Length * sizeof(uint),
                0,
                req => OnReadbackComplete(poolIndex, req, dispatchFrame, dispatchTime));
        }

        private void PerformIntegration(NativeArray<float> obstacleMap)
        {
            commandBuffer.name = "NativeDijkstraMap";
            commandBuffer.Clear();
            commandBuffer.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
            commandBuffer.SetComputeIntParam(integrationComputeShader, ShaderProperties.Width, Width);
            commandBuffer.SetComputeIntParam(integrationComputeShader, ShaderProperties.Height, Height);
            commandBuffer.SetComputeIntParam(integrationComputeShader, ShaderProperties.DiagonalMovement, DiagonalMovement ? 1 : 0);

            var maxIterationsPerFrame = MaxIterationsPerFrame;
            if (maxIterationsPerFrame <= 0 || maxIterationsPerFrame > Iterations)
            {
                maxIterationsPerFrame = Iterations;
                currentStep = 0;
            }

            if (currentStep == 0)
            {
                commandBuffer.SetBufferData(integrationFrontBuffer, obstacleMap);
            }

            var iterationsRemaining = Iterations - currentStep;
            var iterationsThisFrame = Mathf.Min(iterationsRemaining, maxIterationsPerFrame);

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
                currentStep++;
            }

            Graphics.ExecuteCommandBufferAsync(commandBuffer, ComputeQueueType);
        }

        private void GenerateFlowField()
        {
            var threadGroupsX = Mathf.CeilToInt(Width / 8f);
            var threadGroupsY = Mathf.CeilToInt(Height / 8f);

            commandBuffer.SetComputeIntParam(generateFlowFieldComputeShader, ShaderProperties.Width, Width);
            commandBuffer.SetComputeIntParam(generateFlowFieldComputeShader, ShaderProperties.Height, Height);
            commandBuffer.SetComputeIntParam(generateFlowFieldComputeShader, ShaderProperties.DiagonalMovement, DiagonalMovement ? 1 : 0);
            commandBuffer.SetComputeBufferParam(generateFlowFieldComputeShader, generateFlowFieldComputeShaderKernel, ShaderProperties.InputCosts, integrationFrontBuffer);
            commandBuffer.SetComputeBufferParam(generateFlowFieldComputeShader, generateFlowFieldComputeShaderKernel, ShaderProperties.OutputFlowField, flowFieldBuffer);
            commandBuffer.DispatchCompute(generateFlowFieldComputeShader, generateFlowFieldComputeShaderKernel, threadGroupsX, threadGroupsY, 1);
            Graphics.ExecuteCommandBufferAsync(commandBuffer, ComputeQueueType);
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
            Graphics.ExecuteCommandBufferAsync(commandBuffer, ComputeQueueType);
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

            // Reset step counter
            currentStep = 0;
        }

        public void Dispose()
        {
            integrationFrontBuffer.Dispose();
            integrationBackBuffer.Dispose();
            flowFieldBuffer.Dispose();
            bufferPool.Dispose();
            heatMapBackBuffer?.Release();
            heatMapFrontBuffer?.Release();
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

