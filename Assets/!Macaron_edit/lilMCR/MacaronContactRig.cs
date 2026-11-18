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
    //   - 8 Contact Senders: head, torso, upper arms, forearms and hands (Root Transform = the bone, nothing is added
    //     inside the armature), sized from the avatar's meshes;
    //   - Contact Receivers with "Write to Shaders" (VRChat SDK 3.10.6 or newer), each writing those 8 senders to
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
        private const int MaxReceivers = 6;         // 8 senders + 6 receivers = 14 contacts ("Good" allows 16)

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

        // Body parts the capsules stand for (index into the vertex lists).
        private const int SegHead = 0, SegTorso = 1, SegUpperArmL = 2, SegForearmL = 3, SegHandL = 4, SegUpperArmR = 5, SegForearmR = 6, SegHandR = 7, SegLoose = 8, SegCount = 9;
        private const int MinFitPoints = 24;

        private static void MapBone(Dictionary<Transform, int> map, Animator animator, HumanBodyBones bone, int segment)
        {
            Transform t = animator.GetBoneTransform(bone);
            if(t != null && !map.ContainsKey(t)) map[t] = segment;
        }

        // World positions of the avatar's skinned vertices in the current pose, by the body part of their heaviest
        // bone: the humanoid bone itself or, on the arms and hands, a helper below it such as a twist bone.
        // SegLoose: everything else that is not below the head (clothes with an armature of their own, skirts,
        // accessories); the arms take from it what lies around them, which is how a sleeve is measured.
        private static List<Vector3>[] CollectPoints(GameObject avatar, Animator animator)
        {
            List<Vector3>[] points = new List<Vector3>[SegCount];
            for(int i = 0; i < SegCount; i++) points[i] = new List<Vector3>();

            Dictionary<Transform, int> map = new Dictionary<Transform, int>();
            HashSet<Transform> humanoid = new HashSet<Transform>();
            for(int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                Transform t = animator.GetBoneTransform((HumanBodyBones)i);
                if(t != null) humanoid.Add(t);
            }
            MapBone(map, animator, HumanBodyBones.Head, SegHead);
            MapBone(map, animator, HumanBodyBones.Hips, SegTorso);
            MapBone(map, animator, HumanBodyBones.Spine, SegTorso);
            MapBone(map, animator, HumanBodyBones.Chest, SegTorso);
            MapBone(map, animator, HumanBodyBones.UpperChest, SegTorso);
            MapBone(map, animator, HumanBodyBones.LeftUpperArm, SegUpperArmL);
            MapBone(map, animator, HumanBodyBones.LeftLowerArm, SegForearmL);
            MapBone(map, animator, HumanBodyBones.LeftHand, SegHandL);
            MapBone(map, animator, HumanBodyBones.RightUpperArm, SegUpperArmR);
            MapBone(map, animator, HumanBodyBones.RightLowerArm, SegForearmR);
            MapBone(map, animator, HumanBodyBones.RightHand, SegHandR);
            for(int i = (int)HumanBodyBones.LeftThumbProximal; i <= (int)HumanBodyBones.LeftLittleDistal; i++) MapBone(map, animator, (HumanBodyBones)i, SegHandL);
            for(int i = (int)HumanBodyBones.RightThumbProximal; i <= (int)HumanBodyBones.RightLittleDistal; i++) MapBone(map, animator, (HumanBodyBones)i, SegHandR);

            Transform headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            Mesh baked = new Mesh();
            try
            {
                foreach(SkinnedMeshRenderer smr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                {
                    Mesh mesh = smr.sharedMesh;
                    if(mesh == null || !smr.enabled || smr.CompareTag("EditorOnly")) continue;
                    Transform[] bones = smr.bones;
                    BoneWeight[] weights = mesh.boneWeights;
                    if(bones == null || weights == null || weights.Length != mesh.vertexCount) continue;

                    // Body part of every bone of this renderer (-1: none).
                    int[] segmentOfBone = new int[bones.Length];
                    for(int i = 0; i < bones.Length; i++)
                    {
                        segmentOfBone[i] = -1;
                        Transform t = bones[i];
                        if(t == null) continue;
                        int segment;
                        if(map.TryGetValue(t, out segment)) { segmentOfBone[i] = segment; continue; }
                        if(humanoid.Contains(t)) continue;
                        segmentOfBone[i] = headBone != null && t.IsChildOf(headBone) ? -1 : SegLoose;
                        // Helper bone (twist and the like): up to 2 levels below an arm or hand bone.
                        Transform parent = t.parent;
                        for(int level = 0; level < 2 && parent != null; level++, parent = parent.parent)
                        {
                            if(map.TryGetValue(parent, out segment))
                            {
                                if(segment != SegHead && segment != SegTorso) segmentOfBone[i] = segment;
                                break;
                            }
                            if(humanoid.Contains(parent)) break;
                        }
                    }

                    baked.Clear();
                    smr.BakeMesh(baked, true);
                    Vector3[] vertices = baked.vertices;
                    if(vertices.Length != weights.Length) continue;
                    // Baked with the renderer's scale: only its position and rotation are left to apply.
                    Matrix4x4 toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    for(int v = 0; v < vertices.Length; v++)
                    {
                        int bone = weights[v].boneIndex0;
                        if(bone < 0 || bone >= segmentOfBone.Length) continue;
                        int segment = segmentOfBone[bone];
                        if(segment >= 0) points[segment].Add(toWorld.MultiplyPoint3x4(vertices[v]));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }
            return points;
        }

        private static float Percentile(List<float> values, float t)
        {
            if(values.Count == 0) return 0f;
            values.Sort();
            return values[Mathf.Clamp(Mathf.RoundToInt((values.Count - 1) * t), 0, values.Count - 1)];
        }

        // Radius of a capsule around the segment a-b: the given percentile of the distance of the part's vertices
        // (those beside the segment between t0 and t1) to the axis. Falls back when there are too few.
        private static float FitRadius(List<Vector3> points, Vector3 a, Vector3 b, float t0, float t1, float percentile, float fallback, float min, float max)
        {
            return FitRadius(points, null, a, b, t0, t1, percentile, fallback, min, max);
        }

        // loose: vertices of no body part, counted when they are within max of the axis.
        private static float FitRadius(List<Vector3> points, List<Vector3> loose, Vector3 a, Vector3 b, float t0, float t1, float percentile, float fallback, float min, float max)
        {
            Vector3 ab = b - a;
            float length2 = ab.sqrMagnitude;
            if(points == null || length2 < 1e-10f) return fallback;
            List<float> distances = new List<float>();
            foreach(Vector3 p in points)
            {
                float t = Vector3.Dot(p - a, ab) / length2;
                if(t < t0 || t > t1) continue;
                distances.Add(Vector3.Distance(p, a + ab * t));
            }
            if(loose != null)
            {
                foreach(Vector3 p in loose)
                {
                    float t = Vector3.Dot(p - a, ab) / length2;
                    if(t < t0 || t > t1) continue;
                    float d = Vector3.Distance(p, a + ab * t);
                    if(d <= max) distances.Add(d);
                }
            }
            if(distances.Count < MinFitPoints) return fallback;
            return Mathf.Clamp(Percentile(distances, percentile), min, max);
        }

        // Sphere around the middle of the part's vertices (2% - 98% box).
        private static void FitSphere(List<Vector3> points, ref Vector3 center, ref float radius, float min, float max)
        {
            if(points == null || points.Count < MinFitPoints) return;
            List<float> x = new List<float>(points.Count), y = new List<float>(points.Count), z = new List<float>(points.Count);
            foreach(Vector3 p in points) { x.Add(p.x); y.Add(p.y); z.Add(p.z); }
            Vector3 lo = new Vector3(Percentile(x, 0.02f), Percentile(y, 0.02f), Percentile(z, 0.02f));
            Vector3 hi = new Vector3(Percentile(x, 0.98f), Percentile(y, 0.98f), Percentile(z, 0.98f));
            center = (lo + hi) * 0.5f;
            Vector3 size = hi - lo;
            radius = Mathf.Clamp((size.x + size.y + size.z) / 6f, min, max);
        }

        // Moves the ends of a segment inward so that the round caps of the capsule end about where the segment did.
        private static void InsetCaps(ref Vector3 a, ref Vector3 b, float radius, float insetA, float insetB)
        {
            Vector3 ab = b - a;
            float length = ab.magnitude;
            if(length < 1e-5f) return;
            Vector3 dir = ab / length;
            float ia = radius * insetA;
            float ib = radius * insetB;
            float total = ia + ib;
            if(total > length) { ia *= length / total; ib *= length / total; }
            a += dir * ia;
            b -= dir * ib;
        }

        private static bool RendererShown(Renderer r)
        {
            return r.enabled && r.gameObject.activeInHierarchy && !r.CompareTag("EditorOnly");
        }

        private static int RendererVertexCount(Renderer r)
        {
            Mesh mesh = null;
            SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
            if(smr != null) mesh = smr.sharedMesh;
            else
            {
                MeshFilter filter = r.GetComponent<MeshFilter>();
                if(filter != null) mesh = filter.sharedMesh;
            }
            return mesh != null ? mesh.vertexCount : 0;
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
            receiver.allowOthers = false;        // only 8 senders reach the shader: the avatar's own
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
            // When there are more renderers than the receivers can write to: the ones shown now first, then the
            // largest meshes (stable for equal keys, so the hierarchy order is kept).
            List<Renderer> ordered = new List<Renderer>(renderers);
            renderers.Sort((x, y) =>
            {
                int byShown = RendererShown(y).CompareTo(RendererShown(x));
                if(byShown != 0) return byShown;
                int bySize = RendererVertexCount(y).CompareTo(RendererVertexCount(x));
                return bySize != 0 ? bySize : ordered.IndexOf(x).CompareTo(ordered.IndexOf(y));
            });

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

            // Senders: exactly 8, the number a receiver writes, so that none is ever dropped: head, torso, upper arms,
            // forearms and hands. Sizes are measured on the avatar's meshes in its current pose (clothes included);
            // where a part has no vertices of its own, they follow the bone lengths and the eye height.
            List<Vector3>[] points = CollectPoints(avatar, animator);
            int senders = 0;
            Vector3 headCenter = head.position + up * (eyeHeight * 0.045f);
            float headRadius = eyeHeight * 0.07f;
            FitSphere(points[SegHead], ref headCenter, ref headRadius, eyeHeight * 0.05f, eyeHeight * 0.12f);
            if(AddSender(holder, "Head", head, headCenter, headCenter, headRadius)) senders++;

            Vector3 torsoTop = neck != null ? neck.position : head.position;
            Vector3 torsoA = hips.position;
            Vector3 torsoB = torsoTop;
            float torsoRadius = FitRadius(points[SegTorso], torsoA, torsoB, 0.1f, 1f, 0.45f, eyeHeight * 0.075f, eyeHeight * 0.06f, eyeHeight * 0.13f);
            InsetCaps(ref torsoA, ref torsoB, torsoRadius, 0.6f, 0.9f);
            if(AddSender(holder, "Torso", chest, torsoA, torsoB, torsoRadius)) senders++;

            for(int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                string suffix = left ? " L" : " R";
                Transform upperArm = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                Transform lowerArm = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                Transform hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if(upperArm == null || lowerArm == null || hand == null) continue;
                float upperLength = Vector3.Distance(upperArm.position, lowerArm.position);
                float lowerLength = Vector3.Distance(lowerArm.position, hand.position);
                if(upperLength < 1e-4f || lowerLength < 1e-4f) continue;

                Vector3 a = upperArm.position;
                Vector3 b = lowerArm.position;
                float radius = FitRadius(points[left ? SegUpperArmL : SegUpperArmR], points[SegLoose], a, b, 0.25f, 1f, 0.75f, upperLength * 0.15f, upperLength * 0.12f, upperLength * 0.42f);
                InsetCaps(ref a, ref b, radius, 1f, 0.7f);
                if(AddSender(holder, "Upper Arm" + suffix, upperArm, a, b, radius)) senders++;

                a = lowerArm.position;
                b = hand.position;
                radius = FitRadius(points[left ? SegForearmL : SegForearmR], points[SegLoose], a, b, 0f, 1f, 0.75f, lowerLength * 0.12f, lowerLength * 0.1f, lowerLength * 0.45f);
                InsetCaps(ref a, ref b, radius, 0.7f, 0.8f);
                if(AddSender(holder, "Forearm" + suffix, lowerArm, a, b, radius)) senders++;

                // Hand: from the wrist along the fingers, as far as the hand's vertices go.
                Vector3 forward = (hand.position - lowerArm.position).normalized;
                float handLength = eyeHeight * 0.085f;
                List<Vector3> handPoints = points[left ? SegHandL : SegHandR];
                if(handPoints.Count >= MinFitPoints)
                {
                    Vector3 mean = Vector3.zero;
                    foreach(Vector3 q in handPoints) mean += q;
                    Vector3 toMean = mean / handPoints.Count - hand.position;
                    if(toMean.sqrMagnitude > 1e-8f) forward = toMean.normalized;
                    List<float> along = new List<float>(handPoints.Count);
                    foreach(Vector3 q in handPoints) along.Add(Vector3.Dot(q - hand.position, forward));
                    handLength = Mathf.Clamp(Percentile(along, 0.9f), eyeHeight * 0.04f, eyeHeight * 0.14f);
                }
                a = hand.position;
                b = hand.position + forward * handLength;
                radius = FitRadius(handPoints, a, b, 0f, 1f, 0.85f, eyeHeight * 0.03f, eyeHeight * 0.018f, eyeHeight * 0.05f);
                InsetCaps(ref a, ref b, radius, 0.8f, 1f);
                if(AddSender(holder, "Hand" + suffix, hand, a, b, radius)) senders++;
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
