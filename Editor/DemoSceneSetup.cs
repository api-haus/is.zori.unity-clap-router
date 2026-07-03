using System.IO;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Zori.ClapRouter.Editor
{
    public static class DemoSceneSetup
    {
        private const string ObjectName = "ClapRouterDemo";

        [MenuItem("Zori/CLAP Router/Create Demo Player In Scene")]
        public static void CreateDemoPlayer()
        {
            string repo = RepoRoot();
            string hostBin = Path.Combine(repo, "build", "linux-debug", "bin", "clap-ipc");
            string sixSines = Path.Combine(repo, "tests", "fixtures", "SixSines.clap");
            string clapPlugins = Path.Combine(repo, "tests", "fixtures", "ClapPlugins.clap");
            string vital = Path.Combine(repo, "tests", "fixtures", "Vital.clap");
            string vitalPreset = Path.Combine(repo, "tests", "fixtures", "Drone_Keys_Pad.vital");

            GameObject go = GameObject.Find(ObjectName) ?? new GameObject(ObjectName);
            MusicRouterHost host = go.GetComponent<MusicRouterHost>() ?? go.AddComponent<MusicRouterHost>();
            ClapRouterDemoPlayer player =
                go.GetComponent<ClapRouterDemoPlayer>() ?? go.AddComponent<ClapRouterDemoPlayer>();
            ClapRouterParamInspector inspector =
                go.GetComponent<ClapRouterParamInspector>() ?? go.AddComponent<ClapRouterParamInspector>();
            ClapRouterNoteVisualizer visualizer =
                go.GetComponent<ClapRouterNoteVisualizer>() ?? go.AddComponent<ClapRouterNoteVisualizer>();

            SerializedObject hostObj = new SerializedObject(host);
            hostObj.FindProperty("hostBinaryPath").stringValue = hostBin;
            hostObj.FindProperty("instrumentClapPath").stringValue = "";
            hostObj.FindProperty("spawnHostProcess").boolValue = true;
            hostObj.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject playerObj = new SerializedObject(player);
            playerObj.FindProperty("host").objectReferenceValue = host;
            playerObj.FindProperty("sixSinesClapPath").stringValue = sixSines;
            playerObj.FindProperty("svfClapPath").stringValue = clapPlugins;
            playerObj.FindProperty("vitalClapPath").stringValue = vital;
            playerObj.FindProperty("vitalPresetPath").stringValue = vitalPreset;
            playerObj.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject inspectorObj = new SerializedObject(inspector);
            inspectorObj.FindProperty("player").objectReferenceValue = player;
            inspectorObj.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject visualizerObj = new SerializedObject(visualizer);
            visualizerObj.FindProperty("player").objectReferenceValue = player;
            visualizerObj.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);

            WarnMissing("clap-ipc host binary", hostBin,
                "build it: cmake --build build/linux-debug --target clap-ipc");
            WarnMissing("SixSines.clap", sixSines, "build it: tools/build_six_sines.sh");
            WarnMissing("ClapPlugins.clap", clapPlugins, "build it: tools/build_clap_plugins.sh");
            WarnMissing("Vital.clap", vital, "place the Vital CLAP at tests/fixtures/Vital.clap");
            WarnMissing("Drone_Keys_Pad.vital", vitalPreset, "place the preset at tests/fixtures/Drone_Keys_Pad.vital");

            Debug.Log($"[DemoSceneSetup] '{ObjectName}' ready. Save the scene, press Play, and listen — "
                + "you hear the continuous 3-track song immediately. For the keypress lead instrument, assign the "
                + "ClapRouterDemoPlayer's 'Keypress Action' to an InputActionReference (a Button action) in the "
                + "inspector; each press blips a random high note on a distinct 4th track. "
                + "In Play, on-screen per-track param panels (ClapRouterParamInspector) let you tweak each "
                + "instrument's params live (Vital + the Drone preset get their own panel). "
                + "Host stderr shows [clap-ipc][audio] device + callback peak. "
                + "To force a sink: set MR_AUDIO_DEVICE (e.g. 'pipewire' or 'pulse') before launching the editor.");
        }

        private static void WarnMissing(string what, string path, string howto)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                Debug.LogWarning($"[DemoSceneSetup] {what} not found at {path} — {howto}");
            }
        }

        private static string RepoRoot([CallerFilePath] string thisFile = "")
        {
            string package = Path.GetDirectoryName(Path.GetDirectoryName(thisFile));
            return Path.GetDirectoryName(package);
        }
    }
}
