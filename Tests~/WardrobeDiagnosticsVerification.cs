using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using FriendAvatarTools.Wardrobe;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
public static class WardrobeDiagnosticsVerification
{
    static void Check(bool ok,string why) {if(!ok) throw new Exception(why);}
    public static void Run()
    {
        var missing=WardrobeDiagnostics.Build(null,new List<GameObject>());
        Check(missing.Contains("Hierarchy") && missing.Contains("1.1.0"),"No-avatar guidance");
        var avatar=new GameObject("PRIVATE_AVATAR_NAME").AddComponent<VRCAvatarDescriptor>();
        var a=GameObject.CreatePrimitive(PrimitiveType.Cube); a.name="PRIVATE_OUTFIT_NAME"; a.transform.SetParent(avatar.transform);
        var b=GameObject.CreatePrimitive(PrimitiveType.Cube); b.transform.SetParent(avatar.transform);
        var items=new[]{a,b};
        try
        {
            SimpleWardrobeWindow.Install(avatar,items,0);
            var report=WardrobeDiagnostics.Build(avatar,items);
            Check(report.Contains("[OK] MA Menu Installerは有効") && report.Contains("MA Merge AnimatorとControllerあり"),"Healthy installation");
            Check(!report.Contains("PRIVATE_") && !report.Contains(Application.dataPath),"Privacy regression");
            var root=avatar.transform.Find("かんたん衣装切替");
            root.GetComponent<ModularAvatarMenuInstaller>().enabled=false;
            root.gameObject.SetActive(false);
            report=WardrobeDiagnostics.Build(avatar,items);
            Check(report.Contains("MA Menu Installerを有効") && report.Contains("生成オブジェクトまたは親を有効"),"Disabled menu diagnosis");
            Check(WardrobeDiagnostics.Build(avatar,new GameObject[]{null,a}).Contains("空欄数: 1"),"Null outfit handling");
            // A malformed controller should not crash the window when loading the avatar.
            var merge=root.GetComponent<ModularAvatarMergeAnimator>();
            var controller=(UnityEditor.Animations.AnimatorController)merge.animator;
            controller.layers[0].stateMachine.states[0].state.motion=null;
            var window=ScriptableObject.CreateInstance<SimpleWardrobeWindow>();
            try
            {
                typeof(SimpleWardrobeWindow).GetMethod("LoadAvatar",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(window,new object[]{avatar});
                Check(typeof(SimpleWardrobeWindow).GetField("loadProblem",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(window)!=null,"Malformed-data guidance");
            }
            finally {UnityEngine.Object.DestroyImmediate(window);}
        }
        finally {UnityEngine.Object.DestroyImmediate(avatar.gameObject);}
        File.WriteAllText(Path.Combine(Application.dataPath,"../wardrobe-diagnostics-tests.txt"),"PASS: unselected avatar, healthy setup, disabled installer/root, empty outfit, redacted report, malformed saved controller recovery.");
    }
}
