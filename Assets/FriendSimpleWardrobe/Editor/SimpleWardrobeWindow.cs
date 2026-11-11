using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace FriendAvatarTools.Wardrobe
{
    public sealed class SimpleWardrobeWindow : EditorWindow
    {
        private const string RootName = "かんたん衣装切替";
        private const string SelectionParameter = "FriendWardrobe/Outfit";
        private const string Output = "Assets/FriendWardrobeGenerated";
        [SerializeField] private VRCAvatarDescriptor avatar;
        [SerializeField] private List<GameObject> outfits = new List<GameObject>();
        [SerializeField] private int initialOutfit;
        private Vector2 scroll;

        [MenuItem("Tools/フレンド向け/かんたん衣装切替")]
        private static void Open()
        {
            var window = GetWindow<SimpleWardrobeWindow>("かんたん衣装切替");
            var selected = Selection.activeGameObject;
            if (selected && !EditorUtility.IsPersistent(selected))
            {
                var descriptor = selected.GetComponentInParent<VRCAvatarDescriptor>(true);
                if (descriptor) window.LoadAvatar(descriptor);
            }
            window.minSize = new Vector2(410, 300);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("先に着せ替えを済ませ、Hierarchyから衣装を追加してください。1着を選ぶと、登録した他の衣装が非表示になります。", MessageType.Info);
            var next = (VRCAvatarDescriptor)EditorGUILayout.ObjectField("アバター", avatar, typeof(VRCAvatarDescriptor), true);
            if (next != avatar) LoadAvatar(next);
            using (new EditorGUI.DisabledScope(!avatar))
            {
                var drop = GUILayoutUtility.GetRect(0, 52, GUILayout.ExpandWidth(true));
                GUI.Box(drop, "Hierarchyの衣装をまとめてここへドラッグ");
                AcceptDrop(drop);
                scroll = EditorGUILayout.BeginScrollView(scroll);
                for (int i = 0; i < outfits.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Toggle(initialOutfit == i, "初期", EditorStyles.radioButton, GUILayout.Width(48)))
                            initialOutfit = i;
                        outfits[i] = (GameObject)EditorGUILayout.ObjectField(outfits[i], typeof(GameObject), true);
                        if (GUILayout.Button("外す", GUILayout.Width(48)))
                        {
                            outfits.RemoveAt(i);
                            if (initialOutfit > i) initialOutfit--;
                            initialOutfit = Mathf.Clamp(initialOutfit, 0, Math.Max(0, outfits.Count - 1));
                            break;
                        }
                    }
                }
                EditorGUILayout.EndScrollView();
                using (new EditorGUI.DisabledScope(outfits.Count >= 32))
                    if (GUILayout.Button("入力欄を追加")) outfits.Add(null);
            }
            string problem = Validate(avatar, outfits);
            if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Warning);
            using (new EditorGUI.DisabledScope(problem != null || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("衣装切替メニューを作る・更新する", GUILayout.Height(32))) Apply();
        }

        private void LoadAvatar(VRCAvatarDescriptor next)
        {
            avatar = next;
            outfits.Clear();
            initialOutfit = 0;
            var root = avatar ? avatar.transform.Find(RootName) : null;
            if (!IsGenerated(root)) return;
            var config = root.GetComponent<ModularAvatarParameters>().parameters.Single(p => p.nameOrPrefix == SelectionParameter);
            var controller = (AnimatorController)root.GetComponent<ModularAvatarMergeAnimator>().animator;
            foreach (var state in controller.layers[0].stateMachine.states)
            {
                var clip = (AnimationClip)state.state.motion;
                var enabled = AnimationUtility.GetCurveBindings(clip)
                    .Single(binding => AnimationUtility.GetEditorCurve(clip, binding).Evaluate(0) > .5f);
                outfits.Add(avatar.transform.Find(enabled.path)?.gameObject);
            }
            initialOutfit = Mathf.Clamp((int)config.defaultValue - 1, 0, Math.Max(0, outfits.Count - 1));
        }

        private void AcceptDrop(Rect drop)
        {
            var evt = Event.current;
            if (!drop.Contains(evt.mousePosition) || (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform))
                return;
            var selection = DragAndDrop.objectReferences.OfType<GameObject>()
                .Where(obj => !EditorUtility.IsPersistent(obj) && obj != avatar.gameObject && obj.transform.IsChildOf(avatar.transform))
                .Where(obj => !outfits.Contains(obj)).Distinct().Take(32 - outfits.Count).ToArray();
            DragAndDrop.visualMode = selection.Length > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (evt.type == EventType.DragPerform && selection.Length > 0)
            {
                DragAndDrop.AcceptDrag();
                outfits.AddRange(selection);
            }
            evt.Use();
        }

        internal static string Validate(VRCAvatarDescriptor target, IReadOnlyList<GameObject> items)
        {
            if (!target || EditorUtility.IsPersistent(target) || !target.gameObject.scene.IsValid())
                return "Hierarchyにあるアバターを選んでください。";
            if (items.Count < 2 || items.Count > 32) return "着せ替え済みの衣装を2〜32着登録してください。";
            var previous = target.transform.Find(RootName);
            if (previous && !IsGenerated(previous)) return "同名のオブジェクトがあるため、その名前を変更してください。";
            var animator = target.GetComponent<Animator>();
            var hips = animator && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            for (int i = 0; i < items.Count; i++)
            {
                var obj = items[i];
                if (!obj || obj == target.gameObject || !obj.transform.IsChildOf(target.transform) ||
                    (previous && obj.transform.IsChildOf(previous))) return "アバター直下か、その子にある衣装を選んでください。";
                if ((target.VisemeSkinnedMesh && target.VisemeSkinnedMesh.transform.IsChildOf(obj.transform)) ||
                    (hips && hips.IsChildOf(obj.transform)) || obj.GetComponentInChildren<VRCAvatarDescriptor>(true))
                    return "アバター本体や骨格全体ではなく、衣装だけのオブジェクトを選んでください。";
                if (!obj.GetComponentInChildren<Renderer>(true)) return obj.name + " に表示用のメッシュがありません。";
                var bindingPath = AnimationUtility.CalculateTransformPath(obj.transform, target.transform);
                if (target.transform.Find(bindingPath) != obj.transform)
                    return "衣装の階層名が重複しています。衣装や親オブジェクトに別の名前を付けてください。";
                for (var parent = obj.transform.parent; parent != target.transform; parent = parent.parent)
                    if (!parent.gameObject.activeSelf) return obj.name + " の親オブジェクトを表示してから登録してください。";
                for (int j = 0; j < i; j++)
                    if (obj == items[j] || obj.transform.IsChildOf(items[j].transform) || items[j].transform.IsChildOf(obj.transform))
                        return "同じ衣装や、親子になっている衣装を重ねて登録できません。";
            }
            if (!previous && target.expressionParameters &&
                target.expressionParameters.CalcTotalCost() + 8 > VRCExpressionParameters.MAX_PARAMETER_COST)
                return "同期パラメーターに8ビットの空きが必要です。";
            return null;
        }

        private void Apply()
        {
            try
            {
                Install(avatar, outfits, initialOutfit);
                ShowNotification(new GUIContent("衣装切替メニューを追加しました"));
            }
            catch (Exception error)
            {
                EditorUtility.DisplayDialog("かんたん衣装切替", error.Message, "閉じる");
            }
        }

        internal static void Install(VRCAvatarDescriptor target, IReadOnlyList<GameObject> items, int initial)
        {
            var problem = Validate(target, items);
            if (problem != null) throw new InvalidOperationException(problem);
            if (initial < 0 || initial >= items.Count) throw new ArgumentOutOfRangeException(nameof(initial));
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("かんたん衣装切替");
            string controllerPath = null;
            try
            {
                var previous = target.transform.Find(RootName);
                var originalInstaller = previous ? previous.GetComponent<ModularAvatarMenuInstaller>() : null;
                var installTarget = originalInstaller ? originalInstaller.installTargetMenu : null;
                if (previous) Undo.DestroyObjectImmediate(previous.gameObject);
                var root = Child(target.transform, RootName);
                root.AddComponent<ModularAvatarMenuInstaller>().installTargetMenu = installTarget;
                var parentItem = root.AddComponent<ModularAvatarMenuItem>();
                parentItem.label = "衣装切替";
                parentItem.MenuSource = SubmenuSource.Children;
                parentItem.Control = new VRCExpressionsMenu.Control { type = VRCExpressionsMenu.Control.ControlType.SubMenu };
                root.AddComponent<ModularAvatarParameters>().parameters.Add(new ParameterConfig
                {
                    nameOrPrefix = SelectionParameter, internalParameter = true, syncType = ParameterSyncType.Int,
                    saved = true, defaultValue = initial + 1, hasExplicitDefaultValue = true
                });
                if (!AssetDatabase.IsValidFolder(Output)) AssetDatabase.CreateFolder("Assets", "FriendWardrobeGenerated");
                controllerPath = AssetDatabase.GenerateUniqueAssetPath(Output + "/OutfitSwitch.controller");
                var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                controller.AddParameter(new AnimatorControllerParameter
                    { name = SelectionParameter, type = AnimatorControllerParameterType.Int, defaultInt = initial + 1 });
                var layers = controller.layers;
                layers[0].name = "Friend Wardrobe";
                layers[0].defaultWeight = 1;
                controller.layers = layers;
                var machine = layers[0].stateMachine;
                var merge = root.AddComponent<ModularAvatarMergeAnimator>();
                merge.animator = controller;
                merge.pathMode = MergeAnimatorPathMode.Absolute;
                merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
                merge.matchAvatarWriteDefaults = true;
                var paths = items.Select(outfit => AnimationUtility.CalculateTransformPath(outfit.transform, target.transform)).ToArray();
                for (int i = 0; i < items.Count; i++)
                {
                    var choice = Child(root.transform, items[i].name);
                    var item = choice.AddComponent<ModularAvatarMenuItem>();
                    item.label = items[i].name;
                    item.automaticValue = false;
                    item.isDefault = i == initial;
                    item.Control = new VRCExpressionsMenu.Control
                    {
                        type = VRCExpressionsMenu.Control.ControlType.Toggle,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = SelectionParameter },
                        value = i + 1
                    };
                    var clip = new AnimationClip { name = "Outfit " + (i + 1) };
                    AssetDatabase.AddObjectToAsset(clip, controller);
                    for (int j = 0; j < items.Count; j++)
                        clip.SetCurve(paths[j], typeof(GameObject), "m_IsActive",
                            AnimationCurve.Constant(0, 1f / 60f, i == j ? 1 : 0));
                    var state = machine.AddState(clip.name);
                    state.motion = clip;
                    state.writeDefaultValues = false;
                    AddTransition(state, i + 1);
                    if (i == initial)
                    {
                        machine.defaultState = state;
                        AddTransition(state, 0);
                    }
                    Undo.RecordObject(items[i], "Set the default outfit");
                    items[i].SetActive(i == initial);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(items[i]);
                }
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
                Undo.CollapseUndoOperations(group);

                void AddTransition(AnimatorState state, int value)
                {
                    var transition = machine.AddAnyStateTransition(state);
                    transition.duration = 0;
                    transition.hasExitTime = false;
                    transition.canTransitionToSelf = false;
                    transition.AddCondition(AnimatorConditionMode.Equals, value, SelectionParameter);
                }
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                if (controllerPath != null) AssetDatabase.DeleteAsset(controllerPath);
                throw;
            }
        }

        private static bool IsGenerated(Transform root) => root && root.GetComponent<ModularAvatarMenuInstaller>() &&
            root.TryGetComponent<ModularAvatarMergeAnimator>(out var merge) && merge.animator is AnimatorController &&
            root.TryGetComponent<ModularAvatarParameters>(out var config) &&
            config.parameters.Any(parameter => parameter.nameOrPrefix == SelectionParameter);

        private static GameObject Child(Transform parent, string name)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(obj, "Create outfit menu");
            return obj;
        }
    }
}
