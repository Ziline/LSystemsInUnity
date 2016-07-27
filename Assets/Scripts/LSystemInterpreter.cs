﻿using UnityEngine;
using System;
using System.Collections.Generic;

public static class LSystemInterpreter
{
    private struct Turtle
    {
        public Quaternion direction;
        public Vector3 position;
        public Vector3 step;

        public Turtle(Turtle other)
        {
            this.direction = other.direction;
            this.position = other.position;
            this.step = other.step;
        }

        public Turtle(Quaternion direction, Vector3 position, Vector3 step)
        {
            this.direction = direction;
            this.position = position;
            this.step = step;
        }

        public void Forward()
        {
            position += direction * step;
        }

        public void RotateX(float angle)
        {
            direction *= Quaternion.Euler(angle, 0, 0);
        }

        public void RotateY(float angle)
        {
            direction *= Quaternion.Euler(0, angle, 0);
        }

        public void RotateZ(float angle)
        {
            direction *= Quaternion.Euler(0, 0, angle);
        }

    }

    static void CreateSegment(
        int segmentAxisSamples,
        int segmentRadialSamples,
        float segmentWidth,
        float segmentHeight,
        int leafDensity,
        bool narrowBranches,
        Material trunkMaterial,
        Turtle turtle, 
        int nestingLevel, 
        ref Mesh currentMesh, 
        ref int chunkCount, 
        GameObject trunk, 
        Dictionary<int, Mesh> segmentsCache)
    {
        Vector3[] newVertices;
        Vector3[] newNormals;
        Vector2[] newUVs;
        int[] newIndices;

        Mesh segment;
        if (segmentsCache.ContainsKey(nestingLevel))
            segment = segmentsCache[nestingLevel];
        else
        {
            float thickness = (narrowBranches) ? segmentWidth * (0.5f / (nestingLevel + 1)) : segmentWidth * 0.5f;
            segment = ProceduralMeshes.CreateCylinder(segmentAxisSamples, segmentRadialSamples, thickness, segmentHeight);
            segmentsCache[nestingLevel] = segment;
        }

        newVertices = segment.vertices;
        newNormals = segment.normals;
        newUVs = segment.uv;
        newIndices = segment.triangles;

        if (currentMesh.vertices.Length + newVertices.Length > 65000)
        {
            CreateNewChunk(currentMesh, ref chunkCount, trunkMaterial, trunk);
            currentMesh = new Mesh();
        }

        int numVertices = currentMesh.vertices.Length + newVertices.Length;
        int numTriangles = currentMesh.triangles.Length + newIndices.Length;

        Vector3[] vertices = new Vector3[numVertices];
        Vector3[] normals = new Vector3[numVertices];
        int[] indices = new int[numTriangles];
        Vector2[] uvs = new Vector2[numVertices];

        Array.Copy(currentMesh.vertices, 0, vertices, 0, currentMesh.vertices.Length);
        Array.Copy(currentMesh.normals, 0, normals, 0, currentMesh.normals.Length);
        Array.Copy(currentMesh.triangles, 0, indices, 0, currentMesh.triangles.Length);
        Array.Copy(currentMesh.uv, 0, uvs, 0, currentMesh.uv.Length);

        Vector3 vertexOffset = turtle.position - (turtle.direction * (new Vector3(segmentWidth, segmentHeight, 0) * 0.5f));

        int offset = currentMesh.vertices.Length;
        for (int i = 0; i < newVertices.Length; i++)
        {
            Vector3 vertex = newVertices[i];
            vertices[offset + i] = vertexOffset + (turtle.direction * vertex);
        }

        int trianglesOffset = currentMesh.vertices.Length;
        offset = currentMesh.triangles.Length;
        for (int i = 0; i < newIndices.Length; i++)
        {
            int index = newIndices[i];
            indices[offset + i] = (trianglesOffset + index);
        }

        Array.Copy(newNormals, 0, normals, currentMesh.normals.Length, newNormals.Length);
        Array.Copy(newUVs, 0, uvs, currentMesh.uv.Length, newUVs.Length);

        currentMesh.vertices = vertices;
        currentMesh.normals = normals;
        currentMesh.triangles = indices;
        currentMesh.uv = uvs;

        currentMesh.Optimize();
    }

    static void AddFoliageAt(
        float segmentWidth, 
        float segmentHeight, 
        int leafDensity, 
        Turtle turtle, 
        GameObject leafBillboard, 
        GameObject leaves)
    {
        for (int i = 0; i < leafDensity; i++)
        {
            for (int j = 0; j < (leafDensity - i) * leafDensity; j++)
            {
                Vector3 positionOffset = turtle.direction * new Vector3(segmentWidth * 0.5f, ((segmentHeight * 0.25f) * (leafDensity - i)), 0);
                Vector3 rotationOffset = new Vector3(0, (360 / ((leafDensity - i) + 1)) * j, Mathf.Min(30 * (leafDensity - i), 90));

                GameObject leaf = (GameObject)GameObject.Instantiate(leafBillboard, Vector3.zero, turtle.direction);
                leaf.transform.parent = leaves.transform;
                leaf.transform.position = turtle.position - positionOffset;
                leaf.transform.Rotate(rotationOffset);
            }
        }
    }

    static void CreateNewChunk(Mesh mesh, ref int count, Material trunkMaterial, GameObject trunk)
    {
        GameObject chunk = new GameObject("Chunk " + (++count));
        chunk.transform.parent = trunk.transform;
        chunk.transform.localPosition = Vector3.zero;
        chunk.AddComponent<MeshRenderer>().material = trunkMaterial;
        chunk.AddComponent<MeshFilter>().mesh = mesh;
    }

    static GameObject CreateLeafBillboard(float leafSize, Material leafMaterial)
    {
        GameObject leafBillboard = new GameObject("Leaf");
        leafBillboard.AddComponent<MeshRenderer>().sharedMaterial = leafMaterial;
        leafBillboard.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshes.CreateXZPlane(leafSize, leafSize, 1, 1, new Vector3(-leafSize, 0, leafSize * 0.5f));
        return leafBillboard;
    }

    public static void Interpret(
        int segmentAxisSamples,
        int segmentRadialSamples,
        float segmentWidth,
        float segmentHeight,
        float leafSize,
        int leafDensity,
        bool useFoliage,
        bool narrowBranches,
        Material leafMaterial,
        Material trunkMaterial,
        float angle, 
        string moduleString, 
        out GameObject leaves, 
        out GameObject trunk)
    {
        leaves = new GameObject("Leaves");
        trunk = new GameObject("Trunk");

        GameObject leafBillboard = CreateLeafBillboard(leafSize, leafMaterial);

        int chunkCount = 0;
        Mesh currentMesh = new Mesh();
        Dictionary<int, Mesh> segmentsCache = new Dictionary<int, Mesh>();
        Turtle current = new Turtle(Quaternion.identity, Vector3.zero, new Vector3(0, segmentHeight, 0));
        Stack<Turtle> stack = new Stack<Turtle>();
        for (int i = 0; i < moduleString.Length; i++)
        {
            string module = moduleString[i] + "";

            if (module == "F")
            {
                current.Forward();
                CreateSegment(
                    segmentAxisSamples,
                    segmentRadialSamples,
                    segmentWidth,
                    segmentHeight,
                    leafDensity,
                    narrowBranches,
                    trunkMaterial,
                    current, 
                    stack.Count, 
                    ref currentMesh, 
                    ref chunkCount, 
                    trunk, 
                    segmentsCache);
            }
            else if (module == "+")
            {
                current.RotateZ(angle);
            }
            else if (module == "-")
            {
                current.RotateZ(-angle);
            }
            else if (module == "&")
            {
                current.RotateX(angle);
            }
            else if (module == "^")
            {
                current.RotateX(-angle);
            }
            else if (module == "\\")
            {
                current.RotateY(angle);
            }
            else if (module == "/")
            {
                current.RotateY(-angle);
            }
            else if (module == "|")
            {
                current.RotateZ(180);
            }
            else if (module == "[")
            {
                stack.Push(current);
                current = new Turtle(current);
            }
            else if (module == "]")
            {
                if (useFoliage)
                {
                    AddFoliageAt(segmentWidth, segmentHeight, leafDensity, current, leafBillboard, leaves);
                }
                current = stack.Pop();
            }
        }

        CreateNewChunk(currentMesh, ref chunkCount, trunkMaterial, trunk);
        GameObject.Destroy(leafBillboard);
    }

}
