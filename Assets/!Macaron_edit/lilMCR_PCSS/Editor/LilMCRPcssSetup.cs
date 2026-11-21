#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace lilMCR.PCSS
{
    /// <summary>
    /// Per-avatar setup of lilMCR PCSS (Macaron > lilMCR PCSS > Setup):
    ///  - switches the avatar's Macaron materials to the PCSS variants (same material, same values, Undo),
    ///  - creates the dedicated light "lilMCR_PCSS_Light" under the avatar root.
    /// Remove does the reverse. Macaron's own tools keep working on these materials.
    ///
    /// The light:
    ///  - black (_LightColor0 = 0): the world and other players get no light from it.
    ///  - its cookie carries the lilMCR signature and a per-avatar light ID (1..511, three base-8 digits in the corners);
    ///    the avatar's materials store the same ID, so another lilMCR avatar's light nearby is ignored.
    ///  - culling mask Player / PlayerLocal / MirrorReflection. Default (the avatar in the Unity editor) only with
    ///    "Editor preview", which Apply turns off again and which is never uploaded (LilMCRPcssBuildHook).
    ///  - the shadow is an extra multiply pass: it only darkens what Macaron drew; without the light nothing changes.
    /// </summary>
    public static class LilMCRPcssSetup
    {
        public const string RigName = "lilMCR_PCSS_Light";
        public const string AssetFolder = liltoon_MCR.MacaronCore.ConvertedFolder + "/lilMCR_PCSS_Generated";   // light cookies

        public const string PropEnable = "_lilMCRPCSS_Enable";
        public const string PropReach = "_lilMCRPCSS_Reach";
        public const string PropLightId = "_lilMCRPCSS_LightId";
        public const string PropMaxLevel = "_lilMCRPCSS_MaxLevel";

        //------------------------------------------------------------------------------------------
        // Editor preview button (top of both windows)

        public static void PreviewButton(GameObject avatar)
        {
            if(avatar == null || FindLight(avatar) == null) return;
            bool on = IsEditorPreview(avatar);
            Color bg = GUI.backgroundColor;
            GUI.backgroundColor = on ? liltoon_MCR.MacaronGUI.Mint : liltoon_MCR.MacaronGUI.Lemon;
            string label = on ? PcssText.S("編輯器預覽：開（點擊關閉）") : PcssText.S("編輯器預覽：關（點擊在 Unity 中預覽影子）");
            if(GUILayout.Button(label, GUILayout.Height(30f))) SetEditorPreview(avatar, !on);
            GUI.backgroundColor = bg;
            EditorGUILayout.LabelField(PcssText.S("開啟後光源也會作用在 Default 圖層（Unity 編輯器中的角色）。關閉統一調整視窗或按「套用」時會自動關閉；上傳的角色不會帶有這個設定。"), EditorStyles.wordWrappedMiniLabel);
        }

        // Cookie layout: must match lilmcr_pcss.hlsl (LMP_UV_*).
        private const string CookiePrefix = "lilMCR_PCSS_Cookie_";
        private const int CookieSize = 32;
        private const int MarkerBlock = 4;
        private const byte Signature = 64;            // 0.25
        private const int MaxLightId = 511;           // 3 digits base 8

        // Black light (color black, intensity 1 -> _LightColor0 = 0). Nothing else gets light from it; only this avatar's
        // materials react to it (the multiply shadow pass).
        private const float LightIntensity = 1f;
        private const float MaxLevel = 0.0005f;       // materials accept the light up to this _LightColor0 level
        private const float ReachMargin = 0.75f;      // meters added to the farthest renderer origin (body animation)

        //------------------------------------------------------------------------------------------
        // Avatar helpers

        // Same rule as Macaron's tools, so every window picks the same root for a selection.
        public static Transform FindAvatarRoot(Transform t)
        {
            return liltoon_MCR.MacaronCore.FindAvatarRoot(t);
        }

        public static GameObject SelectedAvatar()
        {
            GameObject go = Selection.activeGameObject;
            if(go == null || EditorUtility.IsPersistent(go)) return null;
            Transform root = FindAvatarRoot(go.transform);
            return root != null ? root.gameObject : null;
        }

        public static List<Renderer> AvatarRenderers(GameObject avatar)
        {
            List<Renderer> list = new List<Renderer>();
            if(avatar == null) return list;
            foreach(Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if(r is SkinnedMeshRenderer || r is MeshRenderer) list.Add(r);
            }
            return list;
        }

        private static Transform ObjectSpaceTransform(Renderer r)
        {
            SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
            return smr != null && smr.rootBone != null ? smr.rootBone : r.transform;
        }

        public static bool IsMacaron(Material m) { return m != null && m.shader != null && LilMCRPcssGenerator.IsSourceName(m.shader.name); }
        public static bool IsPcss(Material m) { return m != null && m.shader != null && LilMCRPcssGenerator.IsPcssName(m.shader.name); }

        /// <summary>Macaron and PCSS materials of the avatar (each once).</summary>
        public static List<Material> AvatarMaterials(GameObject avatar)
        {
            List<Material> list = new List<Material>();
            HashSet<Material> seen = new HashSet<Material>();
            foreach(Renderer r in AvatarRenderers(avatar))
            {
                foreach(Material m in r.sharedMaterials)
                {
                    if((IsMacaron(m) || IsPcss(m)) && seen.Add(m)) list.Add(m);
                }
            }
            return list;
        }

        private static List<Renderer> TargetRenderers(GameObject avatar)
        {
            List<Renderer> list = new List<Renderer>();
            foreach(Renderer r in AvatarRenderers(avatar))
            {
                foreach(Material m in r.sharedMaterials)
                {
                    if(IsMacaron(m) || IsPcss(m)) { list.Add(r); break; }
                }
            }
            return list;
        }

        public static string AvatarFolder(GameObject avatar)
        {
            return AssetFolder + "/" + liltoon_MCR.MacaronCore.SafeFileName(avatar.name);
        }

        //------------------------------------------------------------------------------------------
        // Direction of a new light (avatar space, x = right seen from the front, y = up, z = front). Change it in
        // Batch Settings. A little to the viewer's left, about 22 degrees up.

        private static readonly Vector3 DefaultDirectionVector = new Vector3(-0.1343283f, 0.3731343f, 0.9180015f);

        public static Vector3 DefaultDirection() { return DefaultDirectionVector.normalized; }

        /// <summary>Default direction of the light, used when a new light is created (Setup, conversion) and by the
        /// direction pad's "恢復預設方向". Always the add-on's own default: the PCSS light does not follow the contact
        /// shadow direction chosen in the Converter.</summary>
        public static Vector3 ConvertedDirection(GameObject avatar)
        {
            return DefaultDirection();
        }

        //------------------------------------------------------------------------------------------
        // Apply / remove

        public struct Result
        {
            public int Materials;
            public int LightId;
            public List<string> Missing;
            /// <summary>Other avatars / objects in the open scenes that use the same materials (they change too).</summary>
            public List<string> SharedWith;
        }

        /// <summary>Turns lilMCR PCSS on for the avatar.</summary>
        public static Result Apply(GameObject avatar, bool? selfOnly = null)
        {
            bool sharedSelfOnly = selfOnly ?? LilMCRPcssSharedShadow.Get(avatar);
            Result res = new Result { Missing = new List<string>(), SharedWith = SharedUsers(avatar) };
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("lilMCR PCSS Apply");
            int group = Undo.GetCurrentGroup();

            Vector3 direction;
            // An existing light keeps its direction (set in Batch Settings); a new one starts from the default direction.
            if(!CurrentDirection(avatar, out direction)) direction = ConvertedDirection(avatar);
            int lightId;
            float reach = EnsureRig(avatar, direction, false, out lightId);
            res.LightId = lightId;
            // Shadow map resolution of the quality tier the avatar was converted with (or set to in Batch Settings).
            int avatarTier = liltoon_MCR.MacaronCore.AppliedTier(avatar);
            if(avatarTier >= 0 && GetShadowResolution(avatar) != TierShadowResolution(avatarTier))
                SetShadowResolution(avatar, TierShadowResolution(avatarTier));
            Vector4? signature = ProjectionSignature(FindLight(avatar), avatar);

            foreach(Material m in AvatarMaterials(avatar))
            {
                Undo.RecordObject(m, "lilMCR PCSS Apply");
                if(IsMacaron(m))
                {
                    string target = LilMCRPcssGenerator.ToPcssName(m.shader.name);
                    Shader s = Shader.Find(target);
                    if(s == null) { if(!res.Missing.Contains(target)) res.Missing.Add(target); continue; }
                    SwapShader(m, s);
                    // Initialize only a conversion into PCSS, not an update of an existing PCSS material.
                    // Explicit assignment also clears stale saved values from earlier shader swaps.
                    if(m.HasProperty("_lilMCRPCSS_MinBlur")) m.SetFloat("_lilMCRPCSS_MinBlur", 3f);
                    if(m.HasProperty("_lilMCRPCSS_ShadowSaturation")) m.SetFloat("_lilMCRPCSS_ShadowSaturation", 0.1f);
                    // PCSS values of the material's quality tier (they could not be written before the swap).
                    int tier = liltoon_MCR.MacaronCore.AppliedTier(m);
                    if(tier >= 0) liltoon_MCR.MacaronCore.ApplyTierValues(m, tier, "_lilMCRPCSS_");
                }
                m.SetFloat(PropEnable, 1f);
                m.SetFloat(PropReach, reach);
                SetProjectionSignature(m, signature);
                m.SetFloat(PropLightId, lightId);
                if(m.HasProperty(PropMaxLevel)) m.SetFloat(PropMaxLevel, MaxLevel);
                EditorUtility.SetDirty(m);
                res.Materials++;
            }

            LilMCRPcssSharedShadow.Set(LilMCRPcssSharedShadow.Materials(avatar), sharedSelfOnly);
            Undo.CollapseUndoOperations(group);
            AssetDatabase.SaveAssets();
            return res;
        }

        // Names of renderers outside the avatar (open scenes) that share one of its Macaron / PCSS materials.
        private static List<string> SharedUsers(GameObject avatar)
        {
            List<string> names = new List<string>();
            HashSet<Material> mine = new HashSet<Material>(AvatarMaterials(avatar));
            if(mine.Count == 0) return names;
            foreach(Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>(true))
            {
                if(r == null || r.transform.IsChildOf(avatar.transform) || EditorUtility.IsPersistent(r)) continue;
                foreach(Material m in r.sharedMaterials)
                {
                    if(m == null || !mine.Contains(m)) continue;
                    Transform owner = FindAvatarRoot(r.transform);
                    string name = (owner != null ? owner : r.transform.root).name;
                    if(!names.Contains(name)) names.Add(name);
                    break;
                }
            }
            return names;
        }

        /// <summary>
        /// Takes lilMCR PCSS off the avatar: materials back to Macaron and the light removed (Undo).
        /// deleteFiles also deletes this light's cookie (no Undo) when nothing else still uses it. Returns the material count.
        /// </summary>
        public static int Remove(GameObject avatar, bool deleteFiles)
        {
            Light light = FindLight(avatar);
            string cookiePath = light != null && light.cookie != null && IdFromCookie(light.cookie) > 0 ? AssetDatabase.GetAssetPath(light.cookie) : "";
            int n = Remove(avatar);
            if(deleteFiles)
            {
                if(!string.IsNullOrEmpty(cookiePath) && cookiePath.StartsWith(AssetFolder + "/", StringComparison.Ordinal) && !CookieInUse(cookiePath))
                {
                    string folder = Path.GetDirectoryName(cookiePath).Replace('\\', '/');
                    AssetDatabase.DeleteAsset(cookiePath);
                    if(AssetDatabase.IsValidFolder(folder) && AssetDatabase.FindAssets("", new[] { folder }).Length == 0)
                        AssetDatabase.DeleteAsset(folder);
                }
                if(AssetDatabase.IsValidFolder(AssetFolder) && AssetDatabase.FindAssets("", new[] { AssetFolder }).Length == 0)
                    AssetDatabase.DeleteAsset(AssetFolder);
                ForgetCoverage(avatar);
                AssetDatabase.Refresh();
            }
            return n;
        }

        // Another light in the open scenes, or a saved scene / prefab, still references the cookie: keep it.
        private static bool CookieInUse(string cookiePath)
        {
            foreach(Light l in UnityEngine.Object.FindObjectsOfType<Light>(true))
            {
                if(l != null && l.cookie != null && AssetDatabase.GetAssetPath(l.cookie) == cookiePath) return true;
            }
            HashSet<string> open = new HashSet<string>();
            for(int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                open.Add(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path);
            foreach(string guid in AssetDatabase.FindAssets("t:Scene t:Prefab", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if(open.Contains(path)) continue;   // open scenes were checked above (unsaved state)
                foreach(string dep in AssetDatabase.GetDependencies(path, false))
                    if(dep == cookiePath) return true;
            }
            return false;
        }

        public static int Remove(GameObject avatar)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("lilMCR PCSS Remove");
            int group = Undo.GetCurrentGroup();
            int n = 0;
            foreach(Material m in AvatarMaterials(avatar))
            {
                if(!IsPcss(m)) continue;
                Shader s = Shader.Find(LilMCRPcssGenerator.ToSourceName(m.shader.name));
                if(s == null) continue;
                Undo.RecordObject(m, "lilMCR PCSS Remove");
                SwapShader(m, s);
                EditorUtility.SetDirty(m);
                n++;
            }
            Transform rig = FindChild(avatar, RigName);
            if(rig != null) Undo.DestroyObjectImmediate(rig.gameObject);
            Undo.CollapseUndoOperations(group);
            AssetDatabase.SaveAssets();
            return n;
        }

        // Shader swap that keeps every value (same property names) and the render queue.
        private static void SwapShader(Material m, Shader s)
        {
            int queue = m.renderQueue;
            bool custom = queue != m.shader.renderQueue;
            m.shader = s;
            if(custom) m.renderQueue = queue;
        }

        private static Transform FindChild(GameObject avatar, string name)
        {
            if(avatar == null) return null;
            for(int i = 0; i < avatar.transform.childCount; i++)
            {
                Transform c = avatar.transform.GetChild(i);
                if(c.name == name) return c;
            }
            return null;
        }

        public static Light FindLight(GameObject avatar)
        {
            Transform rig = FindChild(avatar, RigName);
            return rig != null ? rig.GetComponentInChildren<Light>(true) : null;
        }

        //------------------------------------------------------------------------------------------
        // Rig

        /// <summary>
        /// Creates or updates the light for a direction in avatar space. Returns the reach (meters) the materials need,
        /// and the light ID.
        /// </summary>
        public static float EnsureRig(GameObject avatar, Vector3 direction, bool keepPreview, out int lightId)
        {
            Transform root = avatar.transform;
            Bounds bounds = AvatarBounds(avatar);
            Vector3 centerWS = AvatarCenter(avatar, bounds);
            // Tight radius (the cone decides how many shadow-map texels land on the avatar), times the user's coverage.
            float radius = AvatarRadius(avatar, centerWS, bounds) * GetCoverage(avatar);

            Vector3 d = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            // avatar space -> world: x = right seen from the front (= the avatar's left), y = up, z = the avatar's front
            Vector3 dirWS = (-root.right * d.x + root.up * d.y + root.forward * d.z).normalized;

            float distance = Mathf.Max(1.2f, radius * 2.2f);
            Vector3 lightWS = centerWS + dirWS * distance;
            // Cover the radius plus the soft cone edge, nothing more.
            float spotAngle = Mathf.Clamp(2f * Mathf.Atan(radius * 1.08f / distance) * Mathf.Rad2Deg, 10f, 120f);

            Transform rig = FindChild(avatar, RigName);
            Light light;
            if(rig == null)
            {
                GameObject holder = new GameObject(RigName);
                Undo.RegisterCreatedObjectUndo(holder, "lilMCR PCSS Add Light");
                holder.transform.SetParent(root, false);
                GameObject lightObject = new GameObject("Key Light");
                lightObject.transform.SetParent(holder.transform, false);
                light = lightObject.AddComponent<Light>();
                rig = holder.transform;
            }
            else
            {
                light = rig.GetComponentInChildren<Light>(true);
                if(light == null)
                {
                    GameObject lightObject = new GameObject("Key Light");
                    Undo.RegisterCreatedObjectUndo(lightObject, "lilMCR PCSS Add Light");
                    lightObject.transform.SetParent(rig, false);
                    light = lightObject.AddComponent<Light>();
                }
                Undo.RecordObject(rig, "lilMCR PCSS Update Light");
                Undo.RecordObject(light.transform, "lilMCR PCSS Update Light");
                Undo.RecordObject(light, "lilMCR PCSS Update Light");
                Undo.RecordObject(light.gameObject, "lilMCR PCSS Update Light");
            }

            // Keep the light ID of an existing light (materials set up earlier keep matching); otherwise pick a new one.
            lightId = IdFromCookie(light.cookie);
            if(lightId <= 0) lightId = UnityEngine.Random.Range(1, MaxLightId + 1);

            Vector3 up = Mathf.Abs(Vector3.Dot(dirWS, root.up)) > 0.98f ? root.forward : root.up;
            rig.localPosition = Vector3.zero;
            rig.localRotation = Quaternion.identity;
            rig.localScale = Vector3.one;
            light.gameObject.SetActive(true);
            light.transform.position = lightWS;
            light.transform.rotation = Quaternion.LookRotation(centerWS - lightWS, up);
            light.transform.localScale = Vector3.one;

            light.enabled = true;
            light.type = LightType.Spot;
            light.color = Color.black;
            light.intensity = LightIntensity;
            light.range = distance + radius * 1.1f + 0.2f;      // just past the far side of the avatar
            // Encode the cookie ID in a sub-degree part of the cone angle.
            // Round upward so the original coverage is never reduced.
            light.spotAngle = Mathf.Ceil(spotAngle) + lightId / 1024f;
            light.cookie = EnsureCookie(AvatarFolder(avatar), lightId);
            light.shadows = LightShadows.Hard;                  // filtered by lilMCR PCSS
            light.shadowStrength = 1f;
            light.shadowResolution = UnityEngine.Rendering.LightShadowResolution.VeryHigh;
            if(light.shadowCustomResolution <= 0) light.shadowCustomResolution = DefaultShadowResolution;
            light.shadowBias = 0f;
            light.shadowNormalBias = 0f;
            // Keep the reserved self-depth band close to the emitter rather than near the avatar.
            // A distant near plane lets nearby foreign hair/props enter that band.
            light.shadowNearPlane = 0.01f;
            light.renderMode = LightRenderMode.ForcePixel;   // must be a pixel light (ForwardAdd)
            light.lightmapBakeType = LightmapBakeType.Realtime;
            bool preview = keepPreview && (light.cullingMask & DefaultLayerBit) != 0;
            light.cullingMask = RuntimeLayers() | (preview ? DefaultLayerBit : 0);   // Apply turns the editor preview off
            EditorUtility.SetDirty(light);
            EditorUtility.SetDirty(light.transform);
            EditorUtility.SetDirty(rig);

            // Reach: farthest object-space origin of a renderer (what the shader compares against) plus a margin.
            float reach = 0f;
            foreach(Renderer r in TargetRenderers(avatar))
            {
                reach = Mathf.Max(reach, Vector3.Distance(ObjectSpaceTransform(r).position, lightWS));
            }
            if(reach <= 0f) reach = distance + radius;
            return reach + ReachMargin;
        }

        // Computed once per avatar (it walks the renderers) and written to every material.
        private static Vector4? ProjectionSignature(Light light, GameObject avatar)
        {
            if(light == null) return null;
            float halfAngle = light.spotAngle * Mathf.Deg2Rad * 0.5f;
            float cotangent = 1f / Mathf.Tan(halfAngle);
            // Less than half an ID step; compare matrix values without per-vertex atan.
            float tolerance = (1f + cotangent * cotangent) * Mathf.Deg2Rad * (0.35f / 1024f) * 0.5f;
            Bounds bounds = AvatarBounds(avatar);
            Vector3 center = AvatarCenter(avatar, bounds);
            float radius = AvatarRadius(avatar, center, bounds) * GetCoverage(avatar);
            float originalNear = Mathf.Max(0.1f, Vector3.Distance(light.transform.position, center) - radius * 1.5f);
            // Extend the original caster interval toward the light for pose margin.
            float sourceNear = originalNear * 0.5f;
            return new Vector4(cotangent, tolerance, sourceNear / light.range, 0f);
        }

        private static void SetProjectionSignature(Material material, Vector4? signature)
        {
            if(signature.HasValue && material.HasProperty("_lilMCRPCSS_Projection")) material.SetVector("_lilMCRPCSS_Projection", signature.Value);
        }

        // VRChat avatar layers. Other players standing inside the light's narrow cone still draw one extra (black, so
        // invisible) pass and cast into the shadow map; the cone and range are kept as small as the avatar allows.
        private static int RuntimeLayers()
        {
            return (1 << LayerOr("Player", 9)) | (1 << LayerOr("PlayerLocal", 10)) | (1 << LayerOr("MirrorReflection", 18));
        }

        private const int DefaultLayerBit = 1 << 0;

        public static bool IsEditorPreview(GameObject avatar)
        {
            Light l = FindLight(avatar);
            return l != null && (l.cullingMask & DefaultLayerBit) != 0;
        }

        public static void SetEditorPreview(GameObject avatar, bool on)
        {
            Light l = FindLight(avatar);
            if(l == null) return;
            Undo.RecordObject(l, "lilMCR PCSS Editor Preview");
            l.cullingMask = on ? (l.cullingMask | DefaultLayerBit) : (l.cullingMask & ~DefaultLayerBit);
            EditorUtility.SetDirty(l);
        }

        /// <summary>For the avatar that is being built (a copy): the light keeps the runtime layers only, without Undo.
        /// Every lilMCR PCSS light under the avatar is covered. Returns true when the preview was on.</summary>
        public static bool ClearEditorPreviewForBuild(GameObject avatar)
        {
            if(avatar == null) return false;
            bool cleared = false;
            foreach(Light l in avatar.GetComponentsInChildren<Light>(true))
            {
                if((l.cullingMask & DefaultLayerBit) == 0) continue;
                Transform t = l.transform;
                // By the cookie (the light may have been moved or renamed by a build tool), or by the rig's name.
                bool ours = l.cookie != null && l.cookie.name.StartsWith(CookiePrefix, StringComparison.Ordinal);
                for(; !ours && t != null && t != avatar.transform.parent; t = t.parent)
                    if(t.name == RigName) { ours = true; break; }
                if(!ours) continue;
                l.cullingMask &= ~DefaultLayerBit;
                cleared = true;
            }
            return cleared;
        }

        private static int LayerOr(string name, int fallback)
        {
            int layer = LayerMask.NameToLayer(name);
            return layer >= 0 ? layer : fallback;
        }

        //------------------------------------------------------------------------------------------
        // Direction and shadow map resolution (Batch Settings)
        //   Avatar space as Macaron: x = right seen from the front (= the avatar's left), y = up, z = the avatar's front.

        public const int DefaultShadowResolution = 4096;
        public static readonly int[] ShadowResolutions = { 1024, 2048, 4096, 8192 };

        /// <summary>Direction (avatar space) from the avatar center to the existing light. false when there is no light.</summary>
        public static bool CurrentDirection(GameObject avatar, out Vector3 direction)
        {
            direction = Vector3.forward;
            Light l = FindLight(avatar);
            if(l == null) return false;
            Transform root = avatar.transform;
            Vector3 w = l.transform.position - AvatarCenter(avatar, AvatarBounds(avatar));
            if(w.sqrMagnitude < 1e-8f) return false;
            w.Normalize();
            direction = new Vector3(Vector3.Dot(w, -root.right), Vector3.Dot(w, root.up), Vector3.Dot(w, root.forward));
            return true;
        }

        /// <summary>Moves the light to the direction (avatar space) and updates the materials' reach. Keeps the editor preview.</summary>
        public static void SetDirection(GameObject avatar, Vector3 direction)
        {
            if(FindLight(avatar) == null) return;
            int id;
            float reach = EnsureRig(avatar, direction, true, out id);
            Vector4? signature = ProjectionSignature(FindLight(avatar), avatar);
            foreach(Material m in AvatarMaterials(avatar))
            {
                if(!IsPcss(m) || !m.HasProperty(PropReach)) continue;
                Undo.RecordObject(m, "lilMCR PCSS Direction");
                m.SetFloat(PropReach, reach);
                SetProjectionSignature(m, signature);
                EditorUtility.SetDirty(m);
            }
        }

        public static int GetShadowResolution(GameObject avatar)
        {
            Light l = FindLight(avatar);
            return l != null && l.shadowCustomResolution > 0 ? l.shadowCustomResolution : DefaultShadowResolution;
        }

        /// <summary>Shadow map resolution of a Macaron quality tier: Lightest 1K, Light 2K, Standard / High the default (4K), Overkill 8K.</summary>
        public static int TierShadowResolution(int tier)
        {
            if(tier == liltoon_MCR.MacaronCore.TierLightest) return 1024;
            if(tier == liltoon_MCR.MacaronCore.TierLight) return 2048;
            if(tier == liltoon_MCR.MacaronCore.TierOverkill) return 8192;
            return DefaultShadowResolution;
        }

        // Macaron's tier buttons (Converter, Batch Settings, this add-on's Batch) also set the light's resolution.
        [InitializeOnLoadMethod]
        private static void RegisterTierHook()
        {
            liltoon_MCR.MacaronCore.TierAppliedToAvatar -= OnTierApplied;
            liltoon_MCR.MacaronCore.TierAppliedToAvatar += OnTierApplied;
        }

        private static void OnTierApplied(GameObject avatar, int tier)
        {
            if(FindLight(avatar) == null) return;
            int resolution = TierShadowResolution(tier);
            if(GetShadowResolution(avatar) != resolution) SetShadowResolution(avatar, resolution);
        }

        public static void SetShadowResolution(GameObject avatar, int resolution)
        {
            Light l = FindLight(avatar);
            if(l == null) return;
            Undo.RecordObject(l, "lilMCR PCSS Shadow Resolution");
            l.shadowCustomResolution = resolution;
            EditorUtility.SetDirty(l);
        }

        // Humanoid: farthest main bone from the center + a margin for the head top / hair; otherwise the Macaron
        // renderers' bounds. (Renderer bounds of skinned meshes are often far larger than the body.)
        private static readonly HumanBodyBones[] RadiusBones =
        {
            HumanBodyBones.Head, HumanBodyBones.Hips, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
        };
        private const float BoneMargin = 0.22f;

        private static float AvatarRadius(GameObject avatar, Vector3 center, Bounds bounds)
        {
            Animator animator = avatar.GetComponent<Animator>();
            if(animator != null && animator.isHuman)
            {
                float r = 0f;
                foreach(HumanBodyBones b in RadiusBones)
                {
                    Transform t = animator.GetBoneTransform(b);
                    if(t != null) r = Mathf.Max(r, Vector3.Distance(center, t.position));
                }
                if(r > 0.1f) return r + BoneMargin;
            }
            return Mathf.Max(bounds.extents.magnitude * 0.85f, 0.3f);
        }

        // Coverage: how much larger than the computed radius the light covers (Batch Settings), per avatar.
        public const float DefaultCoverage = 0.5f;
        // Keyed by the avatar's object ID (stable across scene reopen and unique per avatar). Unsaved scenes have no
        // ID yet and use the name, as earlier versions did; that value is also the fallback when reading.
        private static readonly Dictionary<int, string> coverageKeys = new Dictionary<int, string>();
        private static string LegacyCoverageKey(GameObject avatar) { return "lilMCR.PCSS.Coverage." + avatar.name; }
        private static string CoverageKey(GameObject avatar)
        {
            string key;
            if(coverageKeys.TryGetValue(avatar.GetInstanceID(), out key)) return key;
            GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(avatar);
            key = id.identifierType != 0 && !id.assetGUID.Empty() ? "lilMCR.PCSS.Coverage." + id.ToString() : LegacyCoverageKey(avatar);
            if(key != LegacyCoverageKey(avatar)) coverageKeys[avatar.GetInstanceID()] = key;
            return key;
        }
        public static float GetCoverage(GameObject avatar)
        {
            float v;
            string key = CoverageKey(avatar);
            if(LilMCRPcssCoverageStore.Instance.TryGet(key, out v)) return Mathf.Clamp(v, 0.5f, 2f);
            if(EditorPrefs.HasKey(key)) return Mathf.Clamp(EditorPrefs.GetFloat(key, DefaultCoverage), 0.5f, 2f);
            return Mathf.Clamp(EditorPrefs.GetFloat(LegacyCoverageKey(avatar), DefaultCoverage), 0.5f, 2f);
        }

        // Saved in EditorPrefs; an in-memory copy records Undo so Ctrl+Z restores it together with the light.
        public static void SetCoverage(GameObject avatar, float k)
        {
            string key = CoverageKey(avatar);
            LilMCRPcssCoverageStore store = LilMCRPcssCoverageStore.Instance;
            float current = GetCoverage(avatar);
            store.Set(key, current);
            Undo.RecordObject(store, "lilMCR PCSS Coverage");
            k = Mathf.Clamp(k, 0.5f, 2f);
            store.Set(key, k);
            EditorPrefs.SetFloat(key, k);
        }

        /// <summary>Writes the coverage values restored by Undo / Redo back to EditorPrefs.</summary>
        public static void SaveCoverageAfterUndo()
        {
            LilMCRPcssCoverageStore store = LilMCRPcssCoverageStore.Instance;
            for(int i = 0; i < store.Keys.Count; i++) EditorPrefs.SetFloat(store.Keys[i], store.Values[i]);
        }

        private static void ForgetCoverage(GameObject avatar)
        {
            string key = CoverageKey(avatar);
            LilMCRPcssCoverageStore.Instance.Remove(key);
            EditorPrefs.DeleteKey(key);
            EditorPrefs.DeleteKey(LegacyCoverageKey(avatar));
        }

        /// <summary>Approximate size of one shadow-map texel on the avatar (millimeters).</summary>
        public static float TexelSizeMM(GameObject avatar)
        {
            Light l = FindLight(avatar);
            if(l == null) return 0f;
            float d = Vector3.Distance(l.transform.position, AvatarCenter(avatar, AvatarBounds(avatar)));
            float width = 2f * d * Mathf.Tan(l.spotAngle * 0.5f * Mathf.Deg2Rad);
            return width / Mathf.Max(GetShadowResolution(avatar), 1) * 1000f;
        }

        private static Bounds AvatarBounds(GameObject avatar)
        {
            bool has = false;
            Bounds b = new Bounds(avatar.transform.position + Vector3.up * 0.8f, Vector3.one);
            List<Renderer> rs = TargetRenderers(avatar);
            if(rs.Count == 0) rs = AvatarRenderers(avatar);
            foreach(Renderer r in rs)
            {
                if(!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        // Chest (humanoid) or the bounds center.
        private static Vector3 AvatarCenter(GameObject avatar, Bounds bounds)
        {
            Animator animator = avatar.GetComponent<Animator>();
            if(animator != null && animator.isHuman)
            {
                Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                if(chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Spine);
                if(chest != null) return chest.position;
            }
            return bounds.center;
        }

        //------------------------------------------------------------------------------------------
        // Cookie (signature + light ID): must match Templates/lilmcr_pcss.hlsl

        private static int IdFromCookie(Texture cookie)
        {
            if(cookie == null || !cookie.name.StartsWith(CookiePrefix, StringComparison.Ordinal)) return 0;
            int id;
            return int.TryParse(cookie.name.Substring(CookiePrefix.Length), out id) && id >= 1 && id <= MaxLightId ? id : 0;
        }

        private static byte DigitGray(int digit)
        {
            return (byte)Mathf.RoundToInt(255f * (digit + 0.5f) / 8f);
        }

        public static Texture2D EnsureCookie(string folder, int lightId)
        {
            liltoon_MCR.MacaronCore.EnsureFolder(folder);
            string path = folder + "/" + CookiePrefix + lightId + ".png";
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if(existing != null) return existing;

            byte d2 = DigitGray((lightId >> 6) & 7);
            byte d1 = DigitGray((lightId >> 3) & 7);
            byte d0 = DigitGray(lightId & 7);

            Color32[] px = new Color32[CookieSize * CookieSize];
            float c = (CookieSize - 1) * 0.5f;
            int hi = CookieSize - MarkerBlock;
            for(int y = 0; y < CookieSize; y++)
            {
                for(int x = 0; x < CookieSize; x++)
                {
                    byte g;
                    bool left = x < MarkerBlock, right = x >= hi, bottom = y < MarkerBlock, top = y >= hi;
                    if(bottom && left) g = Signature;
                    else if(bottom && right) g = d2;
                    else if(top && left) g = d1;
                    else if(top && right) g = d0;
                    else
                    {
                        float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / (CookieSize * 0.5f);
                        float t = Mathf.InverseLerp(0.75f, 1f, r);
                        g = (byte)Mathf.RoundToInt(255f * (1f - t * t * (3f - 2f * t)));
                    }
                    px[y * CookieSize + x] = new Color32(g, g, g, 255);
                }
            }
            Texture2D tex = new Texture2D(CookieSize, CookieSize, TextureFormat.RGBA32, false, true);
            try
            {
                tex.SetPixels32(px);
                tex.Apply(false);
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if(ti != null)
            {
                ti.textureType = TextureImporterType.Cookie;
                ti.alphaSource = TextureImporterAlphaSource.FromGrayScale;
                ti.sRGBTexture = false;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
#endif
