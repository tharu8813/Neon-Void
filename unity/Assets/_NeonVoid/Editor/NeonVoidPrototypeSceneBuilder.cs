#if UNITY_EDITOR
using System.IO;
using NeonVoid.Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NeonVoid.EditorTools
{
    /// <summary>
    /// Safe, intentionally tiny graybox scene bootstrapper.
    /// This is the Unity integration smoke test; it is NOT the game world.
    /// </summary>
    public static class NeonVoidPrototypeSceneBuilder
    {
        private const string ScenePath = "Assets/_NeonVoid/Scenes/Prototype_Bootstrap.unity";
        private const string MaterialDirectory = "Assets/_NeonVoid/Content/PrototypeMaterials";

        [MenuItem("Tools/Neon Void/Generate Prototype Scene")]
        public static void CreateScene()
        {
            if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog(
                    "Neon Void prototype exists",
                    "This will overwrite the existing PROTOTYPE scene. Continue?",
                    "Overwrite prototype", "Cancel"))
                return;

            Directory.CreateDirectory("Assets/_NeonVoid/Scenes");
            Directory.CreateDirectory(MaterialDirectory);
            AssetDatabase.Refresh();

            Material floor = GetOrCreateMaterial("NV_Floor", new Color(0.065f, 0.09f, 0.16f));
            Material wall = GetOrCreateMaterial("NV_Wall", new Color(0.17f, 0.27f, 0.37f));
            Material playerMat = GetOrCreateMaterial("NV_Player", new Color(0.3f, 0.95f, 0.96f));
            Material enemyMat = GetOrCreateMaterial("NV_Dummy", new Color(1f, 0.23f, 0.42f));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateBox("Ground - XZ", new Vector3(0f, -0.25f, 0f),
                new Vector3(34f, 0.5f, 24f), floor);
            CreateBox("Wall North", new Vector3(0f, 1f, 12f), new Vector3(34f, 2f, 0.6f), wall);
            CreateBox("Wall South", new Vector3(0f, 1f, -12f), new Vector3(34f, 2f, 0.6f), wall);
            CreateBox("Wall East", new Vector3(17f, 1f, 0f), new Vector3(0.6f, 2f, 24f), wall);
            CreateBox("Wall West", new Vector3(-17f, 1f, 0f), new Vector3(0.6f, 2f, 24f), wall);
            CreateBox("Cover A", new Vector3(-5f, 0.75f, 3f), new Vector3(3f, 1.5f, 1f), wall);
            CreateBox("Cover B", new Vector3(5f, 0.75f, -4f), new Vector3(2f, 1.5f, 2f), wall);

            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Vaccine Player (Prototype)";
            player.transform.position = new Vector3(0f, 1f, 0f);
            SetMaterial(player, playerMat);
            CapsuleCollider temporaryCollider = player.GetComponent<CapsuleCollider>();
            Object.DestroyImmediate(temporaryCollider);
            CharacterController character = player.AddComponent<CharacterController>();
            character.center = Vector3.zero;
            character.height = 2f;
            character.radius = 0.45f;
            player.AddComponent<PrototypePlayerController>();
            player.AddComponent<PrototypeAutoShooter>();

            CreateDummy("Dummy A", new Vector3(6f, 0.7f, 5f), enemyMat);
            CreateDummy("Dummy B", new Vector3(-7f, 0.7f, -5f), enemyMat);
            CreateDummy("Dummy C", new Vector3(8f, 0.7f, -3f), enemyMat);

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 22f, 0f);
            cameraObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 7.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.015f, 0.02f, 0.045f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
            PrototypeCameraFollow follow = cameraObject.AddComponent<PrototypeCameraFollow>();
            follow.player = player.transform;
            cameraObject.AddComponent<AudioListener>();

            GameObject lightObject = new GameObject("Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.68f, 0.85f, 1f);
            light.intensity = 1.5f;

            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (!saved)
            {
                Debug.LogError("Neon Void prototype scene could not be saved.");
                return;
            }
            EditorUtility.DisplayDialog("Neon Void prototype created",
                "Graybox scene saved. This proves only the bootstrap. " +
                "P0 must verify input, URP, 60 Hz game architecture and Windows play.", "OK");
            Debug.Log("Neon Void prototype generated: " + ScenePath);
        }

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = MaterialDirectory + "/" + name + ".mat";
            Material asset = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (asset != null)
                return asset;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            Material material = new Material(shader);
            material.name = name;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static GameObject CreateBox(string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            SetMaterial(go, material);
            return go;
        }

        private static GameObject CreateDummy(string name, Vector3 position, Material material)
        {
            GameObject dummy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dummy.name = name;
            dummy.transform.position = position;
            dummy.transform.localScale = Vector3.one * 1.4f;
            SetMaterial(dummy, material);
            dummy.AddComponent<PrototypeDummyTarget>();
            return dummy;
        }

        private static void SetMaterial(GameObject obj, Material material)
        {
            Renderer renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
        }
    }
}
#endif
