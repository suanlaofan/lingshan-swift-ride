#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class LingshanGeometryReview
{
    public static void Capture(string output)
    {
        Directory.CreateDirectory(output);
        MeshFilter target = null;
        foreach (var mf in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            if (mf.sharedMesh != null && mf.sharedMesh.name == "LingshanArchitecture_LOD0") { target = mf; break; }
        if (target == null) throw new Exception("Optimized model not found");
        string sourcePath = "Assets/模型资产/tripo_convert_a8fc79f3-b0b5-45bd-a817-50b48b1bcc64/tripo_convert_a8fc79f3-b0b5-45bd-a817-50b48b1bcc64.fbx";
        Mesh original = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(sourcePath)) if (asset is Mesh m && m.vertexCount > 500000) original = m;
        if (original == null) throw new Exception("Original mesh missing");
        Mesh saved = target.sharedMesh;
        var renderers = target.GetComponentsInChildren<Renderer>();
        var layers = new int[renderers.Length]; var enabled = new bool[renderers.Length];
        var group = target.GetComponent<LODGroup>();
        bool groupEnabled = group.enabled;
        var go = new GameObject("Temporary Lingshan Geometry Review");
        var camera = go.AddComponent<Camera>(); camera.enabled = false;
        var data = go.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = false; data.requiresColorOption = CameraOverrideOption.Off; data.requiresDepthOption = CameraOverrideOption.Off;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.1f, 0.13f, 0.18f); camera.cullingMask = 1 << 31; camera.fieldOfView = 45; camera.nearClipPlane = 0.1f; camera.farClipPlane = 200;
        var rt = new RenderTexture(1024, 768, 24, RenderTextureFormat.ARGB32); rt.Create();
        RenderTexture previous = RenderTexture.active;
        try
        {
            group.enabled = false;
            for (int i=0;i<renderers.Length;i++) { layers[i]=renderers[i].gameObject.layer; enabled[i]=renderers[i].enabled;renderers[i].gameObject.layer=31;renderers[i].enabled=renderers[i].gameObject==target.gameObject; }
            Bounds bounds = target.GetComponent<Renderer>().bounds;
            for(int angle=0;angle<2;angle++)
            {
                Vector3 offset = angle==0 ? new Vector3(0.7f,0.4f,-1f) : new Vector3(-0.7f,0.25f,1f);
                camera.transform.position = bounds.center + offset.normalized * bounds.size.magnitude * 1.3f;
                camera.transform.LookAt(bounds.center);
                for(int lod=-1;lod<3;lod++)
                {
                    target.sharedMesh = lod < 0 ? original : AssetDatabase.LoadAssetAtPath<Mesh>(LingshanPicoOptimization.MeshFolder+"/LingshanArchitecture_LOD"+lod+".asset");
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                    RenderTexture.active = rt;
                    var image = new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();
                    File.WriteAllBytes(Path.Combine(output,"view"+angle+"-"+(lod<0?"original":"LOD"+lod)+".png"),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
                }
            }
        }
        finally
        {
            target.sharedMesh = saved; group.enabled = groupEnabled;
            for(int i=0;i<renderers.Length;i++){renderers[i].gameObject.layer=layers[i];renderers[i].enabled=enabled[i];}
            RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(go);
        }
        Debug.Log("LINGSHAN_GEOMETRY_REVIEW_CAPTURED " + output);
    }
}
#endif
