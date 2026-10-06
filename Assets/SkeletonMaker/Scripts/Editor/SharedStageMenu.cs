using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// SkeletonMaker > Add Shared Stage: adds what two instances need to meet -
    /// the "Stage Origin" (StageFrame) both calibrate onto, and a "Shared Stage"
    /// object holding the OSC link, the body sender and receiver, the floor-mark
    /// calibrator, the recorder and player, a switched-off "Fake Peer", and
    /// the TestPartner that brings the recording (or the fake peer) on in the
    /// Join stage. Replaces what a previous run added, keeping nothing of it.
    /// </summary>
    public static class SharedStageMenu
    {
        private const string OriginName = "Stage Origin";
        private const string RootName = "Shared Stage";

        [MenuItem("SkeletonMaker/Add Shared Stage")]
        public static void AddSharedStage()
        {
            foreach (var old in Object.FindObjectsByType<StageFrame>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Undo.DestroyObjectImmediate(old.gameObject);
            foreach (var old in Object.FindObjectsByType<OscLink>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Undo.DestroyObjectImmediate(old.gameObject);

            var origin = new GameObject(OriginName);
            Undo.RegisterCreatedObjectUndo(origin, "Add Shared Stage");
            origin.AddComponent<StageFrame>();

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Add Shared Stage");
            var link = root.AddComponent<OscLink>();
            var quad = Object.FindFirstObjectByType<RaymarchQuad>(FindObjectsInactive.Include);

            var sender = root.AddComponent<BodySender>();
            Wire(sender, ("link", link), ("quad", quad),
                ("bodySource", XsensMenu.SceneBodySource()));

            Wire(root.AddComponent<BodyReceiver>(), ("link", link), ("quad", quad));
            Wire(root.AddComponent<BodyRecorder>(), ("link", link));
            Wire(root.AddComponent<BodyPlayer>(), ("link", link));

            var xrOrigin = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include);
            Transform pointer = null;
            if (xrOrigin != null)
            {
                foreach (var child in xrOrigin.GetComponentsInChildren<Transform>(true))
                    if (child.name == "Right Controller") { pointer = child; break; }
            }
            Wire(root.AddComponent<StageCalibrator>(),
                ("xrOrigin", xrOrigin != null ? xrOrigin.transform : null), ("pointer", pointer));

            // Off until wanted: switched on, a second performer dances on the stage straight away.
            var fake = new GameObject("Fake Peer");
            fake.transform.SetParent(root.transform, false);
            Wire(fake.AddComponent<FakePeer>(), ("link", link), ("template", DanceAnimator()));
            fake.SetActive(false);

            AddTestPartner();

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("Shared Stage added. Give each PC its own Performer Id and the other's address on the OscLink. Alone, the Join stage shows your recording (H to make one), or the fake peer until there is one.");
        }

        /// <summary>Gives the Shared Stage its TestPartner if it has none yet,
        /// and wires it (again) to what is there. Changes nothing else.</summary>
        public static void AddTestPartner()
        {
            var link = Object.FindFirstObjectByType<OscLink>(FindObjectsInactive.Include);
            if (link == null)
            {
                Debug.LogWarning("SharedStageMenu: no Shared Stage in the open scene - run 'Add Shared Stage' first.");
                return;
            }

            var partner = link.GetComponent<TestPartner>();
            if (partner == null) partner = Undo.AddComponent<TestPartner>(link.gameObject);
            var fake = link.GetComponentInChildren<FakePeer>(true);
            var recordGuide = StagesMenu.FindByName(StagesMenu.RecordGuideName);
            Wire(partner, ("link", link),
                ("recorder", link.GetComponent<BodyRecorder>()), ("player", link.GetComponent<BodyPlayer>()),
                ("receiver", link.GetComponent<BodyReceiver>()),
                ("fakePeer", fake != null ? fake.gameObject : null),
                ("label", recordGuide != null ? recordGuide.GetComponent<TextMesh>() : null));

            // The partner switches it on when it is wanted.
            if (fake != null && fake.gameObject.activeSelf)
            {
                Undo.RecordObject(fake.gameObject, "Add Test Partner");
                fake.gameObject.SetActive(false);
            }
            EditorSceneManager.MarkSceneDirty(link.gameObject.scene);
        }

        // The Animator the avatar's Animation mode dances with.
        private static Animator DanceAnimator()
        {
            var avatar = Object.FindFirstObjectByType<AvatarDanceSource>(FindObjectsInactive.Include);
            if (avatar == null) return null;
            return new SerializedObject(avatar).FindProperty("sourceAnimator").objectReferenceValue as Animator;
        }

        private static void Wire(Object component, params (string property, Object value)[] references)
        {
            var so = new SerializedObject(component);
            foreach (var (property, value) in references) so.FindProperty(property).objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        /// <summary>Writes one message of every kind of argument and reads it back.</summary>
        [MenuItem("SkeletonMaker/Check OSC Codec")]
        public static void CheckOscCodec()
        {
            byte[] datagram = new OscWriter().Begin(OscBody.Address(7, OscBody.Element))
                .Int(-123456).Float(1.5f).String("Bone_LeftUpperArm_LeftLowerArm").String("abc").Float(-0.25f).Int(1).ToArray();

            var messages = new List<OscMessage>();
            OscCodec.Parse(datagram, messages);
            bool ok = datagram.Length % 4 == 0 && messages.Count == 1;
            if (ok)
            {
                var m = messages[0];
                ok = OscBody.TryParseAddress(m.Address, out int id, out string leaf) && id == 7 && leaf == OscBody.Element
                     && m.Int() == -123456 && m.Float() == 1.5f && m.String() == "Bone_LeftUpperArm_LeftLowerArm"
                     && m.String() == "abc" && m.Float() == -0.25f && m.Int() == 1 && !m.Bad;
                m.Int(); // one more than there is
                ok &= m.Bad;
            }

            if (ok) Debug.Log($"OSC codec: round trip fine ({datagram.Length} bytes).");
            else Debug.LogError("OSC codec: the message read back differs from the one written.");
        }
    }
}
