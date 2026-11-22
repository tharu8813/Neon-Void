#if UNITY_EDITOR
using System.IO;
using NeonVoid.Combat.Skills;
using NeonVoid.Core;
using NeonVoid.Missions;
using NeonVoid.Player;
using NeonVoid.Presentation;
using NeonVoid.Progression;
using NeonVoid.Progression.Save;
using NeonVoid.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using static NeonVoid.EditorTools.NeonVoidSceneWiring;

namespace NeonVoid.EditorTools
{
    /// <summary>
    /// The scene you press Play on to walk the loop: 허브 → 의뢰 시작 → 메인보드 → 하드웨어 구역
    /// → 목표 → 보스 → 정화 → 철수 → 허브.
    /// <para>
    /// Separate from <see cref="NeonVoidPrototypeSceneBuilder"/>, which builds the combat arena
    /// with four dummies in it. That scene is the P1-P2 smoke test and every bit of it is still
    /// wanted; this one has no dummies and no walls, because its enemies and its rooms are
    /// generated from the mission's seed when the player walks into a portal.
    /// </para>
    /// <para>
    /// <b>Why a scene at all, when there are PlayMode tests for every piece.</b> Because the
    /// tests drive the loop through code: they stand the player on a portal with a teleport and
    /// kill a boss with a lethal damage request. docs/07 asks P3 to prove
    /// 실제로 CPU 1개 클리어→철수→재실행 성장 확인 and 8~12분 목표 실측, and neither is something a
    /// test can answer - they need a person with a keyboard. Until this scene existed there was
    /// nothing for that person to open.
    /// </para>
    /// <para>
    /// <b>Prototype.</b> The hub is a disc with four posts, the motherboard a disc with five,
    /// and the areas are grey boxes. No panels, no art, no audio. What it is for is the loop
    /// running under a Play button.
    /// </para>
    /// </summary>
    public static class NeonVoidLoopSceneBuilder
    {
        private const string ScenePath = "Assets/_NeonVoid/Scenes/Prototype_Loop.unity";

        /// <summary>
        /// [초안] how far the motherboard stands from the hub. Far enough that neither disc's
        /// floor is in the other's way; the player is moved between them rather than walking,
        /// because 메인보드는 길찾기 게임이 아니다 (docs/00 section 9).
        /// </summary>
        private static readonly Vector3 MotherboardAt = new Vector3(0f, 0f, 120f);

        private static bool _automated;

        private static bool AsksQuestions => !Application.isBatchMode && !_automated;

        /// <summary>Regenerates the scene without asking anything, for the CLI.</summary>
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

        [MenuItem("Tools/Neon Void/Generate Loop Scene")]
        public static void CreateScene()
        {
            // DisplayDialog returns false in batch mode, so asking there would silently cancel
            // the regeneration and leave the old scene in place while still reporting success.
            if (AsksQuestions
                && File.Exists(ScenePath)
                && !EditorUtility.DisplayDialog(
                    "Neon Void loop scene exists",
                    "This will overwrite the existing PROTOTYPE loop scene. Continue?",
                    "Overwrite", "Cancel"))
                return;

            Directory.CreateDirectory("Assets/_NeonVoid/Scenes");
            Directory.CreateDirectory(MaterialDirectory);
            AssetDatabase.Refresh();

            PrototypePalette palette = PrototypePalette.Create();

            // Written before the scene is opened, because opening a new scene unloads assets
            // nothing in it refers to yet and the references would quietly become null.
            string[] skillPaths = NeonVoidPrototypeSkills.CreateOrUpdateAll();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 키 바인딩 first: it reads 전역 설정 and pushes the player's keys onto the action
            // asset, and the input router reads that asset when it wakes.
            var bindingObject = new GameObject("Input Binding Director");
            var bindings = bindingObject.AddComponent<NeonVoid.Player.InputBindingDirector>();
            NeonVoidSceneWiring.AssignInputAsset(bindings);

            // One clock. Two accumulators would drift.
            var simulation = new GameObject("Simulation Runner");
            SimulationRunner runner = simulation.AddComponent<SimulationRunner>();

            // Permanent state first, so the mission director finds it while resolving its commit
            // gate and its run id. 'Write To Real Save Directory' is off by default, so nothing
            // here spends a clone until someone turns it on: [확정] docs/04 section 2 makes
            // 복제 회복 impossible, and a session lost to writes being off can be played again.
            // The HUD states which way it is set, so the choice is never silent.
            var saveObject = new GameObject("Vaccine Save Director");
            VaccineSaveDirector saveDirector = saveObject.AddComponent<VaccineSaveDirector>();

            // The mission does NOT begin here: [확정] docs/00's loop starts in the hub, and a
            // mission begun on Awake is a run the player never asked for. The 의뢰 터미널 begins
            // it, and MissionSeries gives each one its own run id and seed.
            var missionObject = new GameObject("Mission Run Director");
            MissionRunDirector mission = missionObject.AddComponent<MissionRunDirector>();
            AssignSerializedBool(mission, "beginMissionOnAwake", false);

            SkillAsset[] authored = NeonVoidPrototypeSkills.Load(skillPaths);

            // [확정] D-009: the pool the 스킬 장착 터미널 offers. The caster holds three; this
            // holds all seven, because a terminal that could only see the equipped ones would
            // have nothing to swap to.
            var libraryObject = new GameObject("Skill Library");
            var library = libraryObject.AddComponent<SkillLibrary>();
            library.Configure(authored);
            VaccinePlayer vaccine = CreateVaccinePlayer(
                new Vector3(0f, AreaWorldBuilder.PlayerStandingLiftUnits, 0f), palette, authored);

            // The death, and the banner that shows nothing until a verified write is
            // acknowledged - so a failed save never reads as a settled loss.
            var deathObject = new GameObject("Run Death Director");
            RunDeathDirector deaths = deathObject.AddComponent<RunDeathDirector>();
            AssignSerialized(deaths, "playerActor", vaccine.Actor);
            PrototypeDeathBanner banner = deathObject.AddComponent<PrototypeDeathBanner>();
            AssignSerialized(banner, "deathDirector", deaths);
            AssignSerialized(saveDirector, "deathDirector", deaths);

            // 백신 성장: 영구 능력치 포인트 투자와 무료 재분배, through the same transaction
            // everything else commits through. On the save object because it is the save it
            // reads and writes.
            VaccineGrowthDirector growth = saveObject.AddComponent<VaccineGrowthDirector>();
            AssignSerialized(growth, "playerActor", vaccine.Actor);
            AssignSerialized(growth, "saveDirector", saveDirector);

            // 플러그인 데이터베이스: 보관 DB ↔ 출전 이동. Same object, same reason - it reads and
            // writes the save, and a move is a commit like any other.
            var database = saveObject.AddComponent<NeonVoid.Progression.PluginDatabaseDirector>();
            AssignSerialized(database, "saveDirector", saveDirector);

            // 출전 소지: what the player put in the 출전 list is what the 의뢰 starts with.
            // Without it the hub's database terminal moves cards that change nothing.
            var deployed = saveObject.AddComponent<NeonVoid.Progression.DeployedLoadoutDirector>();
            AssignSerialized(deployed, "saveDirector", saveDirector);
            AssignSerialized(deployed, "procs", vaccine.Actor.GetComponent<NeonVoid.Plugins.PluginProcDirector>());

            // 메인보드: five portals and the builder that puts an area up behind one of them.
            var boardObject = new GameObject("Motherboard");
            boardObject.transform.position = MotherboardAt;
            AreaWorldBuilder builder = boardObject.AddComponent<AreaWorldBuilder>();
            MotherboardDirector motherboard = boardObject.AddComponent<MotherboardDirector>();
            AssignSerialized(motherboard, "mission", mission);
            AssignSerialized(motherboard, "builder", builder);
            AssignSerialized(motherboard, "player", vaccine.Object.transform);

            // 디펜더 허브: where the player starts and where every mission ends.
            var hubObject = new GameObject("Defender Hub");
            HubRuntime hub = hubObject.AddComponent<HubRuntime>();
            AssignSerialized(hub, "mission", mission);
            AssignSerialized(hub, "motherboard", motherboard);
            AssignSerialized(hub, "player", vaccine.Object.transform);
            AssignSerialized(hub, "input", vaccine.Router);
            AssignSerialized(hub, "growth", growth);

            Camera camera = CreateCamera(vaccine);
            AssignSerialized(vaccine.Router, "aimCamera", camera);

            // Added after the camera exists, because it needs one to put health over an actor's
            // head. Without it the loop is unobservable: which phase the mission is in decides
            // what every terminal and portal will do, and none of that is visible in the world.
            PrototypeCombatDebugOverlay overlay = deathObject.AddComponent<PrototypeCombatDebugOverlay>();
            AssignSerialized(overlay, "player", vaccine.Actor);
            AssignSerialized(overlay, "procDirector", vaccine.Procs);
            AssignSerialized(overlay, "runner", runner);
            AssignSerialized(overlay, "worldCamera", camera);
            AssignSerialized(overlay, "saveDirector", saveDirector);
            AssignSerialized(overlay, "mission", mission);
            AssignSerialized(overlay, "hub", hub);
            AssignSerialized(overlay, "motherboard", motherboard);

            // The 백신 성장 터미널's screen. Drawn only while the player is standing at it:
            // docs/00 makes the hub a place you walk around, not a menu you open anywhere.
            PrototypeGrowthPanel panel = deathObject.AddComponent<PrototypeGrowthPanel>();
            AssignSerialized(panel, "hub", hub);

            // The real HUD. docs/06 puts UI Toolkit first, and the combat HUD's framework is
            // to be chosen 실제 검증 후 - which needs a HUD to verify.
            // Sound. [초안] docs/05 section 6; the clips are synthesized placeholders until a
            // licensed set is recorded in third_party.
            var audioObject = new GameObject("Game Audio");
            var audio = audioObject.AddComponent<NeonVoid.Presentation.GameAudio>();
            audio.Configure(bindings, vaccine.Actor);

            // 보상 플러그인. [확정] docs/01 ties a 보상 화면 to a 정화, and the flow raises it
            // after the purification is committed.
            var rewardObject = new GameObject("Reward Director");
            var rewards = rewardObject.AddComponent<NeonVoid.Plugins.RewardDirector>();
            rewards.Configure(mission, motherboard, vaccine.Procs);

            BuildHud(
                vaccine.Actor, motherboard, hub, saveDirector, deaths, bindings, audio, rewards,
                library, vaccine.Caster);

            CreateDirectionalLight();

            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (!saved)
            {
                Debug.LogError("Neon Void loop scene could not be saved.");
                return;
            }

            if (AsksQuestions)
            {
                EditorUtility.DisplayDialog(
                    "Neon Void loop scene created",
                    "허브에서 시작합니다. 의뢰 터미널로 걸어가 상호작용(F)하면 의뢰가 시작되고 " +
                    "메인보드로 이동합니다. 포털에 1.5초 서면 그 하드웨어 구역이 생성됩니다.\n\n" +
                    "This proves nothing by itself. P3 asks for 실제로 CPU 1개 클리어→철수→재실행 " +
                    "성장 확인 and an 8~12분 measurement, and both need a person playing it.",
                    "OK");
            }

            Debug.Log("Neon Void loop scene generated: " + ScenePath);
        }

        /// <summary>
        /// The top-down camera, with its focus bounds opened up.
        /// <para>
        /// The arena scene clamps the camera to its own 34x24 room. This scene's player teleports
        /// 120 units to the motherboard and several hundred more into an area, so the same clamp
        /// would leave the camera looking at an empty hub while the player fought somewhere off
        /// screen. The bounds are kept rather than removed because the component has them; they
        /// are simply wider than anything this scene builds.
        /// </para>
        /// </summary>
        private static Camera CreateCamera(in VaccinePlayer vaccine)
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.position = new Vector3(0f, 22f, 0f);
            cameraObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 11f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.015f, 0.02f, 0.045f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;

            TopDownLookaheadCamera follow = cameraObject.AddComponent<TopDownLookaheadCamera>();
            follow.SetTarget(vaccine.Object.transform);
            follow.SetBounds(new Vector2(-4000f, -4000f), new Vector2(4000f, 4000f));

            cameraObject.AddComponent<AudioListener>();
            return camera;
        }
    
        /// <summary>
        /// The UI Toolkit HUD: a UIDocument with the project's PanelSettings and the
        /// controller that fills it from the simulation.
        /// </summary>
        private static void BuildHud(
            NeonVoid.Combat.CombatActor player,
            MotherboardDirector board,
            HubRuntime hub,
            VaccineSaveDirector save,
            RunDeathDirector deaths,
            NeonVoid.Player.InputBindingDirector bindings,
            NeonVoid.Presentation.GameAudio audio,
            NeonVoid.Plugins.RewardDirector rewards,
            SkillLibrary library,
            PlayerSkillCaster caster)
        {
            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/_NeonVoid/Runtime/UI/NeonVoidHud.uxml");
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                "Assets/_NeonVoid/UI/NeonVoidPanelSettings.asset");

            if (asset == null || settings == null)
            {
                Debug.LogError(
                    "Neon Void HUD assets are missing, so the loop scene has no HUD. " +
                    "Expected NeonVoidHud.uxml and NeonVoidPanelSettings.asset.");
                return;
            }

            var host = new GameObject("HUD");
            host.SetActive(false);

            UIDocument document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            document.visualTreeAsset = asset;

            HudController controller = host.AddComponent<HudController>();
            // Wired rather than found, so the notice that says whether this session is being
            // written down cannot be the thing that silently went missing.
            controller.Configure(
                player, board, null, hub, save, deaths, bindings, audio, rewards, library, caster);

            // 설정 is opened with a key and fed one, which is the only part that needs a real
            // keyboard. docs/05 section 2: 설정/오류 복구.
            var settingsInput = host.AddComponent<NeonVoid.UI.SettingsScreenInput>();
            settingsInput.Configure(controller, bindings != null ? bindings.Actions : null);

            host.SetActive(true);
        }
    }
}
#endif
