using System.Text;
using NUnit.Framework;
using Unity.Collections;
using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace FlowFieldAI.Tests
{
    public static class TestUtils
    {
        public static NativeArray<float> ParseObstacleMapString(string obstacleMap, int width, int height, Allocator allocator)
        {
            obstacleMap = Regex.Replace(obstacleMap, @"\s+", "");
            var nativeObstacleMap = new NativeArray<float>(width * height, allocator);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var c = obstacleMap[y * width + x];
                    nativeObstacleMap[y * width + x] = c switch
                    {
                        '░' => NativeFlowField.FreeTile,
                        '█' => NativeFlowField.ObstacleTile,
                        '0' => 0,
                        _ => throw new ArgumentException($"Invalid ObstacleMap char: {c} in string: {obstacleMap}", $"{nameof(obstacleMap)} at ({x},{y})"),
                    };
                }
            }

            return nativeObstacleMap;
        }

        private static NativeArray<int> ParseFlowFieldString(string flowField, int width, int height, Allocator allocator)
        {
            flowField = Regex.Replace(flowField, @"\s+", "");
            var nativeFlowField = new NativeArray<int>(width * height, allocator);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var c = flowField[y * width + x];
                    var delta = Arrows.First(kvp => kvp.Value == c).Key;
                    nativeFlowField[y * width + x] = (y + delta.y) * width + (x + delta.x);
                }
            }

            return nativeFlowField;
        }

        public static void ShouldBeEqualTo(
            this NativeArray<int> actual,
            string expected,
            int width,
            int height,
            float tolerance = 0.00001f)
        {
            expected = Regex.Replace(expected, @"\s+", "");
            Assert.AreEqual(expected.Length, actual.Length);
            Assert.AreEqual(expected.Length, width * height);

            var expectedArray = ParseFlowFieldString(expected, width, height, Allocator.Temp);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    Assert.AreEqual(expectedArray[index], actual[index], tolerance,
                        $"Mismatch at ({x},{y})\n " +
                        $"expected:\n{expectedArray.FormatMatrix(width, height)} \n" +
                        $"actual:\n{actual.FormatMatrix(width, height)}"
                    );
                }
            }
            expectedArray.Dispose();
        }

        private static string FormatMatrix(this NativeArray<int> matrix, int width, int height)
        {
            var array = new int[matrix.Length];
            matrix.CopyTo(array);
            return array.FormatMatrix(width, height);
        }

        private static string FormatMatrix(this int[] matrix, int width, int height)
        {
            var sb = new StringBuilder();

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    var nextIndex = matrix[index];
                    if (nextIndex < 0)
                    {
                        sb.Append('·');
                    }
                    else
                    {
                        var nextTile = new int2(nextIndex % width, nextIndex / width);
                        var delta = nextTile - new int2(x, y);
                        sb.Append(Arrows[delta]);
                    }
                    sb.Append(' ');
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static readonly Dictionary<int2, char> Arrows = new()
        {
            { new int2(0, 0), '·'},
            { new int2(1, 0), '→'},
            { new int2(-1, 0), '←'},
            { new int2(0, -1), '↑'},
            { new int2(0, 1), '↓'},
            { new int2(-1, -1), '↖'},
            { new int2(1, -1), '↗'},
            { new int2(-1, 1), '↙'},
            { new int2(1, 1), '↘'},
        };
    }
}
