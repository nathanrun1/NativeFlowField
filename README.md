**GPU-powered flow field generation for Unity DOTS**

<img src="https://i.imgur.com/zcKoRNy.gif" width="100%" />

## Overview

**NativeFlowField** is a high-performance Unity library for generating 2D navigation flow fields on the GPU using compute shaders and native collections.
It is intended for DOTS-based projects that require scalable, low-latency navigation data suitable for thousands of agents operating in dynamic environments.

Unlike traditional pathfinding, flow fields provide optimal routing from any point on a grid.
A single flow field can therefore guide many agents efficiently with minimal overhead.
Since agents use a shared map, they don't have to make individual pathfinding requests, which makes flow fields ideal for crowd simulation, RTS unit movement and swarm behavior.

This implementation offloads flow field computation entirely to the GPU, enabling real-time updates to dynamic obstacle maps and continuous recalculation of full-resolution fields. Compared to Unity’s built-in NavMesh, NativeFlowField offers better scalability under high agent counts and better responsiveness to real-time world changes.

## Usage

For a complete sample project, check out the [NativeFlowFieldTestProject](https://github.com/kingstone426/NativeFlowFieldTestProject).

### Step 1: Create a NativeFlowField

```
// Let's create a tiny 8x8 flow field
var flowField = new NativeFlowField(8, 8);

// Enable 8-direction movement
flowField.DiagonalMovement = true;
```

### Step 2: Create an input field

```
// Make sure the input field is the same size as the flow field
var inputField = new NativeArray<float>(8 * 8, Allocator.Persistent);
```

The input field contains the map data that will be baked.
Each element represents a tile that can be either Walkable, Obstacle or Target.

<table><tr><td>

```
float W = float.MinValue;   // Walkable
float O = float.MaxValue;   // Obstacle
float T = 0;                // Target

// Let's populate the input field with some map data
NativeArray<float>.Copy(new float[]
{
    O, O, O, O, O, O, O, O,
    O, W, W, W, W, W, W, O,
    O, W, T, W, O, O, W, O,
    O, O, O, W, O, W, W, O,
    O, W, W, W, O, W, O, O,
    O, W, O, O, O, W, W, O,
    O, W, W, W, W, W, W, O,
    O, O, O, O, O, O, O, O,
}, inputField);
```

</td><td>

<img src="https://i.imgur.com/zcKoRNy.gif" width="430px" />

</td></tr></table>

> [!TIP]  
> You can mark multiple tiles as targets and (optionally) give them different priority. A value of zero represents default priority, while positive numbers have lower priority and negative numbers have higher priority.
>

### Step 3: Bake the Flow Field

<table><tr><td>

```
flowField.Bake(inputField);
```

</td><td>

<img src="https://i.imgur.com/zcKoRNy.gif" width="430px" />

</td></tr></table>

The Bake method dispatches compute shader passes that propagates the distance field step-by-step outwards from the Target.

> [!NOTE]  
> The Bake method returns a `AsyncGPUReadbackRequest`, which you can poll or wait for if blocking behavior is needed (e.g., during testing). For real-time applications, however, it is recommended that you recreate the input field and call `Bake` each frame (or whenever the terrain has been updated).
>

### Step 3: Use the Result

Once the GPU has finished baking the input field, the resulting flow field can be accessed from the `NextIndices` property. `NextIndices` is a `NativeArray<int>` where each element holds the index of the adjacent tile with the lowest distance to target. This makes it very simple to navigate towards the target.

Here is an example of a job that helps agents navigate towards targets:

```
[BurstCompile]
private unsafe partial struct NavigationJob : IJobEntity
{
    [ReadOnly][NativeDisableUnsafePtrRestriction] public int* FlowField;
    public int Width;

    private void Execute(ref LocalTransform transform, in Agent _)
    {
        // Get current tile from position
        var currentTile = (int2)math.round(transform.Position.xz);
        var currentTileIndex = currentTileIndex.x + currentTileIndex.y * Width;
    
        // Traverse to next tile (or traverse to self, if the Target has been reached)
        var newTileIndex = FlowField[currentTileIndex];
    
        // Update position to new tile
        transform.Position.x = newTileIndex % Width;
        transform.Position.y = (int)(newTileIndex / Width);
    }
}
```

Schedule the `NavigationJob` from a system and watch the agents move towards the target.

<table><tr><td>

```
var ptr = (int*)flowField.NextIndices.GetUnsafeReadOnlyPtr();

Dependency = new NavigationJob
{
    FlowField = ptr,
    Width = 8
}.ScheduleParallel(Dependency);
```

</td><td>

<img src="https://i.imgur.com/zcKoRNy.gif" width="430px" />

</td></tr></table>


> [!NOTE]
> The `unsafe` pointers are used as a workaround for a known [bug](https://discussions.unity.com/t/asyncgpureadback-requestintonativearray-causes-invalidoperationexception-on-nativearray/818225/76) with `AsyncGPUReadback.RequestIntoNativeArray`.
>


### Step 4: Clean Up
Remember to dispose all native resources when done:

```
inputField.Dispose();
flowField.Dispose();
```