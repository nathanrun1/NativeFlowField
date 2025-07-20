using System;
using System.Collections.Generic;
using Unity.Collections;

namespace FlowFieldAI
{
    internal class NativeArrayPool
    {
        private readonly List<NativeArray<int>> buffers = new();
        private readonly Stack<int> freeIndices = new();
        private readonly int length;

        public int Count { get; private set; }
        public int Capacity { get; }
        public int RentedOutCount => Count - freeIndices.Count;
        public bool IsExhausted => freeIndices.Count == 0 && Count == Capacity;

        public NativeArrayPool(int minCapacity, int capacity, int length)
        {
            this.length = length;
            this.Count = minCapacity;
            this.Capacity = capacity;

            for (var i = 0; i < Count; i++)
            {
                buffers.Add(new NativeArray<int>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory));
                freeIndices.Push(i);
            }
        }

        public int Rent()
        {
            if (IsExhausted)
            {
                throw new InvalidOperationException("Pool is exhausted.");
            }

            if (freeIndices.Count == 0)
            {
                buffers.Add(new NativeArray<int>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory));
                return Count++;
            }

            return freeIndices.Pop();
        }

        public void Return(int index)
        {
            freeIndices.Push(index);
        }

        public NativeArray<int> this[int index]
        {
            get => buffers[index];
            set => buffers[index] = value; // allow update if reassigned by AsyncGPUReadback
        }

        public void Dispose()
        {
            foreach (var buffer in buffers)
            {
                if (buffer.IsCreated)
                {
                    try { buffer.Dispose(); }
                    catch
                    {
                        // Ignore. Some of these will likely be indisposable due to active AsyncGPUReadback jobs
                    }
                }
            }
        }
    }
}
