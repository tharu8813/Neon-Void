#if UNITY_EDITOR
using System.IO;
using NeonVoid.Combat;
using NeonVoid.Combat.AI;
using NeonVoid.Combat.Projectiles;
using NeonVoid.Combat.Skills;
using NeonVoid.Core;
using NeonVoid.Player;
using NeonVoid.Plugins;
using NeonVoid.Presentation;
using NeonVoid.Progression;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
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

        // A dialog waits for a click. In batch mode there is nobody to click, and when the CLI
        // drives a running Editor the click would have to come from whoever happens to be
        // sitting in front of it - so an automated run says so and asks nothing.
        private static bool _automated;

        /// <summary>
        /// Regenerates the scene without asking anything, for the CLI driving a live Editor.
        /// Overwrites the existing prototype scene, which is the point of calling this.
        /// </summary>
        public static void CreateSceneForAutomation()
        {
            _automated = true;
            try
            {
                CreateScene();
            }
            finally
            {
                _automated = false;
            }
        }

        private static bool AsksQuestions => !Application.isBatchMode && !_automated;
        private const string MaterialDirectory = "Assets/_NeonVoid/Content/PrototypeMaterials";

        [MenuItem("Tools/Neon Void/Generate Prototype Scene")]
        public static void CreateScene()
        {
            // EditorUtility.DisplayDialog returns false in batch mode, so asking there would
            // silently cancel the regeneration and leave the old scene in place while the
            // command still reported success. Only a human gets asked.
            if (AsksQuestions
                && File.Exists(ScenePath)
                && !EditorUtility.DisplayDialog(
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
            Material corpseMat = GetOrCreateMaterial("NV_Corpse", new Color(0.21f, 0.13f, 0.19f));
            Material fieldMat = GetOrCreateMaterial("NV_Field", new Color(0.95f, 0.55f, 0.15f));

            // docs/03 section 6: two or three prototype skills, as replaceable assets. Only
            // the files are written here; the references are loaded after the scene exists,
            // because opening a new scene unloads assets nothing in it refers to yet and the
            // references would quietly become null.
            string[] skillPaths = NeonVoidPrototypeSkills.CreateOrUpdateAll();

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
            // Every tick in the scene comes from this one clock. Two accumulators would drift.
            var simulation = new GameObject("Simulation Runner");
            simulation.AddComponent<SimulationRunner>();

            // P0-02: movement, dash and aim run on the deterministic 60 Hz simulation.
            PlayerInputRouter router = player.AddComponent<PlayerInputRouter>();
            PlayerSimulationDriver driver = player.AddComponent<PlayerSimulationDriver>();
            AssignInputAsset(router);
            AssignSerialized(driver, "inputRouter", router);

            // P1: the player is a real combat actor and shoots through the damage pipeline.
            CombatActor playerActor = player.AddComponent<CombatActor>();
            AutoAttacker attacker = player.AddComponent<AutoAttacker>();
            AssignSerialized(attacker, "projectileMaterial", playerMat);

            // [초안] prototype stats, set here so the skill rules are observable rather than
            // multiplied by zero: the starting block has no ability power, which would make
            // every magic skill deal its flat component only, and no omnivamp, which would
            // hide the 0.33 area efficiency the field exists to demonstrate.
            AssignSerializedFloat(playerActor, "abilityPower", 20f);
            AssignSerializedFloat(playerActor, "omnivampPercent", 15f);

            // P2: Q/E/R cast the three prototype skills through the tested cast rules.
            PlayerSkillCaster caster = player.AddComponent<PlayerSkillCaster>();
            AssignSerialized(caster, "inputRouter", router);
            AssignSerialized(caster, "motionDriver", driver);
            AssignSerialized(caster, "fieldMaterial", fieldMat);
            AssignSerializedObjectArray(caster, "slotSkills", NeonVoidPrototypeSkills.Load(skillPaths));

            // P2: plugin procs. Three of docs/03's 27 are granted at startup so the proc queue
            // has something to judge; the three-card reward screen that really grants them
            // needs the save commit behind it.
            PluginProcDirector procs = player.AddComponent<PluginProcDirector>();
            AssignSerialized(procs, "owner", playerActor);

            // The body stops colliding and looks dead. Without this a corpse stays a solid,
            // invisible wall that can corner the player and stop their shots.
            ActorDeathReaction playerDeath = player.AddComponent<ActorDeathReaction>();
            AssignSerialized(playerDeath, "corpseMaterial", corpseMat);

            CreateDummy("Dummy A", new Vector3(6f, 0.7f, 5f), enemyMat, corpseMat);
            CreateDummy("Dummy B", new Vector3(-7f, 0.7f, -5f), enemyMat, corpseMat);
            CreateDummy("Dummy C", new Vector3(8f, 0.7f, -3f), enemyMat, corpseMat);

            // The player's death ends the run. The director records it and commands a save
            // service; none exists yet, so the run honestly sits in AwaitingCommit and the
            // banner says so rather than showing invented losses.
            var deathObject = new GameObject("Run Death Director (Prototype)");
            RunDeathDirector director = deathObject.AddComponent<RunDeathDirector>();
            AssignSerialized(director, "playerActor", playerActor);
            PrototypeDeathBanner banner = deathObject.AddComponent<PrototypeDeathBanner>();
            AssignSerialized(banner, "deathDirector", director);

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
            TopDownLookaheadCamera follow = cameraObject.AddComponent<TopDownLookaheadCamera>();
            follow.SetTarget(player.transform);
            // The arena is 34 x 24 with walls at +/-17 and +/-12; keep the focus inside it.
            follow.SetBounds(new Vector2(-16f, -10f), new Vector2(16f, 10f));
            AssignSerialized(router, "aimCamera", camera);
            cameraObject.AddComponent<AudioListener>();

            // Added after the camera exists, because it needs one to put health over an actor's
            // head. The plugin effects have no VFX, health has no bar and statuses have no
            // icons, so without this the simulation is unobservable and therefore unverifiable.
            PrototypeCombatDebugOverlay overlay = deathObject.AddComponent<PrototypeCombatDebugOverlay>();
            AssignSerialized(overlay, "player", playerActor);
            AssignSerialized(overlay, "procDirector", procs);
            AssignSerialized(overlay, "runner", simulation.GetComponent<SimulationRunner>());
            AssignSerialized(overlay, "worldCamera", camera);

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
            if (AsksQuestions)
                EditorUtility.DisplayDialog("Neon Void prototype created",
                    "Graybox scene saved. This proves only the bootstrap. " +
                    "P0 must verify input, URP, 60 Hz game architecture and Windows play.", "OK");
            Debug.Log("Neon Void prototype generated: " + ScenePath);
        }

        private const string InputAssetPath = "Assets/_NeonVoid/Content/Input/NeonVoidControls.inputactions";

        /// <summary>
        /// Wires the generated action asset into the router. A missing asset is reported
        /// loudly: the scene would still open and still look correct, but nothing would
        /// respond to the keyboard, which is the exact failure this build step exists to
        /// prevent.
        /// </summary>
        private static void AssignInputAsset(PlayerInputRouter router)
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            if (asset == null)
            {
                Debug.LogError($"Input actions not found at {InputAssetPath}; the player will not respond to input.");
                return;
            }
            AssignSerialized(router, "actions", asset);
        }

        /// <summary>
        /// Sets a private [SerializeField] through SerializedObject so the value is written
        /// into the scene file. Assigning a backing field directly would not survive the save.
        /// </summary>
        private static void AssignSerialized(Object target, string fieldName, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"{target.GetType().Name} has no serialized field '{fieldName}'; scene wiring is incomplete.");
                return;
            }
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignSerializedInt(Object target, string fieldName, int value)
        {
            SerializedProperty property = FindField(target, fieldName);
            if (property == null)
                return;
            property.intValue = value;
            property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignSerializedFloat(Object target, string fieldName, float value)
        {
            SerializedProperty property = FindField(target, fieldName);
            if (property == null)
                return;
            property.floatValue = value;
            property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Fills a serialized array of object references.
        /// <para>
        /// The resize is applied and the property re-fetched before the elements are written.
        /// Setting <c>arraySize</c> and then writing into element properties fetched from the
        /// same handle silently produced an array of nulls: the scene saved with three empty
        /// skill slots, every log line was clean, and the only symptom was that nothing
        /// happened in play. Hence also the read-back below - a wiring step that can fail
        /// quietly is worse than one that fails loudly.
        /// </para>
        /// </summary>
        private static void AssignSerializedObjectArray(Object target, string fieldName, Object[] values)
        {
            int count = values != null ? values.Length : 0;
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"{target.GetType().Name} has no serialized field '{fieldName}'; scene wiring is incomplete.");
                return;
            }

            if (!property.isArray)
            {
                Debug.LogError($"{target.GetType().Name}.{fieldName} is not an array; scene wiring is incomplete.");
                return;
            }

            // An object that is not a saved asset has no guid to write, so the reference would
            // serialize as a null however correct it looks in memory.
            for (int i = 0; i < count; ++i)
                if (values[i] != null && !AssetDatabase.Contains(values[i]))
                    Debug.LogError(
                        $"{target.GetType().Name}.{fieldName}[{i}] was handed an object the AssetDatabase does not hold; the reference will be lost on save.");

            property.arraySize = count;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            serialized.Update();
            property = serialized.FindProperty(fieldName);
            for (int i = 0; i < count; ++i)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            serialized.Update();
            property = serialized.FindProperty(fieldName);
            for (int i = 0; i < count; ++i)
            {
                if (values[i] == null)
                {
                    Debug.LogError($"{target.GetType().Name}.{fieldName}[{i}] was handed nothing; the slot will be empty.");
                    continue;
                }

                if (property.GetArrayElementAtIndex(i).objectReferenceValue == null)
                    Debug.LogError(
                        $"{target.GetType().Name}.{fieldName}[{i}] did not take the reference to '{values[i].name}'; scene wiring is incomplete.");
            }
        }

        private static SerializedProperty FindField(Object target, string fieldName)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
                Debug.LogError($"{target.GetType().Name} has no serialized field '{fieldName}'; scene wiring is incomplete.");
            return property;
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

        private static GameObject CreateDummy(string name, Vector3 position, Material material, Material corpseMaterial)
        {
            GameObject dummy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dummy.name = name;
            dummy.transform.position = position;
            dummy.transform.localScale = Vector3.one * 1.4f;
            SetMaterial(dummy, material);

            // Swap the primitive's SphereCollider for a CharacterController: the agent moves
            // through one, and it is still a collider, so projectile sweeps hit it as before.
            Object.DestroyImmediate(dummy.GetComponent<SphereCollider>());
            CharacterController body = dummy.AddComponent<CharacterController>();
            body.center = Vector3.zero;
            body.height = 1f;
            body.radius = 0.5f;

            // A real combat actor on the hostile team, so the damage pipeline applies to it.
            CombatActor actor = dummy.AddComponent<CombatActor>();
            AssignSerializedInt(actor, "teamId", 1);
            AssignSerializedFloat(actor, "maxHealth", 40f);

            // [초안] resistances above zero, so mitigation exists at all. With both at 0 the
            // 100/(100+r) formula returns 1.0 for every hit ever landed in this project:
            // armour, penetration and armour reduction were all computing on nothing.
            AssignSerializedFloat(actor, "physicalArmor", 10f);
            AssignSerializedFloat(actor, "magicResistance", 5f);

            // P2: it chases and attacks. The brain only forms intent; the hit itself goes
            // through CombatActor.ReceiveDamage, the same entry point the player's shots use.
            dummy.AddComponent<EnemyAgent>();

            // Killing one must leave something visibly dead that no longer blocks the arena.
            // The body is parked rather than destroyed: what a kill drops is reward policy,
            // and docs/04 puts rewards behind an approved save commit.
            ActorDeathReaction death = dummy.AddComponent<ActorDeathReaction>();
            AssignSerialized(death, "corpseMaterial", corpseMaterial);
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
