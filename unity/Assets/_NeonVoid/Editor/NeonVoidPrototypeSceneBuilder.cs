#if UNITY_EDITOR
using System.IO;
using NeonVoid.Combat;
using NeonVoid.Combat.AI;
using NeonVoid.Combat.Projectiles;
using NeonVoid.Combat.Skills;
using NeonVoid.Core;
using NeonVoid.Missions;
using NeonVoid.Player;
using NeonVoid.Plugins;
using NeonVoid.Presentation;
using NeonVoid.Progression;
using NeonVoid.Progression.Save;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using static NeonVoid.EditorTools.NeonVoidSceneWiring;

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

            PrototypePalette palette = PrototypePalette.Create();
            Material floor = palette.Floor;
            Material wall = palette.Wall;
            Material playerMat = palette.Player;
            Material corpseMat = palette.Corpse;
            Material enemyShotMat = palette.EnemyShot;
            Material enemyMat = GetOrCreateMaterial("NV_Dummy", new Color(1f, 0.23f, 0.42f));
            Material rangedMat = GetOrCreateMaterial("NV_Ranged", new Color(0.72f, 0.3f, 0.95f));
            Material chargerMat = GetOrCreateMaterial("NV_Charger", new Color(1f, 0.62f, 0.2f));
            Material supportMat = GetOrCreateMaterial("NV_Support", new Color(0.35f, 0.9f, 0.6f));

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

            // Every tick in the scene comes from this one clock. Two accumulators would drift.
            var simulation = new GameObject("Simulation Runner");
            simulation.AddComponent<SimulationRunner>();

            // The run's identity, which the death receipt is keyed on and the reward rolls are
            // seeded from. The run id now comes from permanent state through the save director
            // below; the run SNAPSHOT is still not saved, because docs/04's run fields are
            // undecided (D-017).
            var missionObject = new GameObject("Mission Run Director (Prototype)");
            missionObject.AddComponent<MissionRunDirector>();

            // P0-02 and P1-P2 in one object: deterministic motion, a real combat actor, the
            // three skill slots and the proc queue. Shared with the loop scene, which needs the
            // same vaccine standing in a hub instead of an arena.
            SkillAsset[] authored = NeonVoidPrototypeSkills.Load(skillPaths);
            VaccinePlayer vaccine = CreateVaccinePlayer(new Vector3(0f, 1f, 0f), palette, authored);
            GameObject player = vaccine.Object;
            PlayerInputRouter router = vaccine.Router;
            CombatActor playerActor = vaccine.Actor;
            PluginProcDirector procs = vaccine.Procs;

            // One of each implemented archetype, so the arena is not three copies of one enemy.
            // CLAUDE.md: the five hardware zones are not one map pasted five times, and that
            // starts here. The ranged one sits behind cover B, where its line of sight has
            // something to be blocked by.
            CreateDummy("Chaser A", new Vector3(6f, 0.7f, 5f), enemyMat, corpseMat,
                EnemyArchetypeId.Chaser, enemyShotMat);
            CreateDummy("Charger B", new Vector3(-7f, 0.7f, -5f), chargerMat, corpseMat,
                EnemyArchetypeId.Charger, enemyShotMat);
            CreateDummy("Ranged C", new Vector3(9f, 0.7f, -6f), rangedMat, corpseMat,
                EnemyArchetypeId.Ranged, enemyShotMat);
            // Well back, where reaching it costs the player something. Its 강화 reaches the
            // other three, so killing it first is a decision rather than an obvious move.
            CreateDummy("Support D", new Vector3(-12f, 0.7f, 8f), supportMat, corpseMat,
                EnemyArchetypeId.Support, enemyShotMat);

            // The player's death ends the run. The director records it and commands a save
            // service; the banner shows nothing until that service acknowledges a verified
            // write, so a failed save never reads as a settled loss.
            var deathObject = new GameObject("Run Death Director (Prototype)");
            RunDeathDirector director = deathObject.AddComponent<RunDeathDirector>();
            AssignSerialized(director, "playerActor", playerActor);
            PrototypeDeathBanner banner = deathObject.AddComponent<PrototypeDeathBanner>();
            AssignSerialized(banner, "deathDirector", director);

            // Permanent state, approved and connected: a death here spends a clone in the real
            // save under Application.persistentDataPath, and [확정] docs/04 section 2 says
            // 복제 회복은 어떤 수단으로도 불가. It allocates this run's id too, because a run id
            // from a serialized constant mints the same death receipt every launch and the save
            // would refuse every death after the first one. Turn off 'Write To Real Save
            // Directory' on this component to play without permanent consequences.
            var saveObject = new GameObject("Vaccine Save Director");
            VaccineSaveDirector saveDirector = saveObject.AddComponent<VaccineSaveDirector>();
            AssignSerialized(saveDirector, "deathDirector", director);

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
            AssignSerialized(overlay, "saveDirector", saveDirector);

            CreateDirectionalLight();

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

        private static GameObject CreateDummy(
            string name,
            Vector3 position,
            Material material,
            Material corpseMaterial,
            EnemyArchetypeId archetype,
            Material projectileMaterial)
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
            EnemyAgent agent = dummy.AddComponent<EnemyAgent>();
            AssignSerializedInt(agent, "archetypeId", (int)archetype);
            AssignSerialized(agent, "projectileMaterial", projectileMaterial);

            // Killing one must leave something visibly dead that no longer blocks the arena.
            // The body is parked rather than destroyed: what a kill drops is reward policy,
            // and docs/04 puts rewards behind an approved save commit.
            ActorDeathReaction death = dummy.AddComponent<ActorDeathReaction>();
            AssignSerialized(death, "corpseMaterial", corpseMaterial);
            return dummy;
        }

    }
}
#endif
