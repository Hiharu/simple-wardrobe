using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.PackageManager;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace FriendAvatarTools.Wardrobe
{
    public static class WardrobeDiagnostics
    {
        public const string Version = "1.1.0";
        public static string Build(VRCAvatarDescriptor avatar, IReadOnlyList<GameObject> outfits, string loadProblem = null)
        {
            var text = new StringBuilder();
            text.AppendLine("Simple Wardrobe " + Version + " 診断");
            text.AppendLine("UTC: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine("Unity: " + Application.unityVersion);
            text.AppendLine("Build Target: " + EditorUserBuildSettings.activeBuildTarget);
            text.AppendLine("Play Mode: " + EditorApplication.isPlayingOrWillChangePlaymode);
            text.AppendLine("Compiling: " + EditorApplication.isCompiling);
            text.AppendLine("[参考] 動作確認環境: Unity 2022.3.22f1 / SDK 3.10.5 / MA 1.18.7 / NDMF 1.14.8");
            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            foreach (var id in new[] { "com.vrchat.avatars", "com.vrchat.base", "nadena.dev.modular-avatar", "nadena.dev.ndmf" })
            {
                var p = packages.FirstOrDefault(x => x.name == id);
                text.AppendLine(id + ": " + (p == null ? "Package Manager登録なし（Assets導入の可能性あり）" : p.version));
            }
            text.AppendLine("[OK] SDK・MAの参照を解決して診断コードを実行できています。");
            if (!Application.unityVersion.StartsWith("2022.3.")) text.AppendLine("[注意] Unity 2022.3以外は未検証です。SDKの対応環境を確認してください。");
            if (EditorApplication.isPlayingOrWillChangePlaymode) text.AppendLine("[要対応] Play Modeを終了してから作成・更新してください。");
            if (!avatar || EditorUtility.IsPersistent(avatar) || !avatar.gameObject.scene.IsValid())
            {
                text.AppendLine("[要対応] HierarchyのVRC Avatar Descriptor付きアバターを指定してください。");
                return Finish(text);
            }
            text.AppendLine("[OK] シーン上のアバターを指定済み。");
            if (!avatar.gameObject.activeInHierarchy) text.AppendLine("[要対応] アバターまたは親が無効です。Hierarchyで有効にしてください。");
            var animator = avatar.GetComponent<Animator>();
            text.AppendLine("Animator: " + (!animator ? "なし（SDKの設定を確認）" : animator.isHuman ? "Humanoid" : "Generic/その他"));
            text.AppendLine("登録欄数: " + outfits.Count);
            text.AppendLine("空欄数: " + outfits.Count(x => !x));
            for (int i = 0; i < outfits.Count; i++)
            {
                var item = outfits[i];
                if (!item) continue;
                string label = "衣装欄" + (i + 1) + ": ";
                if (item == avatar.gameObject || !item.transform.IsChildOf(avatar.transform))
                { text.AppendLine("[要対応] " + label + "アバター配下の衣装を指定してください。"); continue; }
                if (!item.GetComponentInChildren<Renderer>(true)) text.AppendLine("[要対応] " + label + "Rendererがありません。");
                var path = AnimationUtility.CalculateTransformPath(item.transform, avatar.transform);
                if (avatar.transform.Find(path) != item.transform) text.AppendLine("[要対応] " + label + "階層名が重複し、一意に参照できません。");
                for (var parent = item.transform.parent; parent != avatar.transform; parent = parent.parent)
                    if (!parent.gameObject.activeSelf) { text.AppendLine("[要対応] " + label + "途中の親が無効です。"); break; }
                for (int j = 0; j < i; j++)
                    if (outfits[j] && (item == outfits[j] || item.transform.IsChildOf(outfits[j].transform) || outfits[j].transform.IsChildOf(item.transform)))
                        text.AppendLine("[要対応] " + label + "衣装欄" + (j + 1) + "と重複または親子関係です。");
            }
            // Validate may include user-defined outfit names. Do not put its raw message in the shareable report.
            bool valid;
            try { valid = SimpleWardrobeWindow.Validate(avatar, outfits) == null; }
            catch { valid = false; }
            text.AppendLine(valid ? "[OK] 衣装登録の事前条件を満たしています。" : "[要対応] 衣装登録条件を満たしていません。画面上部の警告を確認してください。");
            if (loadProblem != null) text.AppendLine("[要対応] 既存の登録データを読み込めませんでした。自動修復は未実施。");
            if (avatar.expressionParameters)
            {
                int cost = avatar.expressionParameters.CalcTotalCost();
                text.AppendLine("Descriptor直接設定の同期コスト: " + cost + "/" + VRCExpressionParameters.MAX_PARAMETER_COST);
            }
            else text.AppendLine("Descriptor直接設定のExpression Parameters: なし");
            text.AppendLine("[注意] 上記コストはMA統合前です。衣装切替は8bit使用。合計・重複・競合はSDKビルド後に確認してください。");
            int missing = avatar.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            text.AppendLine((missing == 0 ? "[OK] " : "[要対応] ") + "Missing Script: " + missing);
            var root = avatar.transform.Find("かんたん衣装切替");
            if (!root) text.AppendLine("[未導入] 生成オブジェクトがありません。衣装登録後に作成ボタンを押してください。");
            else
            {
                text.AppendLine(root.gameObject.activeInHierarchy ? "[OK] 生成オブジェクトは有効。" : "[要対応] 生成オブジェクトまたは親を有効にしてください。");
                var installer = root.GetComponent<ModularAvatarMenuInstaller>();
                text.AppendLine(!installer ? "[要対応] MA Menu Installerがありません。" : !installer.enabled ? "[要対応] MA Menu Installerを有効にしてください。" : "[OK] MA Menu Installerは有効。");
                if (installer)
                {
                    text.AppendLine("メニュー導入先: " + (installer.installTargetMenu ? "指定サブメニュー" : "ルート（既定）"));
                    if (installer.installTargetMenu && !Reachable(avatar.expressionsMenu, installer.installTargetMenu, new HashSet<VRCExpressionsMenu>()))
                        text.AppendLine("[注意] 指定先をDescriptorのメニューから確認できません。MA等で後から統合する場合もあるため、Select Menuとビルド結果を確認してください。");
                }
                var merge = root.GetComponent<ModularAvatarMergeAnimator>();
                text.AppendLine(merge && merge.enabled && merge.animator is AnimatorController ? "[OK] MA Merge AnimatorとControllerあり。" : "[要対応] MA Merge Animatorの有効状態とController参照を確認してください。");
                var config = root.GetComponent<ModularAvatarParameters>();
                text.AppendLine(config && config.parameters.Any(p => p.nameOrPrefix == "FriendWardrobe/Outfit") ? "[OK] 衣装選択パラメータあり。" : "[要対応] 衣装選択パラメータがありません。");
                text.AppendLine("生成ルートの子オブジェクト数: " + root.childCount);
            }
            return Finish(text);
        }
        private static bool Reachable(VRCExpressionsMenu menu, VRCExpressionsMenu target, HashSet<VRCExpressionsMenu> visited)
        {
            if (!menu || !visited.Add(menu)) return false;
            return menu == target || menu.controls.Any(c => c != null && c.type == VRCExpressionsMenu.Control.ControlType.SubMenu && Reachable(c.subMenu, target, visited));
        }
        private static string Finish(StringBuilder text)
        {
            text.AppendLine("[未確認] Consoleエラー本文・他ギミックの競合・ビルド後メニュー・VRChat実機。");
            text.AppendLine("次の操作: 警告を解消 → 作成/更新 → シーン保存 → SDK Build & Test → メニュー確認。既に公開済みなら再アップロード。");
            text.AppendLine("メニューはMAがビルド時に統合します。元のExpressions Menuだけでは表示確認できません。");
            text.AppendLine("共有時に追記: どの段階で止まるか / 期待した動作 / 実際の動作。");
            text.AppendLine("自動送信なし。アバター名・衣装名・アセットパス・ユーザー名・Blueprint ID・ログ本文は収集していません。");
            return text.ToString();
        }
    }
}
