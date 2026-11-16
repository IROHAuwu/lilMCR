// lilMCR Param Compressor - build pass.
// Ported from ND Parameter Compressor (https://github.com/sakuraimikoto33/ParamCompressor, MIT, (c) 2025 sakuraimikoto33),
// which is based on Laura's Param Compressor (MIT, (c) 2025 Laura). See THIRD_PARTY_NOTICES.txt.
//
// Differences from the original:
//  - Runs only when the merged avatar is over VRChat's synced parameter limit; otherwise nothing is changed.
//  - Picks the fewest sync steps (and the smallest set: Bools only / numbers only / both) that fits within the limit.
//  - Never writes to the user's assets: the Expression Parameters are cloned, and an FX controller that is still the
//    original asset is copied into NDMF's temporary folder before a layer is added.
//  - No parameter list to maintain: everything synced is a candidate except VRChat / face tracking parameters and the
//    filters of the component.
using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

[assembly: ExportsPlugin(typeof(liltoon_MCR.MacaronParamCompressorPlugin))]

namespace liltoon_MCR
{
    public class MacaronParamCompressorPlugin : Plugin<MacaronParamCompressorPlugin>
    {
        public override string QualifiedName => "liltoon_MCR.param-compressor";
        public override string DisplayName => "lilMCR Param Compressor";

        protected override void Configure()
        {
            // After Modular Avatar / Avatar Optimizer have finished, so the merged parameters are final.
            InPhase(BuildPhase.Optimizing)
                .AfterPlugin("nadena.dev.modular-avatar")
                .AfterPlugin("com.anatawa12.avatar-optimizer")
                .Run("lilMCR Param Compressor", ctx => MacaronParamCompressorPass.Execute(ctx));
        }
    }

    public static class MacaronParamCompressorPass
    {
        public const string LogPrefix = "[lilMCR Param Compressor]";

        // Parameter names of the sync layer (kept out of the candidates).
        private const string Root = "lilMCR/ParamSync/";
        private const string IsLocalName = "IsLocal";
        private const string PointerName = Root + "Ptr";
        private const string TrueName = Root + "True";
        private const string DataNumName = Root + "Num";
        private const string DataBoolName = Root + "Bool";

        private const int MaxNumbersPerState = 8;
        private const int MaxBoolsPerState = 64;
        private const float StateExitTime = 0.1f;

        private static readonly HashSet<string> VRChatParams = new HashSet<string>
        {
            IsLocalName, "PreviewMode", "Viseme", "Voice", "GestureLeft", "GestureRight", "GestureLeftWeight",
            "GestureRightWeight", "AngularY", "VelocityX", "VelocityY", "VelocityZ", "VelocityMagnitude",
            "Upright", "Grounded", "Seated", "AFK", "TrackingType", "VRMode", "MuteSelf", "InStation",
            "Earmuffs", "IsOnFriendsList", "AvatarVersion", "IsAnimatorEnabled", "ScaleModified", "ScaleFactor",
            "ScaleFactorInverse", "EyeHeightAsMeters", "EyeHeightAsPercent", "VRCEmote", "VRCFaceBlendH", "VRCFaceBlendV"
        };

        private static readonly HashSet<string> VRCFTv4Params = new HashSet<string>
        {
            "EyesX", "EyesY", "LeftEyeLid", "RightEyeLid", "CombinedEyeLid", "EyesWiden", "EyesDilation", "EyesPupilDiameter",
            "EyesSqueeze", "LeftEyeX", "LeftEyeY", "RightEyeX", "RightEyeY", "LeftEyeWiden", "RightEyeWiden", "LeftEyeSqueeze",
            "RightEyeSqueeze", "LeftEyeLidExpanded", "RightEyeLidExpanded", "CombinedEyeLidExpanded", "LeftEyeLidExpandedSqueeze",
            "RightEyeLidExpandedSqueeze", "CombinedEyeLidExpandedSqueeze", "JawRight", "JawLeft", "JawForward", "JawOpen",
            "MouthApeShape", "MouthUpperRight", "MouthUpperLeft", "MouthLowerRight", "MouthLowerLeft", "MouthUpperOverturn",
            "MouthLowerOverturn", "MouthPout", "MouthSmileRight", "MouthSmileLeft", "MouthSadRight", "MouthSadLeft",
            "CheekPuffRight", "CheekPuffLeft", "CheekSuck", "MouthUpperUpRight", "MouthUpperUpLeft", "MouthLowerDownRight",
            "MouthLowerDownLeft", "MouthUpperInside", "MouthLowerInside", "MouthLowerOverlay", "TongueLongStep1", "TongueLongStep2",
            "TongueDown", "TongueUp", "TongueRight", "TongueLeft", "TongueRoll", "TongueUpLeftMorph", "TongueUpRightMorph",
            "TongueDownLeftMorph", "TongueDownRightMorph", "JawX", "MouthUpper", "MouthLower", "MouthX", "MouthUpperInsideOverturn",
            "MouthLowerInsideOverturn", "SmileSadRight", "SmileSadLeft", "SmileSad", "TongueY", "TongueX", "TongueSteps",
            "PuffSuckRight", "PuffSuckLeft", "PuffSuck", "JawOpenApe", "JawOpenPuff", "JawOpenPuffRight", "JawOpenPuffLeft",
            "JawOpenSuck", "JawOpenForward", "JawOpenOverlay", "MouthUpperUpRightUpperInside", "MouthUpperUpRightPuffRight",
            "MouthUpperUpRightApe", "MouthUpperUpRightPout", "MouthUpperUpRightOverlay", "MouthUpperUpRightSuck",
            "MouthUpperUpLeftUpperInside", "MouthUpperUpLeftPuffLeft", "MouthUpperUpLeftApe", "MouthUpperUpLeftPout",
            "MouthUpperUpLeftOverlay", "MouthUpperUpLeftSuck", "MouthUpperUpUpperInside", "MouthUpperUpInside", "MouthUpperUpPuff",
            "MouthUpperUpPuffLeft", "MouthUpperUpPuffRight", "MouthUpperUpApe", "MouthUpperUpPout", "MouthUpperUpOverlay",
            "MouthUpperUpSuck", "MouthLowerDownRightLowerInside", "MouthLowerDownRightPuffRight", "MouthLowerDownRightApe",
            "MouthLowerDownRightPout", "MouthLowerDownRightOverlay", "MouthLowerDownRightSuck", "MouthLowerDownLeftLowerInside",
            "MouthLowerDownLeftPuffLeft", "MouthLowerDownLeftApe", "MouthLowerDownLeftPout", "MouthLowerDownLeftOverlay",
            "MouthLowerDownLeftSuck", "MouthLowerDownLowerInside", "MouthLowerDownInside", "MouthLowerDownPuff",
            "MouthLowerDownPuffLeft", "MouthLowerDownPuffRight", "MouthLowerDownApe", "MouthLowerDownPout", "MouthLowerDownOverlay",
            "MouthLowerDownSuck", "SmileRightUpperOverturn", "SmileRightLowerOverturn", "SmileRightOverturn", "SmileRightApe",
            "SmileRightOverlay", "SmileRightPout", "SmileLeftUpperOverturn", "SmileLeftLowerOverturn", "SmileLeftOverturn",
            "SmileLeftApe", "SmileLeftOverlay", "SmileLeftPout", "SmileUpperOverturn", "SmileLowerOverturn", "SmileApe", "SmileOverlay",
            "SmilePout", "PuffRightUpperOverturn", "PuffRightLowerOverturn", "PuffRightOverturn", "PuffLeftUpperOverturn",
            "PuffLeftLowerOverturn", "PuffLeftOverturn", "PuffUpperOverturn", "PuffLowerOverturn", "PuffOverturn"
        };

        private static readonly string[] VRCFTv5Prefixes = { "v2/", "FT/v2/" };

        //------------------------------------------------------------------------------------------
        // Plan

        internal sealed class Plan
        {
            public List<VRCExpressionParameters.Parameter> Numbers = new List<VRCExpressionParameters.Parameter>();
            public List<VRCExpressionParameters.Parameter> Bools = new List<VRCExpressionParameters.Parameter>();
            public int NumbersPerState, BoolsPerState, StateCount, PointerBits, Added, Removed;
            public int Saved { get { return Removed - Added; } }
        }

        public static int PointerBitsFor(int stateCount)
        {
            if(stateCount <= 1) return 1;
            int bits = 0;
            for(int v = stateCount - 1; v > 0; v >>= 1) bits++;
            return bits;
        }

        private static Plan MakePlan(List<VRCExpressionParameters.Parameter> numbers, List<VRCExpressionParameters.Parameter> bools, int steps)
        {
            var plan = new Plan();
            int nps = Mathf.Clamp(Mathf.CeilToInt((float)numbers.Count / steps), 1, MaxNumbersPerState);
            int bps = Mathf.Clamp(Mathf.CeilToInt((float)bools.Count / steps), 1, MaxBoolsPerState);
            // Sharing one slot with a single value saves nothing.
            if(numbers.Count > nps) { plan.Numbers.AddRange(numbers); plan.NumbersPerState = nps; }
            if(bools.Count > bps) { plan.Bools.AddRange(bools); plan.BoolsPerState = bps; }
            plan.StateCount = Math.Max(
                plan.Numbers.Count > 0 ? Mathf.CeilToInt((float)plan.Numbers.Count / nps) : 0,
                plan.Bools.Count > 0 ? Mathf.CeilToInt((float)plan.Bools.Count / bps) : 0);
            if(plan.StateCount == 0) return plan;
            plan.PointerBits = PointerBitsFor(plan.StateCount);
            plan.Added = plan.PointerBits + 8 * plan.NumbersPerState + plan.BoolsPerState;
            plan.Removed = 8 * plan.Numbers.Count + plan.Bools.Count;
            return plan;
        }

        // Fewest steps first (least delay), and within one step count the smallest change (Bools only, numbers only, both).
        internal static Plan ChoosePlan(List<VRCExpressionParameters.Parameter> numbers, List<VRCExpressionParameters.Parameter> bools,
            int cost, int limit, int maxSteps)
        {
            var none = new List<VRCExpressionParameters.Parameter>();
            Plan best = null;
            for(int steps = 2; steps <= maxSteps; steps++)
            {
                Plan[] variants = { MakePlan(none, bools, steps), MakePlan(numbers, none, steps), MakePlan(numbers, bools, steps) };
                foreach(Plan p in variants)
                {
                    // Per-state caps (MaxNumbersPerState / MaxBoolsPerState) can need more steps than allowed.
                    if(p.StateCount == 0 || p.StateCount > maxSteps || p.Saved <= 0) continue;
                    if(cost - p.Saved <= limit) return p;
                    if(best == null || p.Saved > best.Saved) best = p;
                }
            }
            return best;       // does not fit: the most that can be saved (the SDK will still report the error)
        }

        //------------------------------------------------------------------------------------------

        public static void Execute(BuildContext ctx)
        {
            MacaronParamCompressor settings = ctx.AvatarRootObject.GetComponentInChildren<MacaronParamCompressor>(true);
            if(settings == null) return;
            try
            {
                Run(ctx, settings);
            }
            finally
            {
                RemoveSettings(settings);
            }
        }

        private static void Run(BuildContext ctx, MacaronParamCompressor settings)
        {
            VRCAvatarDescriptor descriptor = ctx.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            VRCExpressionParameters source = descriptor != null ? descriptor.expressionParameters : null;
            if(source == null || source.parameters == null) return;

            int limit = VRCExpressionParameters.MAX_PARAMETER_COST;
            int cost = source.CalcTotalCost();
            if(cost <= limit)
            {
                Debug.Log(LogPrefix + " " + ctx.AvatarRootObject.name + ": " + cost + " / " + limit + " bits, within the limit. Nothing is compressed.");
                MacaronParamEstimate.RecordLast(ctx.AvatarRootObject.name, cost, cost);
                return;
            }

            // Candidates
            var numbers = new List<VRCExpressionParameters.Parameter>();
            var bools = new List<VRCExpressionParameters.Parameter>();
            foreach(VRCExpressionParameters.Parameter p in source.parameters)
            {
                if(p == null || string.IsNullOrEmpty(p.name) || !p.networkSynced || IsExcluded(p.name, p.valueType, settings)) continue;
                if(p.valueType == VRCExpressionParameters.ValueType.Bool) bools.Add(p);
                else numbers.Add(p);
            }

            Plan plan = ChoosePlan(numbers, bools, cost, limit, Mathf.Clamp(settings.maxSyncSteps, 2, 32));
            if(plan == null)
            {
                Debug.LogWarning(LogPrefix + " " + ctx.AvatarRootObject.name + ": over the synced parameter limit (" + cost + " / " + limit + " bits), but there is nothing that compression can save. Remove some menu items.");
                return;
            }

            // FX controller (copied first when it is still the user's own asset).
            AnimatorController fx = WritableFx(ctx, descriptor);
            if(fx == null)
            {
                Debug.LogWarning(LogPrefix + " " + ctx.AvatarRootObject.name + ": no FX animator controller, nothing is compressed.");
                return;
            }

            // Expression Parameters: always a clone (the original asset is never touched).
            VRCExpressionParameters exprParams = UnityEngine.Object.Instantiate(source);
            exprParams.name = source.name;
            AssetDatabase.AddObjectToAsset(exprParams, ctx.AssetContainer);
            descriptor.expressionParameters = exprParams;

            var compressed = new HashSet<string>(plan.Numbers.Select(p => p.name).Concat(plan.Bools.Select(p => p.name)));
            foreach(VRCExpressionParameters.Parameter p in exprParams.parameters)
            {
                if(p != null && compressed.Contains(p.name)) p.networkSynced = false;
            }

            Build(ctx, fx, exprParams, plan);
            EditorUtility.SetDirty(exprParams);

            int after = exprParams.CalcTotalCost();
            MacaronParamEstimate.RecordLast(ctx.AvatarRootObject.name, cost, after);
            string summary = ctx.AvatarRootObject.name + ": " + cost + " -> " + after + " / " + limit + " bits (" + plan.Numbers.Count + " Int/Float, "
                + plan.Bools.Count + " Bool parameters take turns in " + plan.StateCount + " steps).";
            if(after <= limit) Debug.Log(LogPrefix + " " + summary);
            else Debug.LogWarning(LogPrefix + " " + summary + " Still over the limit: remove some menu items.");
        }

        // s may be null (estimate for an avatar without the component: default settings).
        internal static bool IsExcluded(string name, VRCExpressionParameters.ValueType type, MacaronParamCompressor s)
        {
            if(name.StartsWith(Root, StringComparison.Ordinal)) return true;
            if(VRChatParams.Contains(name) || VRCFTv4Params.Contains(name)) return true;
            foreach(string prefix in VRCFTv5Prefixes) if(name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            if(s == null) return false;

            switch(type)
            {
                case VRCExpressionParameters.ValueType.Bool: if(s.excludeBools) return true; break;
                case VRCExpressionParameters.ValueType.Int: if(s.excludeInts) return true; break;
                case VRCExpressionParameters.ValueType.Float: if(s.excludeFloats) return true; break;
            }
            if(s.excludedPrefixes != null)
                foreach(string prefix in s.excludedPrefixes) if(!string.IsNullOrEmpty(prefix) && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            if(s.excludedSuffixes != null)
                foreach(string suffix in s.excludedSuffixes) if(!string.IsNullOrEmpty(suffix) && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void RemoveSettings(MacaronParamCompressor settings)
        {
            if(settings == null) return;
            GameObject go = settings.gameObject;
            // The prefab object holds nothing else: remove it entirely; otherwise only the component.
            bool onlyThis = go.transform.childCount == 0 && go.GetComponents<Component>().Length == 2;
            if(onlyThis && go.transform.parent != null) UnityEngine.Object.DestroyImmediate(go);
            else UnityEngine.Object.DestroyImmediate(settings);
        }

        //------------------------------------------------------------------------------------------
        // FX controller

        private static AnimatorController WritableFx(BuildContext ctx, VRCAvatarDescriptor descriptor)
        {
            VRCAvatarDescriptor.CustomAnimLayer[] layers = descriptor.baseAnimationLayers;
            if(layers == null) return null;
            int fxIndex = Array.FindIndex(layers, l => l.type == VRCAvatarDescriptor.AnimLayerType.FX);
            if(fxIndex < 0 || layers[fxIndex].isDefault) return null;
            AnimatorController fx = layers[fxIndex].animatorController as AnimatorController;
            if(fx == null) return null;

            string path = AssetDatabase.GetAssetPath(fx);
            string containerPath = AssetDatabase.GetAssetPath(ctx.AssetContainer);
            if(!string.IsNullOrEmpty(path) && !IsTemporary(path, containerPath))
            {
                if(string.IsNullOrEmpty(containerPath)) return null;      // nowhere safe to put a copy
                // Still the user's controller (no Modular Avatar copy): work on a copy in NDMF's temporary folder.
                string folder = System.IO.Path.GetDirectoryName(containerPath).Replace('\\', '/');
                string copy = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + MacaronCore.SafeFileName(fx.name) + "_ParamSync.controller");
                if(!AssetDatabase.CopyAsset(path, copy)) return null;
                fx = AssetDatabase.LoadAssetAtPath<AnimatorController>(copy);
                if(fx == null) return null;
                layers[fxIndex].animatorController = fx;
                descriptor.baseAnimationLayers = layers;
            }
            return fx;
        }

        // Assets made during this build: the asset container itself, NDMF's generated folders, or the container's own
        // folder. Never a whole top-level folder such as "Assets", so the user's own assets are not mistaken for it.
        private static bool IsTemporary(string path, string containerPath)
        {
            if(string.IsNullOrEmpty(path)) return false;
            if(path.StartsWith("Assets/ZZZ_GeneratedAssets/", StringComparison.Ordinal) ||
               path.StartsWith("Packages/nadena.dev.ndmf/", StringComparison.Ordinal)) return true;
            if(string.IsNullOrEmpty(containerPath)) return false;
            if(path == containerPath) return true;
            string folder = System.IO.Path.GetDirectoryName(containerPath).Replace('\\', '/');
            if(folder.IndexOf('/') < 0) return false;                     // container directly under Assets / Packages
            return path.StartsWith(folder + "/", StringComparison.Ordinal);
        }

        //------------------------------------------------------------------------------------------
        // Animator

        private static void Build(BuildContext ctx, AnimatorController fx, VRCExpressionParameters exprParams, Plan plan)
        {
            UnityEngine.Object holder = ctx.AssetContainer;

            // Write Defaults: follow most of the avatar's states.
            int on = 0, off = 0;
            foreach(AnimatorControllerLayer l in fx.layers)
            {
                if(l.stateMachine == null) continue;
                foreach(ChildAnimatorState s in l.stateMachine.states)
                {
                    if(s.state == null || s.state.name.StartsWith("Warning from VRCFury", StringComparison.Ordinal)) continue;
                    if(s.state.writeDefaultValues) on++; else off++;
                }
            }
            bool wd = on > off;

            // Parameters
            if(!fx.parameters.Any(x => x.name == IsLocalName)) fx.AddParameter(IsLocalName, AnimatorControllerParameterType.Bool);
            if(!fx.parameters.Any(x => x.name == TrueName))
            {
                fx.AddParameter(new AnimatorControllerParameter { name = TrueName, type = AnimatorControllerParameterType.Bool, defaultBool = true });
            }
            var added = new List<VRCExpressionParameters.Parameter>();
            for(int i = 0; i < plan.PointerBits; i++) AddSynced(fx, added, PointerName + i, VRCExpressionParameters.ValueType.Bool);
            for(int i = 0; i < plan.NumbersPerState && plan.Numbers.Count > 0; i++) AddSynced(fx, added, DataNumName + i, VRCExpressionParameters.ValueType.Int);
            for(int i = 0; i < plan.BoolsPerState && plan.Bools.Count > 0; i++) AddSynced(fx, added, DataBoolName + i, VRCExpressionParameters.ValueType.Bool);
            exprParams.parameters = exprParams.parameters.Concat(added).ToArray();

            // Layer
            string layerName = fx.MakeUniqueLayerName("lilMCR Param Sync");
            var machine = new AnimatorStateMachine
            {
                name = layerName,
                hideFlags = HideFlags.HideInHierarchy,
                anyStatePosition = new Vector3(20, -90),
                exitPosition = new Vector3(20, -60),
                entryPosition = new Vector3(20, -30)
            };
            AssetDatabase.AddObjectToAsset(machine, holder);
            fx.AddLayer(new AnimatorControllerLayer
            {
                name = layerName, blendingMode = AnimatorLayerBlendingMode.Override, defaultWeight = 1f, stateMachine = machine
            });

            AnimatorState entry = machine.AddState("Entry Selector", new Vector3(0, 60));
            entry.writeDefaultValues = wd;
            entry.motion = DummyClip(holder, "Entry Selector");
            machine.defaultState = entry;

            bool isLocalBool = fx.parameters.Any(x => x.name == IsLocalName && x.type == AnimatorControllerParameterType.Bool);
            AnimatorStateMachine local = SubMachine(machine, entry, false, isLocalBool);
            AnimatorStateMachine remote = SubMachine(machine, entry, true, isLocalBool);

            // States: local "Set" cycles through the batches and copies values into the shared sync parameters,
            // remote "Get" reads the pointer and copies them back.
            int nps = Math.Max(1, plan.NumbersPerState), bps = Math.Max(1, plan.BoolsPerState);
            AnimatorState prevExtra = null;
            Vector3 setPos = new Vector3(0, 60), getPos = new Vector3(0, 60);
            for(int i = 0; i < plan.StateCount; i++)
            {
                AnimatorState set = local.AddState("Local Set #" + i, setPos);
                set.writeDefaultValues = wd;
                set.motion = DummyClip(holder, set.name);
                VRCAvatarParameterDriver setDriver = set.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                setDriver.localOnly = false;
                for(int bit = 0; bit < plan.PointerBits; bit++)
                {
                    setDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
                    {
                        type = VRC_AvatarParameterDriver.ChangeType.Set, name = PointerName + bit, value = ((i >> bit) & 1) == 1 ? 1f : 0f
                    });
                }

                AnimatorState get = remote.AddState("Remote Get #" + i, getPos);
                get.writeDefaultValues = wd;
                get.motion = DummyClip(holder, get.name);
                VRCAvatarParameterDriver getDriver = get.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                getDriver.localOnly = false;

                bool last = i == plan.StateCount - 1;
                AnimatorState extra = local.AddState("Extra Sync Frame #" + i, setPos + new Vector3(last ? -300 : 300, 0));
                extra.writeDefaultValues = wd;
                extra.motion = DummyClip(holder, extra.name);
                Transition(set, extra, true);

                AnimatorTransition entryTr = remote.AddEntryTransition(get);
                for(int bit = 0; bit < plan.PointerBits; bit++)
                {
                    entryTr.AddCondition(((i >> bit) & 1) == 1 ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, PointerName + bit);
                }
                AnimatorStateTransition exit = get.AddExitTransition();
                exit.hasExitTime = false;
                exit.exitTime = 0f;
                exit.hasFixedDuration = true;
                exit.duration = 0f;
                exit.offset = 0f;
                exit.AddCondition(AnimatorConditionMode.If, 0, TrueName);

                if(i == 0)
                {
                    local.defaultState = set;
                    remote.defaultState = get;
                    setPos.x += 300;
                }
                else if(prevExtra != null)
                {
                    Transition(prevExtra, set, false).AddCondition(AnimatorConditionMode.If, 0, TrueName);
                }
                if(last) Transition(extra, local.defaultState, false).AddCondition(AnimatorConditionMode.If, 0, TrueName);
                prevExtra = extra;

                for(int k = 0; k < nps; k++)
                {
                    int idx = i * nps + k;
                    if(idx >= plan.Numbers.Count) break;
                    VRCExpressionParameters.Parameter p = plan.Numbers[idx];
                    if(p.valueType == VRCExpressionParameters.ValueType.Int) CopyInt(fx, setDriver, getDriver, p.name, DataNumName + k);
                    else CopyFloat(fx, setDriver, getDriver, p.name, DataNumName + k);
                }
                for(int k = 0; k < bps; k++)
                {
                    int idx = i * bps + k;
                    if(idx >= plan.Bools.Count) break;
                    CopyBool(fx, setDriver, getDriver, plan.Bools[idx].name, DataBoolName + k);
                }

                setPos.y += 100;
                getPos.y += 60;
            }

            EditorUtility.SetDirty(fx);
        }

        private static void AddSynced(AnimatorController fx, List<VRCExpressionParameters.Parameter> list, string name, VRCExpressionParameters.ValueType type)
        {
            if(!fx.parameters.Any(x => x.name == name))
            {
                fx.AddParameter(name, type == VRCExpressionParameters.ValueType.Bool ? AnimatorControllerParameterType.Bool : AnimatorControllerParameterType.Int);
            }
            list.Add(new VRCExpressionParameters.Parameter { name = name, valueType = type, saved = false, defaultValue = 0f, networkSynced = true });
        }

        private static AnimatorStateMachine SubMachine(AnimatorStateMachine parent, AnimatorState entry, bool remote, bool isLocalBool)
        {
            AnimatorStateMachine m = parent.AddStateMachine(remote ? "Remote User (Get)" : "Local User (Set)", new Vector3(remote ? 200 : -200, 160));
            m.entryPosition = new Vector3(remote ? -260 : 20, -30);
            m.exitPosition = new Vector3(remote ? 300 : 20, remote ? -30 : -60);
            m.anyStatePosition = new Vector3(20, remote ? -30 : -90);
            m.parentStateMachinePosition = new Vector3(0, remote ? -70 : -130);

            AnimatorStateTransition t = entry.AddTransition(m);
            t.hasExitTime = false;
            t.exitTime = 0f;
            t.hasFixedDuration = true;
            t.duration = 0f;
            t.offset = 0f;
            if(isLocalBool) t.AddCondition(remote ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If, 0, IsLocalName);
            else t.AddCondition(remote ? AnimatorConditionMode.Less : AnimatorConditionMode.Greater, remote ? 0.992f : 0.008f, IsLocalName);
            return m;
        }

        private static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, bool hasExitTime)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.canTransitionToSelf = false;
            t.hasExitTime = hasExitTime;
            t.exitTime = hasExitTime ? StateExitTime : 0f;
            t.hasFixedDuration = true;
            t.offset = 0f;
            t.duration = 0f;
            return t;
        }

        // A state needs a motion with some length so "exit time" advances.
        private static AnimationClip DummyClip(UnityEngine.Object holder, string name)
        {
            var clip = new AnimationClip { name = name + "_dummy" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("_lilMCR_dummy", typeof(GameObject), "m_IsActive"), new AnimationCurve(new Keyframe(0f, 1f)));
            AssetDatabase.AddObjectToAsset(clip, holder);
            return clip;
        }

        private static void EnsureParam(AnimatorController fx, string name, AnimatorControllerParameterType type)
        {
            if(!fx.parameters.Any(x => x.name == name)) fx.AddParameter(name, type);
        }

        private static void CopyInt(AnimatorController fx, VRCAvatarParameterDriver set, VRCAvatarParameterDriver get, string param, string sync)
        {
            EnsureParam(fx, param, AnimatorControllerParameterType.Int);
            set.parameters.Add(new VRC_AvatarParameterDriver.Parameter { type = VRC_AvatarParameterDriver.ChangeType.Copy, name = sync, source = param });
            get.parameters.Add(new VRC_AvatarParameterDriver.Parameter { type = VRC_AvatarParameterDriver.ChangeType.Copy, name = param, source = sync });
        }

        private static void CopyFloat(AnimatorController fx, VRCAvatarParameterDriver set, VRCAvatarParameterDriver get, string param, string sync)
        {
            EnsureParam(fx, param, AnimatorControllerParameterType.Float);
            set.parameters.Add(new VRC_AvatarParameterDriver.Parameter
            {
                type = VRC_AvatarParameterDriver.ChangeType.Copy, name = sync, source = param,
                convertRange = true, sourceMin = -1, sourceMax = 1, destMin = 0, destMax = 254
            });
            get.parameters.Add(new VRC_AvatarParameterDriver.Parameter
            {
                type = VRC_AvatarParameterDriver.ChangeType.Copy, name = param, source = sync,
                convertRange = true, sourceMin = 0, sourceMax = 254, destMin = -1, destMax = 1
            });
        }

        private static void CopyBool(AnimatorController fx, VRCAvatarParameterDriver set, VRCAvatarParameterDriver get, string param, string sync)
        {
            EnsureParam(fx, param, AnimatorControllerParameterType.Bool);
            set.parameters.Add(new VRC_AvatarParameterDriver.Parameter { type = VRC_AvatarParameterDriver.ChangeType.Copy, name = sync, source = param });
            get.parameters.Add(new VRC_AvatarParameterDriver.Parameter { type = VRC_AvatarParameterDriver.ChangeType.Copy, name = param, source = sync });
        }
    }
}
