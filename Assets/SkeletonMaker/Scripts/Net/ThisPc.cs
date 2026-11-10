using System.IO;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// The settings that differ between the two PCs of a two-PC performance,
    /// kept out of the scene so both PCs run the very same committed scene:
    /// <c>ThisPc.json</c> in the project folder (next to Assets, not in git),
    /// written by deploy.ps1 on both PCs or by hand:
    /// <code>
    /// { "performerId": 2, "remoteHost": "192.168.2.20", "stageShift": { "x": 1, "y": 0, "z": 0 } }
    /// </code>
    /// Every field is optional; one that is left out keeps what the scene has
    /// (OscLink's Performer Id and Remote Host, StageFrame's Stage Shift).
    /// Only read in the editor, which is where a Link PC runs the app: the
    /// standalone Quest keeps its scene settings.
    /// </summary>
    public static class ThisPc
    {
        public const string FileName = "ThisPc.json";

        [System.Serializable]
        private class Data
        {
            public int performerId;
            public string remoteHost;
            public Vector3 stageShift;
        }

        private static bool loaded;
        private static Data data;
        private static bool hasShift;

        public static string FilePath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", FileName));

        private static Data Load()
        {
            if (loaded) return data;
            loaded = true;
            if (!Application.isEditor) return null;
            string path = FilePath;
            if (!File.Exists(path)) return null;
            try
            {
                string json = File.ReadAllText(path);
                data = JsonUtility.FromJson<Data>(json);
                hasShift = json.Contains("\"stageShift\"");
                Debug.Log($"ThisPc: using {path}" +
                          (data.performerId > 0 ? $", performer {data.performerId}" : "") +
                          (!string.IsNullOrEmpty(data.remoteHost) ? $", other PC {data.remoteHost}" : "") +
                          (hasShift ? $", stage shift {data.stageShift}" : "") + ".");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"ThisPc: couldn't read {path}: {e.Message}. The scene's settings are used.");
                data = null;
            }
            return data;
        }

        public static bool TryGetPerformerId(out int id)
        {
            var d = Load();
            id = d != null ? d.performerId : 0;
            return id > 0;
        }

        public static bool TryGetRemoteHost(out string host)
        {
            var d = Load();
            host = d?.remoteHost;
            return !string.IsNullOrWhiteSpace(host);
        }

        public static bool TryGetStageShift(out Vector3 shift)
        {
            var d = Load();
            shift = d != null ? d.stageShift : Vector3.zero;
            return d != null && hasShift;
        }

#if UNITY_EDITOR
        // Read again on the next Play, in case deploy.ps1 changed it.
        [UnityEditor.InitializeOnEnterPlayMode]
        private static void Forget()
        {
            loaded = false;
            data = null;
            hasShift = false;
        }
#endif
    }
}
