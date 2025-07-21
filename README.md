**GPU-powered flow field generation for Unity DOTS**

<img src="https://i.imgur.com/zcKoRNy.gif" width="100%" />

## Overview

**NativeFlowField** is a high-performance Unity library for generating 2D navigation flow fields on the GPU using compute shaders and native collections. It is intended for DOTS-based projects that require scalable, low-latency navigation data suitable for thousands of agents operating in dynamic environments.

Flow fields allow multiple agents to navigate towards shared targets without making individual pathfinding requests. That makes flow fields ideal for crowd simulation, RTS unit movement and swarm behavior.

This implementation offloads flow field computation entirely to the GPU, enabling real-time responsiveness to dynamic obstacles and moving targets without sacrificing performance.

## Installation

Add the following line to the _dependencies_ section of your _packages.json_:

`"com.kingstone426.nativeflowfield": "https://github.com/kingstone426/NativeFlowField.git"`

Alternatively, open the Package Manager, click the _+_ and select _Install package from git URL..._

`https://github.com/kingstone426/NativeFlowField.git`

## Usage

For a complete sample project, check out the [NativeFlowFieldTestProject](https://github.com/kingstone426/NativeFlowFieldTestProject).

<table>
  <tr>
    <td align="center"><img src="https://i.imgur.com/zcKoRNy.gif" alt="Image 1" width="100%"/>Input field</td>