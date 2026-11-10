#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public class TooHighsAvatarBuilder : EditorWindow
{
    private const string Root = "Assets/TooHighsAvatarBuilder";
    private GameObject avatar;
    private Vector2 scroll;
    private readonly List<Candidate> candidates = new List<Candidate>();
    private string scannedId = "";
    private bool showAll;
    private class Candidate { public Transform bone; public string reason; public bool selected; public int weightedChildren; }

    [MenuItem("Tools/TooHighs/Avatar Builder")]
    public static void Open() { GetWindow<TooHighsAvatarBuilder>("TooHighs Avatar Builder"); }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("TooHighs Avatar Builder V2", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Scan your avatar first. PhysBones can only work where appropriate bones and mesh weights exist. The original is never changed by the Apply command.", MessageType.Info);
        GameObject next = (GameObject)EditorGUILayout.ObjectField("Avatar Root", avatar ? avatar : Selection.activeGameObject, typeof(GameObject), true);
        if (next != avatar) { avatar = next; candidates.Clear(); scannedId = ""; }
        using (new EditorGUI.DisabledScope(!avatar))
        {
            if (GUILayout.Button("Scan Avatar", GUILayout.Height(28))) Scan();
            if (GUILayout.Button("Scan Physics Bone Chains", GUILayout.Height(32))) ScanPhysics();
            if (GUILayout.Button("Create Both Avatar Variants", GUILayout.Height(30))) CreateVariants();
        }
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Physics style: 50% Natural / 50% Expressive", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Candidate detection is conservative and name-based, with a skin-weight bone check. Inspect the proposed roots before applying. No clothing physics can be created from an unrigged mesh.", MessageType.None);
        showAll = EditorGUILayout.ToggleLeft("Show candidates including uncertain chains", showAll);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (Candidate c in candidates)
        {
            if (!showAll && !c.selected) continue;
            EditorGUILayout.BeginVertical("box");
            c.selected = EditorGUILayout.ToggleLeft(GetPath(c.bone, avatar.transform), c.selected);
            EditorGUILayout.LabelField(c.reason, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndScrollView();
        using (new EditorGUI.DisabledScope(!avatar || candidates.All(c => !c.selected) || scannedId != AvatarId()))
        {
            if (GUILayout.Button("Apply Selected PhysBones to NEW Full-Featured Prefab", GUILayout.Height(36))) ApplyPhysics();
        }
        EditorGUILayout.LabelField("Output: " + Root, EditorStyles.miniLabel);
    }

    private string AvatarId() { return avatar ? avatar.GetInstanceID().ToString() : ""; }
    private static Component[] FindComponentsByName(GameObject go, string typeName)
    {
        return go.GetComponentsInChildren<Component>(true).Where(c => c && c.GetType().Name == typeName).ToArray();
    }
    private static string Report(GameObject go)
    {
        var animators = go.GetComponentsInChildren<Animator>(true);
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        return "TooHighs Avatar Scan\nAvatar: " + go.name +
            "\nAnimator count: " + animators.Length +
            "\nHumanoid rig: " + (animators.Any(a => a.avatar && a.avatar.isHuman) ? "Yes" : "No / unassigned") +
            "\nVRC Avatar Descriptor: " + (FindComponentsByName(go, "VRCAvatarDescriptor").Length > 0 ? "Found" : "Missing") +
            "\nRenderers: " + renderers.Length +
            "\nSkinned meshes: " + go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length +
            "\nPhysBones: " + FindComponentsByName(go, "VRCPhysBone").Length +
            "\nPhysBone colliders: " + FindComponentsByName(go, "VRCPhysBoneCollider").Length +
            "\nMaterial slots: " + renderers.Sum(r => r.sharedMaterials.Length) +
            "\nAll objects: " + go.GetComponentsInChildren<Transform>(true).Length;
    }
    private void Scan() { if (!avatar) return; string r = Report(avatar); Debug.Log(r); EditorUtility.DisplayDialog("TooHighs Avatar Scan", r, "OK"); }

    private static readonly string[] Allowed = { "hair", "bang", "fringe", "ponytail", "braid", "skirt", "sleeve", "cloth", "coat", "jacket", "strap", "ribbon", "tail", "ear", "accessory" };
    private static readonly string[] Forbidden = { "hips", "spine", "chest", "neck", "head", "shoulder", "upperarm", "lowerarm", "hand", "finger", "thumb", "upperleg", "lowerleg", "foot", "toe", "eye", "jaw", "teeth", "tongue", "breast", "pelvis" };
    private static bool HasWord(string s, string[] words) { return words.Any(w => s.Contains(w)); }
    private static string GetPath(Transform t, Transform root)
    {
        var names = new List<string>();
        while (t && t != root) { names.Add(t.name); t = t.parent; }
        names.Reverse(); return string.Join("/", names.ToArray());
    }
    private void ScanPhysics()
    {
        candidates.Clear();
        if (!avatar) return;
        var weighted = new HashSet<Transform>();
        foreach (var r in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!r.sharedMesh) continue;
            // Only bones actually referenced by the mesh's bone weights count.
            var weights = r.sharedMesh.boneWeights;
            var indices = new HashSet<int>();
            foreach (var w in weights)
            {
                if (w.weight0 > 0.001f) indices.Add(w.boneIndex0);
                if (w.weight1 > 0.001f) indices.Add(w.boneIndex1);
                if (w.weight2 > 0.001f) indices.Add(w.boneIndex2);
                if (w.weight3 > 0.001f) indices.Add(w.boneIndex3);
            }
            foreach (int i in indices) if (i >= 0 && i < r.bones.Length && r.bones[i]) weighted.Add(r.bones[i]);
        }
        foreach (Transform t in avatar.GetComponentsInChildren<Transform>(true))
        {
            if (t == avatar.transform || t.childCount == 0) continue;
            string n = t.name.ToLowerInvariant().Replace(" ", "").Replace("_", "");
            if (!HasWord(n, Allowed) || HasWord(n, Forbidden)) continue;
            if (t.GetComponents<Component>().Any(c => c && c.GetType().Name == "VRCPhysBone")) continue;
            // Only propose the highest matching ancestor in a named chain.
            bool nested = false;
            for (Transform p = t.parent; p && p != avatar.transform; p = p.parent)
            {
                string pn = p.name.ToLowerInvariant().Replace(" ", "").Replace("_", "");
                if (HasWord(pn, Allowed) && !HasWord(pn, Forbidden)) { nested = true; break; }
            }
            if (nested) continue;
            int count = t.GetComponentsInChildren<Transform>(true).Count(weighted.Contains);
            if (count == 0) continue;
            bool good = count >= 2;
            candidates.Add(new Candidate { bone = t, weightedChildren = count, selected = good,
                reason = count + " weighted bones in subtree; " + (good ? "suggested candidate — review before applying" : "uncertain: only one weighted bone") });
        }
        scannedId = AvatarId();
        var sb = new StringBuilder("Physics bone candidates: " + candidates.Count + "\nSelected by default: " + candidates.Count(c => c.selected) + "\n\n");
        foreach (var c in candidates) sb.AppendLine(GetPath(c.bone, avatar.transform) + " — " + c.reason);
        if (candidates.Count == 0) sb.AppendLine("No confident name/weight matches. This does not prove the avatar cannot support PhysBones. Review rig names and skin weights.");
        Debug.Log(sb.ToString());
        EditorUtility.DisplayDialog("Physics Bone Scan", sb.ToString().Length > 2800 ? sb.ToString().Substring(0, 2800) + "\n...see Console for full report" : sb.ToString(), "OK");
    }
    private static void EnsureFolder(string parent, string name)
    { if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name); }
    private static string Sanitize(string s)
    { foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s.Replace('/', '_').Replace('\\', '_'); }
    private static string SavePrefab(GameObject original, string variant, Action<GameObject> configure)
    {
        EnsureFolder("Assets", "TooHighsAvatarBuilder"); EnsureFolder(Root, variant);
        GameObject copy = Instantiate(original);
        copy.name = original.name + "_" + variant.Replace('-', '_');
        try
        {
            if (configure != null) configure(copy);
            string path = AssetDatabase.GenerateUniqueAssetPath(Root + "/" + variant + "/" + Sanitize(copy.name) + ".prefab");
            bool success;
            PrefabUtility.SaveAsPrefabAsset(copy, path, out success);
            if (!success) throw new Exception("Failed to save prefab at " + path);
            return path;
        }
        finally { DestroyImmediate(copy); }
    }
    private void CreateVariants()
    {
        if (!avatar || !EditorUtility.DisplayDialog("Create variants?", "Create two safe prefab duplicates? No optimization or physics is automatically applied. Your original stays unchanged.", "Create", "Cancel")) return;
        string a = SavePrefab(avatar, "Optimized", null);
        string b = SavePrefab(avatar, "Full-Featured", null);
        AssetDatabase.Refresh();
        Debug.Log("TooHighs variants: " + a + " and " + b);
        EditorUtility.DisplayDialog("Variants created", "Two safe copies saved under " + Root + ". Optimization has NOT yet been applied.", "OK");
    }
    private static Type PhysBoneType()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone", false);
            if (type != null) return type;
        }
        return null;
    }
    private void ApplyPhysics()
    {
        Type type = PhysBoneType();
        if (type == null) { EditorUtility.DisplayDialog("SDK missing", "VRChat SDK3 PhysBone type was not found. Install or repair the VRChat Avatars SDK first.", "OK"); return; }
        var selected = candidates.Where(c => c.selected && c.bone).ToList();
        if (selected.Count == 0) return;
        if (!EditorUtility.DisplayDialog("Apply to new prefab?", "Create a NEW Full-Featured prefab with " + selected.Count + " PhysBone components? The original avatar and previous prefabs will not be changed. PhysBone movement values use conservative SDK defaults until visually tested.", "Create", "Cancel")) return;
        string path = SavePrefab(avatar, "Full-Featured", copy =>
        {
            foreach (Candidate c in selected)
            {
                string relative = GetPath(c.bone, avatar.transform);
                Transform target = copy.transform.Find(relative);
                if (!target || target.GetComponents<Component>().Any(x => x && x.GetType() == type)) continue;
                Component component = target.gameObject.AddComponent(type);
                // Root is the component's own transform. Do not guess SDK field names or change other settings.
                if (!component) throw new Exception("Could not add PhysBone at " + relative);
            }
        });
        AssetDatabase.Refresh();
        Debug.Log("TooHighs PhysBones prefab: " + path);
        EditorUtility.DisplayDialog("PhysBones prefab created", "Created: " + path + "\n\nThe selected chains now have PhysBone components using SDK defaults. The 50/50 movement preset still needs tuning and testing; this is NOT a finished physics setup.", "OK");
    }
}
#endif
