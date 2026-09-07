#if UNITY_EDITOR
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using UnityEditor;
using UnityEngine;

// Connect only this project to its dedicated local MCP service.
// Does not start/stop the shared service or change any scene or player settings.
public static class LingshanMcpConnection
{
    [InitializeOnLoadMethod]
    private static void ScheduleConnect()
    {
        if (Application.isBatchMode || !Application.dataPath.EndsWith("/WukongEnvironmentStarter/Assets")) return;
        EditorApplication.delayCall += Connect;
    }

    [MenuItem("Tools/Lingshan/Connect Existing MCP Service")]
    public static async void Connect()
    {
        const string endpoint = "http://127.0.0.1:8098";
        bool changed = SessionState.GetString("MCPForUnity.ProjectHttpBaseUrl", "") != endpoint;
        SessionState.SetString("MCPForUnity.ProjectHttpBaseUrl", endpoint);
        if (changed && MCPServiceLocator.TransportManager.IsRunning(TransportMode.Http))
            await MCPServiceLocator.TransportManager.StopAsync(TransportMode.Http);
        if (MCPServiceLocator.TransportManager.IsRunning(TransportMode.Http)) return;
        try
        {
            bool connected = await MCPServiceLocator.TransportManager.StartAsync(TransportMode.Http);
            Debug.Log("LINGSHAN_MCP_CONNECTION " + connected);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("Lingshan MCP connection: " + exception.Message);
        }
    }
}
#endif
