using Meta.XR;
using UnityEditor;
using UnityEngine.XR.OpenXR;

namespace SkeletonMaker
{
    /// <summary>
    /// SkeletonMaker > Body Tracking In Editor (Link): switches the Meta XR
    /// Feature on/off for the Standalone target, i.e. for Play mode over Quest
    /// Link. On is what Tracking mode needs in the editor; off takes the Meta
    /// plugin out of editor Play entirely, for when Link is misbehaving (it
    /// blocks the editor while it waits for a Link session that isn't up).
    /// Headset builds (Android) are not affected.
    /// </summary>
    public static class BodyTrackingLinkMenu
    {
        private const string MenuPath = "SkeletonMaker/Body Tracking In Editor (Link)";

        private static MetaXRFeature Feature
        {
            get
            {
                var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
                return settings != null ? settings.GetFeature<MetaXRFeature>() : null;
            }
        }

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            var feature = Feature;
            if (feature == null) return;
            feature.enabled = !feature.enabled;
            EditorUtility.SetDirty(feature);
            AssetDatabase.SaveAssets();
        }

        [MenuItem(MenuPath, true)]
        private static bool Validate()
        {
            var feature = Feature;
            Menu.SetChecked(MenuPath, feature != null && feature.enabled);
            return feature != null && !EditorApplication.isPlayingOrWillChangePlaymode;
        }
    }
}
