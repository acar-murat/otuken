using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Otuken.EditorTools
{
    /// <summary>One-click project configuration and mobile builds, under the "Otuken" menu.</summary>
    public static class OtukenSetup
    {
        const string ScenePath = "Assets/Otuken/Otuken.unity";
        const string BundleId = "com.otuken.arena";

        /// <summary>
        /// Reads the semantic version (MAJOR.MINOR.PATCH) from the VERSION file at the project root.
        /// The integer build number used by the stores is MAJOR*10000 + MINOR*100 + PATCH.
        /// </summary>
        public static string ReadVersion(out int buildNumber)
        {
            string v = File.Exists("VERSION") ? File.ReadAllText("VERSION").Trim() : "0.0.0";
            var m = System.Text.RegularExpressions.Regex.Match(v, @"^(\d+)\.(\d+)\.(\d+)$");
            if (!m.Success) throw new System.Exception($"VERSION must look like 1.2.3, found '{v}'.");
            int major = int.Parse(m.Groups[1].Value), minor = int.Parse(m.Groups[2].Value), patch = int.Parse(m.Groups[3].Value);
            if (minor > 99 || patch > 99) throw new System.Exception("MINOR and PATCH must stay below 100 for the build number.");
            buildNumber = Mathf.Max(1, major * 10000 + minor * 100 + patch);
            return v;
        }

        [MenuItem("Otuken/Set Up Project", priority = 0)]
        public static void SetUp()
        {
            PlayerSettings.companyName = "Otuken";
            PlayerSettings.productName = "Ötüken";
            string version = ReadVersion(out int build);
            PlayerSettings.bundleVersion = version;
            PlayerSettings.Android.bundleVersionCode = build;
            PlayerSettings.iOS.buildNumber = build.ToString();
            Debug.Log($"Ötüken: version {version} (build {build}).");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BundleId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);

            // C# is compiled ahead of time to native ARM64 through IL2CPP (required by Google Play and iOS).
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // Everything is created from code at runtime with an empty scene, so the build cannot see
            // which engine parts are used. Keep engine code and make sure the shaders we look up ship.
            PlayerSettings.stripEngineCode = false;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Minimal);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.iOS, ManagedStrippingLevel.Minimal);

            EnsureScene();
            AlwaysInclude("Sprites/Default"); // ground rings and effect lines
            AlwaysInclude("Standard");        // fallback lit shader (built-in pipeline)
            AlwaysInclude("Legacy Shaders/Diffuse");
            AlwaysInclude("Universal Render Pipeline/Lit"); // only exists in URP projects
            AssetDatabase.SaveAssets();
            Debug.Log("Ötüken: project set up. Press Play to test, or use Otuken > Build Android APK.");
        }

        static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void AlwaysInclude(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) return;
            var settings = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            if (settings == null) return;
            var so = new SerializedObject(settings);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null) return;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            so.ApplyModifiedProperties();
        }

        [MenuItem("Otuken/Build Android APK (test on phone)", priority = 20)]
        public static void BuildApk()
        {
            SetUp();
            EditorUserBuildSettings.buildAppBundle = false;
            Build(BuildTarget.Android, "Builds/Android/Otuken.apk");
        }

        [MenuItem("Otuken/Build Android App Bundle (Google Play)", priority = 21)]
        public static void BuildAab()
        {
            SetUp();
            EditorUserBuildSettings.buildAppBundle = true;
            Build(BuildTarget.Android, "Builds/Android/Otuken.aab");
        }

        [MenuItem("Otuken/Build iOS Xcode Project (needs a Mac)", priority = 22)]
        public static void BuildIos()
        {
            SetUp();
            Build(BuildTarget.iOS, "Builds/iOS");
        }

        /// <summary>Entry point for cloud builds (GitHub Actions / GameCI): exits with 0 on success, 1 on failure.</summary>
        public static void BuildAndroidCi()
        {
            SetUp();
            EditorUserBuildSettings.buildAppBundle = false;
            EditorApplication.Exit(Build(BuildTarget.Android, "Builds/Android/Otuken.apk") ? 0 : 1);
        }

        public static void BuildIosCi()
        {
            SetUp();
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            EditorApplication.Exit(Build(BuildTarget.iOS, "Builds/iOS") ? 0 : 1);
        }

        /// <summary>Test build for the iOS Simulator on Apple-silicon Macs.</summary>
        public static void BuildIosSimCi()
        {
            SetUp();
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
            // Set by reflection so the editor script still compiles if this setting is renamed.
            var prop = typeof(PlayerSettings.iOS).GetProperty("simulatorSdkArchitecture");
            if (prop != null && prop.CanWrite) prop.SetValue(null, System.Enum.Parse(prop.PropertyType, "ARM64"));
            bool ok = Build(BuildTarget.iOS, "Builds/iOS-Sim");
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>Test APK that also runs on x86_64 Android emulators.</summary>
        public static void BuildAndroidTestCi()
        {
            SetUp();
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.X86_64;
            bool ok = Build(BuildTarget.Android, "Builds/Android/Otuken-test.apk");
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Build(BuildTarget target, string path)
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = path,
                target = target,
                options = BuildOptions.None,
            });
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"Ötüken: built {path} ({report.summary.totalSize / (1024 * 1024)} MB).");
                if (!Application.isBatchMode) EditorUtility.RevealInFinder(path);
                return true;
            }
            Debug.LogError($"Ötüken: build {report.summary.result}. The errors above in the Console say why.");
            return false;
        }
    }
}
