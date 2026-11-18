#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
#if VRC_SDK_VRCSDK3
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
#endif

namespace liltoon_MCR
{
    // Material Contacts mode (_MCR_ContactMode, see custom_insert.hlsl): occlusion and contact shadow from capsules
    // instead of the camera depth texture.
    //
    // Builds "MCR_Contacts" under the avatar root:
    //   - Contact Senders for the head, torso, upper arms, forearms, hands and thighs (Root Transform = the bone,
    //     nothing is added inside the armature);
    //   - Contact Receivers with "Write to Shaders" (VRChat SDK 3.10.6 or newer), each writing the nearest 8 senders to
    //     up to 4 renderers that use Macaron materials.
    //
    // VRChat writes the transform of a sender but not its size, so the size travels in the tags: the receiver lists
    // "lilMCR", 5 radius bits and 5 half length bits, a sender carries the bits of its own size, and the shader gets
    // "which of the receiver's tags does this sender have" as a bit mask. The component sizes are snapped to the
    // same grid, so the VRChat gizmo, the contact and the shader agree. The units and the tag order must match
    // custom_insert.hlsl (MCR_MC_RADIUS_UNIT, MCR_MC_LENGTH_UNIT).
    public static class MacaronContactRig
    {
        public const string ObjectName = "MCR_Contacts";
        public const string ShaderParameter = "lilMCR";
        public const string BaseTag = "lilMCR";
        private const int SizeBits = 5;
        private const float RadiusUnit = 0.005f;
        private const float LengthUnit = 0.01f;
        private const int RenderersPerReceiver = 4;
        private const int MaxReceivers = 4;

        public struct Result
        {
            public bool Ok;
            public int Senders;
            public int Receivers;
            public int Renderers;
            public int SkippedRenderers;
            public string Error;
        }

        // The receiver fields of "Write to Shaders" exist (VRChat SDK 3.10.6 or newer).
        public static bool Supported
        {
            get
            {
#if VRC_SDK_VRCSDK3
                return typeof(ContactReceiver).GetField("writeToMaterials", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
#else
                return false;
#endif
            }
        }

        public static Transform Find(GameObject avatar)
        {
            return avatar != null ? avatar.transform.Find(ObjectName) : null;
        }

        public static bool Remove(GameObject avatar)
        {
            Transform t = Find(avatar);
            if(t == null) return false;
            Undo.DestroyObjectImmediate(t.gameObject);
            return true;
        }

        public static string ResultMessage(Result r)
        {
            if(!r.Ok) return ML.F("Material Contacts 無法建立：{0}", r.Error);
            string text = ML.F("已建立 Material Contacts：{0} 個 Sender、{1} 個 Receiver，寫入 {2} 個 Renderer。", r.Senders, r.Receivers, r.Renderers);
            if(r.SkippedRenderers > 0) text += "\n" + ML.F("有 {0} 個 Renderer 超過上限，沒有寫入（維持沒有遮蔽）。", r.SkippedRenderers);
            return text;
        }

#if VRC_SDK_VRCSDK3
        private static string RadiusTag(int bit) { return BaseTag + "_r" + bit; }
        private static string LengthTag(int bit) { return BaseTag + "_h" + bit; }

        // Receiver tag order = bit order in the shader: base, radius bits, half length bits.
        private static List<string> ReceiverTags()
        {
            List<string> tags = new List<string> { BaseTag };
            for(int i = 0; i < SizeBits; i++) tags.Add(RadiusTag(i));
            for(int i = 0; i < SizeBits; i++) tags.Add(LengthTag(i));
            return tags;
        }

        private static List<string> SenderTags(int radiusValue, int lengthValue)
        {
            List<string> tags = new List<string> { BaseTag };
            for(int i = 0; i < SizeBits; i++) if(((radiusValue >> i) & 1) != 0) tags.Add(RadiusTag(i));
            for(int i = 0; i < SizeBits; i++) if(((lengthValue >> i) & 1) != 0) tags.Add(LengthTag(i));
            return tags;
        }

        private static float MaxScale(Transform t)
        {
            Vector3 s = t.lossyScale;
            return Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
        }

        // Capsule between the world points a and b (centers of the caps) with a world radius; a == b = sphere.
        private static bool AddSender(Transform holder, string name, Transform bone, Vector3 a, Vector3 b, float radius)
        {
            if(bone == null) return false;
            float scale = MaxScale(bone);
            // The size grid is in the bone's local units: a bone with an extreme scale cannot be represented.
            if(scale < 0.2f || scale > 5f) return false;

            int maxValue = (1 << SizeBits) - 1;
            int radiusValue = Mathf.Clamp(Mathf.RoundToInt(radius / scale / RadiusUnit) - 1, 0, maxValue);
            int lengthValue = Mathf.Clamp(Mathf.RoundToInt(Vector3.Distance(a, b) * 0.5f / scale / LengthUnit), 0, maxValue);
            float localRadius = (radiusValue + 1) * RadiusUnit;
            float localHalf = lengthValue * LengthUnit;

            GameObject go = new GameObject(name);
            go.transform.SetParent(holder, false);
            VRCContactSender sender = go.AddComponent<VRCContactSender>();
            sender.rootTransform = bone;
            sender.shapeType = lengthValue > 0 ? ContactBase.ShapeType.Capsule : ContactBase.ShapeType.Sphere;
            sender.radius = localRadius;
            sender.height = (localHalf + localRadius) * 2f;      // along Y, caps included
            sender.position = bone.InverseTransformPoint((a + b) * 0.5f);
            Vector3 axis = bone.InverseTransformDirection(b - a);
            sender.rotation = axis.sqrMagnitude > 1e-10f ? Quaternion.FromToRotation(Vector3.up, axis.normalized) : Quaternion.identity;
            sender.localOnly = false;
            sender.collisionTags = SenderTags(radiusValue, lengthValue);
            EditorUtility.SetDirty(sender);
            return true;
        }

        private static bool AddLimb(Transform holder, string name, Transform from, Transform to, float start, float end, float radiusByLength)
        {
            if(from == null || to == null) return false;
            Vector3 p0 = from.position;
            Vector3 p1 = to.position;
            float length = Vector3.Distance(p0, p1);
            if(length < 1e-4f) return false;
            return AddSender(holder, name, from, Vector3.Lerp(p0, p1, start), Vector3.Lerp(p0, p1, end), length * radiusByLength);
        }

        private static bool SetBool(SerializedObject so, string name, bool value)
        {
            SerializedProperty p = so.FindProperty(name);
            if(p == null) return false;
            p.boolValue = value;
            return true;
        }

        private static bool AddReceiver(Transform holder, string name, Transform root, float worldRadius, List<Renderer> renderers, int first)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(holder, false);
            VRCContactReceiver receiver = go.AddComponent<VRCContactReceiver>();
            receiver.rootTransform = root;
            receiver.shapeType = ContactBase.ShapeType.Sphere;
            receiver.radius = Mathf.Min(worldRadius, 3f) / Mathf.Max(MaxScale(root), 1e-4f);
            receiver.position = Vector3.zero;
            receiver.rotation = Quaternion.identity;
            receiver.localOnly = false;
            receiver.allowSelf = true;
            receiver.allowOthers = true;
            receiver.receiverType = ContactReceiver.ReceiverType.Proximity;
            receiver.parameter = string.Empty;          // no animator parameter: shader properties only
            receiver.collisionTags = ReceiverTags();

            // "Write to Shaders" fields by name, so that this file still compiles with an older SDK.
            SerializedObject so = new SerializedObject(receiver);
            bool ok = SetBool(so, "writeToMaterials", true);
            ok &= SetBool(so, "writeToMaterialsContactTags", true);
            SerializedProperty parameter = so.FindProperty("separateShaderParameter");
            SerializedProperty targets = so.FindProperty("shaderTargetRenderers");
            if(parameter == null || targets == null || !targets.isArray) ok = false;
            if(ok)
            {
                parameter.stringValue = ShaderParameter;
                int count = Mathf.Min(RenderersPerReceiver, renderers.Count - first);
                targets.arraySize = count;
                for(int i = 0; i < count; i++) targets.GetArrayElementAtIndex(i).objectReferenceValue = renderers[first + i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(receiver);
            return ok;
        }
#endif

        // Rebuilds MCR_Contacts for the avatar (an existing one is replaced).
        public static Result Setup(GameObject avatar)
        {
            Result result = new Result();
#if VRC_SDK_VRCSDK3
            if(avatar == null) { result.Error = ML.T("沒有選擇角色。"); return result; }
            if(!Supported) { result.Error = ML.T("需要 VRChat SDK 3.10.6 以上。"); return result; }
            Animator animator = avatar.GetComponent<Animator>();
            if(animator == null || !animator.isHuman) { result.Error = ML.T("角色不是人形（Humanoid），找不到骨架。"); return result; }

            List<Renderer> renderers = new List<Renderer>();
            foreach(Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if(!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
                bool uses = false;
                foreach(Material m in r.sharedMaterials) if(MacaronCore.IsMacaronMaterial(m)) { uses = true; break; }
                if(uses) renderers.Add(r);
            }
            if(renderers.Count == 0) { result.Error = ML.T("角色上沒有使用 Macaron 材質的 Renderer。"); return result; }

            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            Transform chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if(chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if(chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Spine);
            if(chest == null) chest = hips;
            if(head == null || hips == null) { result.Error = ML.T("找不到頭或臀部的骨骼。"); return result; }

            // Eye height in world units: the measure every size below is taken from.
            Vector3 up = avatar.transform.up;
            float eyeHeight = Vector3.Dot(head.position - avatar.transform.position, up) * 1.06f;
            VRCAvatarDescriptor descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            if(descriptor != null)
            {
                float view = Vector3.Dot(avatar.transform.TransformPoint(descriptor.ViewPosition) - avatar.transform.position, up);
                if(view > 0.05f) eyeHeight = view;
            }
            if(eyeHeight < 0.05f) { result.Error = ML.T("無法判斷角色的高度。"); return result; }

            Remove(avatar);
            GameObject holderObject = new GameObject(ObjectName);
            holderObject.transform.SetParent(avatar.transform, false);
            Undo.RegisterCreatedObjectUndo(holderObject, "Macaron Material Contacts");
            Transform holder = holderObject.transform;

            // Senders. Sizes follow the bone lengths; the head and hand have no end bone, they follow the eye height.
            int senders = 0;
            Vector3 headCenter = head.position + up * (eyeHeight * 0.045f);
            if(AddSender(holder, "Head", head, headCenter, headCenter, eyeHeight * 0.07f)) senders++;
            Vector3 torsoTop = neck != null ? neck.position : head.position;
            if(AddSender(holder, "Torso", chest, Vector3.Lerp(hips.position, torsoTop, 0.2f), Vector3.Lerp(hips.position, torsoTop, 0.75f), eyeHeight * 0.075f)) senders++;
            for(int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                string suffix = left ? " L" : " R";
                Transform upperArm = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                Transform lowerArm = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                Transform hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                Transform upperLeg = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                Transform lowerLeg = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                if(AddLimb(holder, "Upper Arm" + suffix, upperArm, lowerArm, 0.2f, 0.85f, 0.15f)) senders++;
                if(AddLimb(holder, "Forearm" + suffix, lowerArm, hand, 0.15f, 0.9f, 0.12f)) senders++;
                if(hand != null && lowerArm != null)
                {
                    Vector3 forward = hand.position - lowerArm.position;
                    if(forward.sqrMagnitude > 1e-8f)
                    {
                        Vector3 center = hand.position + forward.normalized * (eyeHeight * 0.04f);
                        if(AddSender(holder, "Hand" + suffix, hand, center, center, eyeHeight * 0.032f)) senders++;
                    }
                }
                if(AddLimb(holder, "Thigh" + suffix, upperLeg, lowerLeg, 0.15f, 0.9f, 0.14f)) senders++;
            }

            // Receivers: 4 renderers each. Centered on the chest, wide enough for the whole body.
            int receivers = 0;
            bool fieldsOk = true;
            for(int first = 0; first < renderers.Count && receivers < MaxReceivers; first += RenderersPerReceiver)
            {
                fieldsOk &= AddReceiver(holder, "Receiver " + (receivers + 1), chest, eyeHeight * 1.2f, renderers, first);
                receivers++;
            }
            if(!fieldsOk)
            {
                Undo.DestroyObjectImmediate(holderObject);
                result.Error = ML.T("這個版本的 VRChat SDK 沒有「Write to Shaders」的欄位。");
                return result;
            }

            result.Ok = true;
            result.Senders = senders;
            result.Receivers = receivers;
            result.Renderers = Mathf.Min(renderers.Count, receivers * RenderersPerReceiver);
            result.SkippedRenderers = renderers.Count - result.Renderers;
#else
            result.Error = ML.T("需要 VRChat SDK。");
#endif
            return result;
        }
    }
}
#endif
