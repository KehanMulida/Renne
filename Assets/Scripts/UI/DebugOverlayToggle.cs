using UnityEngine;

/// <summary>Shared visibility for development text panels. F3 toggles them during play.</summary>
public sealed class DebugOverlayToggle : MonoBehaviour
{
    public static bool Visible { get; private set; }
    private static DebugOverlayToggle instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        Visible=false;
        instance=null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if(instance!=null)return;
        var host=new GameObject("Debug Overlay Toggle (F3)");
        instance=host.AddComponent<DebugOverlayToggle>();
        DontDestroyOnLoad(host);
    }

    private void Update()
    {
        if(Input.GetKeyDown(KeyCode.F3))Visible=!Visible;
    }
}
