using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LilMCR.MaskGenerator
{
    // Independent Editor-only tool. Does not change mesh, source texture or shader code.
    public sealed class UvMaskWindow : EditorWindow
    {
        Renderer source;
        int slot, mode = 1, sizeIndex = 2, padding = 2, target;
        Mesh mesh;
        Material material;
        Texture background;
        Vector2[] uv;
        int[] indices, islands;
        bool[] selected;
        readonly LinkedList<bool[]> history = new LinkedList<bool[]>();   // newest last, at most 32
        Vector2 pan;
        float zoom = 1;
        bool painting, panning, erase;
        Vector3[] lines, screenUV;
        readonly Vector3[] polygon = new Vector3[3];
        const int GridSize = 64;
        List<int>[] cells;
        readonly List<int> broadTriangles = new List<int>();
        readonly HashSet<int> hitIslands = new HashSet<int>();
        readonly Dictionary<int, List<int>> islandMembers = new Dictionary<int, List<int>>();
        Rect cachedImage;
        bool screenDirty = true;
        int selectedCount;
        string error;
        Texture2D exported;
        bool showWire = true;
        float brushSize = 12;                    // brush radius in screen pixels
        const float MinBrush = 1, MaxBrush = 150;
        int[] visitStamp;
        int stamp;
        Vector2 lastPaint;
        bool hasLastPaint;
        static readonly int[] Sizes = { 512, 1024, 2048, 4096 };
        static readonly string[] Targets = { "全域遮罩（含 PCSS）", "AO 遮罩", "Contact Shadow 遮罩", "不產生陰影遮罩", "埋入遮罩", "PCSS 遮罩", "邊緣光遮罩" };
        static readonly string[] Properties = { "_MCR_GlobalMask", "_MCR_OccMask", "_MCR_ShadowMask", "_MCR_CasterMask", "_MCR_UzuMask", "_lilMCRPCSS_Mask", "_MCR_RimMask" };
        static readonly string[] FileSuffixes = { "_MCR_Mask", "_MCR_OccMask", "_MCR_ShadowMask", "_MCR_CasterMask", "_MCR_UzuMask", "_PCSS_Mask", "_MCR_RimMask" };
        // Index of the Uzumore target (white = pushed back, black = excluded).
        const int UzumoreTarget = 4;
        // Index of the lilMCR PCSS target (PCSS-only mask; lilMCR PCSS materials only).
        const int PcssTarget = 5;

        [MenuItem("Macaron/Mask Generator マスク生成", false, 60)]
        static void Open() { GetWindow<UvMaskWindow>(MaskText.T("UV 遮罩生成器")).minSize = new Vector2(600, 650); }

        // Called by the converter through reflection; the add-on remains optional.
        public static void OpenFor(GameObject avatar)
        {
            var window = GetWindow<UvMaskWindow>();
            window.minSize = new Vector2(600, 650);
            window.source = null; window.slot = 0;
            // Only the mesh named "body" opens by default (object name first, then mesh asset name; case-insensitive).
            // Without one the window stays empty.
            window.source = FindBody(avatar);
            if(window.source != null)
            {
                var mats = window.source.sharedMaterials;
                for(int i = 0; i < mats.Length; i++)
                    if(mats[i] != null && mats[i].HasProperty("_MCR_GlobalMask")) { window.slot = i; break; }
            }
            window.Load(); window.Show(); window.Focus();
        }

        static Renderer FindBody(GameObject avatar)
        {
            if(avatar == null) return null;
            Renderer byMeshName = null;
            foreach(Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                Mesh m;
                if(r is SkinnedMeshRenderer) m = ((SkinnedMeshRenderer)r).sharedMesh;
                else if(r is MeshRenderer) { var f = r.GetComponent<MeshFilter>(); m = f != null ? f.sharedMesh : null; }
                else continue;
                if(string.Equals(r.gameObject.name, "body", StringComparison.OrdinalIgnoreCase)) return r;
                if(byMeshName == null && m != null && string.Equals(m.name, "body", StringComparison.OrdinalIgnoreCase)) byMeshName = r;
            }
            return byMeshName;
        }

        void OnEnable() { Selection.selectionChanged += Repaint; wantsMouseMove = true; }
        void OnDisable() { Selection.selectionChanged -= Repaint; }

        void OnGUI()
        {
            titleContent.text = MaskText.T("UV 遮罩生成器");
            MaskText.LanguagePopup();
            EditorGUILayout.LabelField(target == 3 ? MaskText.T("選中的 UV 面 → 黑色 → 不產生陰影")
                : target == UzumoreTarget ? MaskText.T("選中的 UV 面 → 黑色 → 排除埋入（不推回）")
                : target == PcssTarget ? MaskText.T("選中的 UV 面 → 黑色 → 沒有 PCSS 影子")
                : MaskText.T("選中的 UV 面 → 黑色遮罩 → 不受效果影響"), EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            source = (Renderer)EditorGUILayout.ObjectField(MaskText.T("模型 Renderer"), source, typeof(Renderer), true);
            if(EditorGUI.EndChangeCheck()) { slot = 0; Load(); }
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button(MaskText.T("使用目前選取物件")))
                {
                    source = Selection.activeGameObject == null ? null : Selection.activeGameObject.GetComponent<Renderer>();
                    slot = 0; Load();
                }
                if(GUILayout.Button(MaskText.T("重新讀取 UV"))) Load();
                if(GUILayout.Button(MaskText.T("重設視野"))) { zoom = 1; pan = Vector2.zero; }
            }
            if(source != null)
            {
                var mats = source.sharedMaterials;
                string[] labels = new string[mats.Length];
                for(int i = 0; i < mats.Length; i++) labels[i] = i + ": " + (mats[i] == null ? MaskText.T("無材質") : mats[i].name);
                EditorGUI.BeginChangeCheck();
                slot = EditorGUILayout.Popup(MaskText.T("材質槽 / Submesh"), slot, labels);
                if(EditorGUI.EndChangeCheck()) Load();
            }
            if(!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(MaskText.T(error), MessageType.Warning);
            using(new EditorGUILayout.HorizontalScope())
            {
                mode = GUILayout.Toolbar(mode, new[] { MaskText.T("三角面"), MaskText.T("UV 島") });
                showWire = GUILayout.Toggle(showWire, MaskText.T("網格"), GUILayout.Width(100));
                using(new EditorGUI.DisabledScope(selected == null))
                {
                    if(GUILayout.Button(MaskText.T("全選"))) ChangeAll(0);
                    if(GUILayout.Button(MaskText.T("清除"))) ChangeAll(1);
                    if(GUILayout.Button(MaskText.T("反選"))) ChangeAll(2);
                }
                using(new EditorGUI.DisabledScope(history.Count == 0))
                    if(GUILayout.Button(MaskText.T("復原"))) { selected = history.Last.Value; history.RemoveLast(); selectedCount = 0; foreach(bool v in selected) if(v) selectedCount++; exported = null; }
            }
            brushSize = EditorGUILayout.Slider(MaskText.T("筆刷大小"), brushSize, MinBrush, MaxBrush);
            EditorGUILayout.LabelField(MaskText.T("左鍵拖曳以圓形筆刷選取；Shift＋左鍵取消；[ / ] 調整筆刷大小；中鍵移動；滾輪縮放。重疊 UV 共用遮罩。"), EditorStyles.wordWrappedMiniLabel);
            Rect canvas = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawCanvas(canvas);
            int count = selectedCount;
            EditorGUILayout.LabelField(string.Format(MaskText.T("已選 {0} / {1} 個三角面（橘色）"), count, selected == null ? 0 : selected.Length));
            sizeIndex = EditorGUILayout.Popup(MaskText.T("輸出大小"), sizeIndex, new[] { "512", "1024", "2048", "4096" });
            padding = EditorGUILayout.IntSlider(MaskText.T("黑色區域擴張（像素）"), padding, 0, 16);
            target = EditorGUILayout.Popup(MaskText.T("套用目標"), target, Array.ConvertAll(Targets, MaskText.T));
            // What black / white mean for the chosen target.
            EditorGUILayout.LabelField(target == UzumoreTarget
                ? MaskText.T("白色 = 有埋入效果（鏡頭穿入時推回）／黑色 = 排除（不推回，例如脖子、背後）。選中的面輸出為黑色，其餘為白色。")
                : target == 3
                    ? MaskText.T("白色 = 正常產生陰影／黑色 = 不產生陰影。選中的面輸出為黑色，其餘為白色。")
                : target == PcssTarget
                    ? MaskText.T("白色 = 有 PCSS 影子／黑色 = 沒有 PCSS 影子（只影響 PCSS，不影響 AO 與 Contact Shadow）。選中的面輸出為黑色，其餘為白色。")
                    : MaskText.T("白色 = 有效果／黑色 = 排除效果。選中的面輸出為黑色，其餘為白色。"), EditorStyles.wordWrappedMiniLabel);
            using(new EditorGUI.DisabledScope(selected == null || material == null || !material.HasProperty(Properties[target])))
                if(GUILayout.Button(MaskText.T("生成 PNG 並自動套用…"), GUILayout.Height(28))) Export();
            EditorGUILayout.HelpBox(MaskText.T("生成成功後會自動套用至目前材質的指定遮罩，強度設為 1；AO 遮罩反轉設為關閉。共用此材質的物件都會受影響，材質修改可用 Unity Undo 復原。"), MessageType.Info);
            if(material != null && !material.HasProperty(Properties[target]))
                EditorGUILayout.HelpBox(MaskText.T(target == PcssTarget
                    ? "PCSS 遮罩只適用於 lilMCR PCSS 材質（Macaron/PCSS）。請先用 lilMCR PCSS Setup 轉換材質。"
                    : "目前材質不支援此遮罩目標，請選擇 lilMCR 或 lilMCR PCSS 材質。"), MessageType.Warning);
        }

        void Load()
        {
            uv = null; indices = null; selected = null; islands = null; lines = null;
            cells = null; screenUV = null; screenDirty = true; selectedCount = 0;
            broadTriangles.Clear(); islandMembers.Clear(); hitIslands.Clear();
            material = null; mesh = null; background = null; exported = null;
            history.Clear(); error = null; pan = Vector2.zero; zoom = 1;
            painting = panning = false; hasLastPaint = false; visitStamp = null;
            if(source == null) return;
            var skin = source as SkinnedMeshRenderer;
            var filter = source.GetComponent<MeshFilter>();
            mesh = skin != null ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
            if(mesh == null) { error = "找不到 Mesh；請選擇 SkinnedMeshRenderer 或 MeshRenderer。"; return; }
            var mats = source.sharedMaterials;
            if(slot < 0 || slot >= mats.Length || slot >= mesh.subMeshCount) { error = "材質槽沒有對應的 Submesh。"; return; }
            material = mats[slot];
            if(material == null) { error = "此槽沒有材質。"; return; }
            try
            {
                if(mesh.GetTopology(slot) != UnityEngine.MeshTopology.Triangles) throw new InvalidOperationException("只支援三角形拓撲。");
                var coords = mesh.uv;
                var tris = mesh.GetTriangles(slot);
                if(coords.Length != mesh.vertexCount || tris.Length == 0) throw new InvalidOperationException("沒有有效的 UV0 或三角面。");
                Vector2 scale = Vector2.one, offset = Vector2.zero;
                if(material.HasProperty("_MainTex"))
                {
                    background = material.GetTexture("_MainTex");
                    scale = material.GetTextureScale("_MainTex"); offset = material.GetTextureOffset("_MainTex");
                }
                for(int i = 0; i < coords.Length; i++) coords[i] = Vector2.Scale(coords[i], scale) + offset;
                foreach(int v in tris)
                {
                    var p = coords[v];
                    if(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.x) || float.IsInfinity(p.y)
                       || p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1)
                        throw new InvalidOperationException("目前支援 0–1 範圍 UV。此材質經 MainTex Tiling/Offset 後超出範圍；不會自動裁切或錯誤輸出。請使用一般 0–1 UV 材質。");
                }
                uv = coords; indices = tris; selected = new bool[tris.Length / 3];
                visitStamp = new int[selected.Length]; stamp = 0;
                islands = BuildIslands(coords, tris); lines = new Vector3[tris.Length * 2];
                screenUV = new Vector3[coords.Length];
                BuildPickGrid();
                error = "使用 UV0 與 MainTex Tiling/Offset。UV 動畫、旋轉、Parallax 或其他動態 UV 變形不在本工具預覽範圍內。";
            }
            catch(Exception ex) { uv = null; indices = null; selected = null; error = ex.Message; }
        }

        static int Root(int[] parents, int a)
        {
            while(parents[a] != a) { parents[a] = parents[parents[a]]; a = parents[a]; }
            return a;
        }

        // UV-coordinate edge adjacency also connects vertices split by normals at the same UV edge.
        static int[] BuildIslands(Vector2[] coords, int[] tris)
        {
            int n = tris.Length / 3;
            var parents = new int[n];
            var points = new Dictionary<Vector2, int>();
            var edges = new Dictionary<ulong, int>();
            for(int i = 0; i < n; i++) parents[i] = i;
            for(int t = 0; t < n; t++) for(int e = 0; e < 3; e++)
            {
                Vector2 a = coords[tris[t * 3 + e]], b = coords[tris[t * 3 + (e + 1) % 3]];
                int ia, ib;
                if(!points.TryGetValue(a, out ia)) { ia = points.Count; points.Add(a, ia); }
                if(!points.TryGetValue(b, out ib)) { ib = points.Count; points.Add(b, ib); }
                if(ia == ib) continue;
                ulong key = ((ulong)(uint)Math.Min(ia, ib) << 32) | (uint)Math.Max(ia, ib);
                int other;
                if(edges.TryGetValue(key, out other)) parents[Root(parents, t)] = Root(parents, other);
                else edges.Add(key, t);
            }
            for(int i = 0; i < n; i++) parents[i] = Root(parents, i);
            return parents;
        }

        void SetSelected(int t, bool value)
        {
            if(selected[t] == value) return;
            selected[t] = value; selectedCount += value ? 1 : -1;
        }

        void BuildPickGrid()
        {
            cells = new List<int>[GridSize * GridSize];
            for(int t = 0; t < selected.Length; t++)
            {
                List<int> members;
                if(!islandMembers.TryGetValue(islands[t], out members))
                    islandMembers.Add(islands[t], members = new List<int>());
                members.Add(t);
                Vector2 a = uv[indices[3*t]], b = uv[indices[3*t+1]], c = uv[indices[3*t+2]];
                double area = Math.Abs((double)(b.x-a.x)*(c.y-a.y) - (double)(b.y-a.y)*(c.x-a.x));
                // The same cross-product tolerance as Inside expands tiny triangles.
                // Put these in a bounded fallback list instead of missing edge hits.
                if(area < 1e-8) { broadTriangles.Add(t); continue; }
                double e = 1e-8 / area;
                double sx = a.x+b.x+c.x, sy = a.y+b.y+c.y;
                double loX = Math.Min(a.x,Math.Min(b.x,c.x))*(1+3*e)-sx*e-1e-6;
                double hiX = Math.Max(a.x,Math.Max(b.x,c.x))*(1+3*e)-sx*e+1e-6;
                double loY = Math.Min(a.y,Math.Min(b.y,c.y))*(1+3*e)-sy*e-1e-6;
                double hiY = Math.Max(a.y,Math.Max(b.y,c.y))*(1+3*e)-sy*e+1e-6;
                int x0 = Mathf.Clamp((int)Math.Floor(loX*GridSize),0,GridSize-1);
                int x1 = Mathf.Clamp((int)Math.Floor(hiX*GridSize),0,GridSize-1);
                int y0 = Mathf.Clamp((int)Math.Floor(loY*GridSize),0,GridSize-1);
                int y1 = Mathf.Clamp((int)Math.Floor(hiY*GridSize),0,GridSize-1);
                if((x1-x0+1)*(y1-y0+1)>64) { broadTriangles.Add(t); continue; }
                for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++)
                {
                    int cell = y*GridSize+x;
                    if(cells[cell] == null) cells[cell] = new List<int>();
                    cells[cell].Add(t);
                }
            }
        }

        void PickTriangle(int t, Vector2 p, float r)
        {
            if(visitStamp[t] == stamp) return;
            visitStamp[t] = stamp;
            if(!TouchesCircle(p, r, uv[indices[3*t]], uv[indices[3*t+1]], uv[indices[3*t+2]])) return;
            if(mode == 0) SetSelected(t,!erase); else hitIslands.Add(islands[t]);
        }

        // Triangle and circle overlap: center inside the triangle, or an edge within the radius.
        static bool TouchesCircle(Vector2 p, float r, Vector2 a, Vector2 b, Vector2 c)
        {
            if(Inside(p, a, b, c)) return true;
            float r2 = r * r;
            return SegmentDistSq(p, a, b) <= r2 || SegmentDistSq(p, b, c) <= r2 || SegmentDistSq(p, c, a) <= r2;
        }

        static float SegmentDistSq(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len = Vector2.Dot(ab, ab);
            float t = len > 0 ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len) : 0;
            return (a + ab * t - p).sqrMagnitude;
        }

        // p and r in UV units.
        void PaintAt(Vector2 p, float r)
        {
            hitIslands.Clear();
            if(++stamp == int.MaxValue) { stamp = 1; Array.Clear(visitStamp, 0, visitStamp.Length); }
            if(cells == null)
            {
                for(int t=0;t<selected.Length;t++) PickTriangle(t,p,r);
            }
            else if(p.x+r >= 0 && p.x-r <= 1 && p.y+r >= 0 && p.y-r <= 1)
            {
                int x0 = Mathf.Clamp((int)Math.Floor((p.x-r)*GridSize),0,GridSize-1);
                int x1 = Mathf.Clamp((int)Math.Floor((p.x+r)*GridSize),0,GridSize-1);
                int y0 = Mathf.Clamp((int)Math.Floor((p.y-r)*GridSize),0,GridSize-1);
                int y1 = Mathf.Clamp((int)Math.Floor((p.y+r)*GridSize),0,GridSize-1);
                for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++)
                {
                    var candidates = cells[y*GridSize+x];
                    if(candidates != null) foreach(int t in candidates) PickTriangle(t,p,r);
                }
                foreach(int t in broadTriangles) PickTriangle(t,p,r);
            }
            if(mode == 1) foreach(int island in hitIslands)
                foreach(int t in islandMembers[island]) SetSelected(t,!erase);
        }

        // Fills the gap between two mouse events so a fast drag does not skip triangles.
        void PaintStroke(Vector2 p, float r)
        {
            if(hasLastPaint)
            {
                float step = Mathf.Max(r * 0.5f, 1e-4f);
                float dist = Vector2.Distance(lastPaint, p);
                int n = Mathf.Min(Mathf.CeilToInt(dist / step), 256);
                for(int i = 1; i < n; i++) PaintAt(Vector2.Lerp(lastPaint, p, (float)i / n), r);
            }
            PaintAt(p, r);
            lastPaint = p; hasLastPaint = true;
        }

        void UpdateScreenCache(Rect image)
        {
            if(!screenDirty && cachedImage == image) return;
            for(int i=0;i<uv.Length;i++) screenUV[i] = ToScreen(uv[i],image);
            for(int t=0;t<selected.Length;t++)
            {
                Vector3 a=screenUV[indices[t*3]],b=screenUV[indices[t*3+1]],c=screenUV[indices[t*3+2]];
                int k=t*6; lines[k]=a;lines[k+1]=b;lines[k+2]=b;lines[k+3]=c;lines[k+4]=c;lines[k+5]=a;
            }
            cachedImage=image;screenDirty=false;
        }
        void Remember()
        {
            if(selected == null) return;
            if(history.Count >= 32) history.RemoveFirst();   // drop the oldest step only
            history.AddLast((bool[])selected.Clone()); exported = null;
        }
        void ChangeAll(int action)
        {
            Remember();
            for(int i = 0; i < selected.Length; i++) SetSelected(i, action == 0 || (action == 2 && !selected[i]));
        }
        static Vector2 ToScreen(Vector2 p, Rect r) { return new Vector2(r.x + p.x * r.width, r.yMax - p.y * r.height); }
        static float Cross(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }
        internal static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float area = Cross(b - a, c - a);
            if(Mathf.Abs(area) < 1e-12f) return false;
            float x = Cross(b - a, p - a), y = Cross(c - b, p - b), z = Cross(a - c, p - c);
            return area > 0 ? x >= -1e-8f && y >= -1e-8f && z >= -1e-8f : x <= 1e-8f && y <= 1e-8f && z <= 1e-8f;
        }

        void DrawCanvas(Rect canvas)
        {
            GUI.Box(canvas, GUIContent.none);
            GUI.BeginGroup(canvas);
            try
            {
                var ev = Event.current;
                float side = Mathf.Max(1, Mathf.Min(canvas.width, canvas.height) - 16) * zoom;
                var image = new Rect((canvas.width - side) * 0.5f + pan.x, (canvas.height - side) * 0.5f + pan.y, side, side);
                bool hovering = new Rect(0, 0, canvas.width, canvas.height).Contains(ev.mousePosition);
                if(ev.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(image, new Color(0.25f, 0.25f, 0.25f));
                    if(background != null) GUI.DrawTexture(image, background, ScaleMode.StretchToFill, false);
                    if(indices != null)
                    {
                        Handles.BeginGUI();
                        Color old = Handles.color;
                        try
                        {
                            if(showWire || selectedCount > 0) UpdateScreenCache(image);
                            if(selectedCount > 0) for(int t = 0; t < selected.Length; t++) if(selected[t])
                            {
                                polygon[0] = screenUV[indices[t * 3]];
                                polygon[1] = screenUV[indices[t * 3 + 1]];
                                polygon[2] = screenUV[indices[t * 3 + 2]];
                                Handles.color = new Color(1, 0.55f, 0, 0.45f);
                                Handles.DrawAAConvexPolygon(polygon);
                            }
                            if(showWire) { Handles.color = new Color(1, 0.15f, 0.15f, 0.85f); Handles.DrawLines(lines); }
                            if(hovering && !panning && selected != null)
                            {
                                Handles.color = erase && painting ? new Color(0.4f, 0.8f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.9f);
                                Handles.DrawWireDisc(ev.mousePosition, Vector3.forward, brushSize);
                            }
                        }
                        finally { Handles.color = old; Handles.EndGUI(); }
                    }
                }
                if(ev.type == EventType.MouseMove && hovering) Repaint();
                if(ev.type == EventType.KeyDown && hovering && (ev.keyCode == KeyCode.LeftBracket || ev.keyCode == KeyCode.RightBracket))
                {
                    brushSize = Mathf.Clamp(brushSize * (ev.keyCode == KeyCode.RightBracket ? 1.25f : 0.8f), MinBrush, MaxBrush);
                    ev.Use(); Repaint();
                }
                if(ev.type == EventType.ScrollWheel && hovering)
                {
                    float next = Mathf.Clamp(zoom * Mathf.Pow(1.12f, -ev.delta.y), 0.25f, 16);
                    Vector2 center = new Vector2(canvas.width, canvas.height) * 0.5f;
                    pan = ev.mousePosition - center - (ev.mousePosition - center - pan) * (next / zoom);
                    zoom = next; ev.Use(); Repaint();
                }
                if(ev.type == EventType.MouseDown && hovering && ev.button == 2) { panning = true; ev.Use(); }
                if(ev.type == EventType.MouseDrag && panning) { pan += ev.delta; ev.Use(); Repaint(); }
                if(ev.type == EventType.MouseDown && hovering && ev.button == 0 && selected != null)
                { Remember(); painting = true; erase = ev.shift; hasLastPaint = false; }
                if(painting && hovering && (ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag))
                {
                    Vector2 p = new Vector2((ev.mousePosition.x - image.x) / side, 1 - (ev.mousePosition.y - image.y) / side);
                    PaintStroke(p, brushSize / side);
                    ev.Use(); Repaint();
                }
                if(ev.rawType == EventType.MouseUp) { painting = false; panning = false; hasLastPaint = false; }
            }
            finally { GUI.EndGroup(); }
        }

        internal static Color32[] Rasterize(Vector2[] coords, int[] tris, bool[] selection, int size, int expand)
        {
            var black = new bool[size * size];
            Vector2[] offsets = { new Vector2(.25f, .25f), new Vector2(.75f, .25f), new Vector2(.25f, .75f), new Vector2(.75f, .75f) };
            for(int t = 0; t < selection.Length; t++) if(selection[t])
            {
                Vector2 a = coords[tris[t * 3]] * size, b = coords[tris[t * 3 + 1]] * size, c = coords[tris[t * 3 + 2]] * size;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), 0, size - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))), 0, size - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), 0, size - 1);
                int y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))), 0, size - 1);
                for(int y = y0; y <= y1; y++) for(int x = x0; x <= x1; x++)
                {
                    if(black[y * size + x]) continue;
                    foreach(var offset in offsets) if(Inside(new Vector2(x, y) + offset, a, b, c)) { black[y * size + x] = true; break; }
                }
            }
            if(expand > 0)
            {
                var temp = new bool[black.Length];
                // Sliding windows: O(pixel count), independent of padding radius.
                for(int y = 0; y < size; y++)
                {
                    int sum = 0;
                    for(int x = 0; x < size + expand; x++)
                    {
                        if(x < size && black[y * size + x]) sum++;
                        int remove = x - 2 * expand - 1;
                        if(remove >= 0 && black[y * size + remove]) sum--;
                        if(x >= expand) temp[y * size + x - expand] = sum > 0;
                    }
                }
                for(int x = 0; x < size; x++)
                {
                    int sum = 0;
                    for(int y = 0; y < size + expand; y++)
                    {
                        if(y < size && temp[y * size + x]) sum++;
                        int remove = y - 2 * expand - 1;
                        if(remove >= 0 && temp[remove * size + x]) sum--;
                        if(y >= expand) black[(y - expand) * size + x] = sum > 0;
                    }
                }
            }
            var pixels = new Color32[black.Length];
            for(int i = 0; i < pixels.Length; i++) { byte v = black[i] ? (byte)0 : (byte)255; pixels[i] = new Color32(v, v, v, 255); }
            return pixels;
        }

        string DefaultMaskName()
        {
            string suffix = FileSuffixes[target];
            if(source == null) return "Avatar" + suffix;
            Transform avatar = null;
            // No dependency on the VRChat SDK: prefer its descriptor, then the top-most Animator.
            for(Transform t = source.transform; t != null; t = t.parent)
            {
                bool descriptor = false;
                foreach(Component component in t.GetComponents<Component>())
                    if(component != null && component.GetType().Name == "VRCAvatarDescriptor") { descriptor = true; break; }
                if(descriptor) { avatar = t; break; }
                if(t.GetComponent<Animator>() != null) avatar = t;
            }
            if(avatar == null) avatar = source.transform.root;
            string name = avatar.name;
            foreach(char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim().TrimEnd('.');
            return (string.IsNullOrEmpty(name) ? "Avatar" : name) + suffix;
        }

        void Export()
        {
            if(selected == null || material == null || !material.HasProperty(Properties[target])) return;
            string path = EditorUtility.SaveFilePanelInProject(MaskText.T("生成並套用遮罩"), DefaultMaskName(), "png", MaskText.T("選中的區域會輸出為黑色，並自動套用至目前選擇的遮罩目標。"));
            if(string.IsNullOrEmpty(path)) return;
            if(background != null && AssetDatabase.LoadAssetAtPath<Texture>(path) == background) { EditorUtility.DisplayDialog(MaskText.T("無法覆蓋"), MaskText.T("請勿覆蓋目前作為背景的主貼圖。"), MaskText.T("確定")); return; }
            Texture2D tex = null;
            try
            {
                EditorUtility.DisplayProgressBar(MaskText.T("輸出遮罩"), MaskText.T("填入 UV 三角面與擴張邊界…"), .3f);
                int size = Sizes[sizeIndex];
                tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
                tex.SetPixels32(Rasterize(uv, indices, selected, size, padding)); tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default; importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.None;
                // The shaders read the red channel only. High quality compression (BC7 on PC) keeps the black / white
                // preview and the antialiased edges at a quarter of the memory of uncompressed RGBA32.
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.maxTextureSize = size; importer.mipmapEnabled = true;
                // The shaders sample masks with lilToon's shared repeat sampler; match it so preview and result agree.
                importer.wrapMode = TextureWrapMode.Repeat; importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
                exported = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if(exported == null) throw new InvalidOperationException("遮罩匯入失敗，材質未套用。");
                Apply();
                EditorGUIUtility.PingObject(exported);
            }
            catch(Exception ex) { Debug.LogException(ex); EditorUtility.DisplayDialog(MaskText.T("輸出失敗"), MaskText.T(ex.Message), MaskText.T("確定")); }
            finally { if(tex != null) DestroyImmediate(tex); EditorUtility.ClearProgressBar(); }
        }

        void Apply()
        {
            if(exported == null || material == null || !material.HasProperty(Properties[target])) return;
            Undo.RecordObject(material, "Apply lilMCR UV mask");
            string prop = Properties[target];
            material.SetTexture(prop, exported);
            if(material.HasProperty(prop + "Amount")) material.SetFloat(prop + "Amount", 1);
            if(target == 0 || target == 3) { material.SetTextureScale(prop, Vector2.one); material.SetTextureOffset(prop, Vector2.zero); }
            if(target == 1 && material.HasProperty("_MCR_OccMaskFlip")) material.SetFloat("_MCR_OccMaskFlip", 0);
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssets();
        }
    }
}
