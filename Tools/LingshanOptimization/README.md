# Lingshan optimized architecture

Generated meshes are checked in under Assets/Art/PICOOptimized. Original FBX and scene materials are retained.

Source: tripo_convert_a8fc79f3-b0b5-45bd-a817-50b48b1bcc64.fbx, Unity mesh tripo_node_a8fc79f3 (1,291,567 vertices / 1,950,396 triangles).

The source-mesh.bin format is little endian: int32 vertexCount, int32 indexCount, vertexCount records of eight float32 values (position XYZ, normal XYZ, UV), then indexCount int32 indices. Export coordinates directly from Unity Mesh local space to avoid FBX axis/scale conversion. Use Mesh.vertices, Mesh.normals, Mesh.uv and Mesh.triangles.

Run Blender 5.2 in factory-startup background mode with decimate_mesh.py and `-- <scratch-directory>`. The script welds coincident position seams to six decimals while retaining per-corner UV, then generates 60,000 / 15,000 / 3,500 triangle meshes. Call LingshanPicoOptimization.ImportMeshes(scratchDirectory) in Unity MCP, then Apply and Validate. Do not overwrite the source FBX.

Geometry review screenshots use original materials and the same local coordinates. LOD thresholds are 0.25 / 0.06 / 0; lowest LOD stays visible. The model's base renderer and collider overrides are limited to the two matching scene instances.
