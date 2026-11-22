#if UNITY_EDITOR
using NeonVoid.Combat;
using NeonVoid.Combat.Projectiles;
using NeonVoid.Combat.Skills;
using NeonVoid.Core;
using NeonVoid.Player;
using NeonVoid.Plugins;
using NeonVoid.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonVoid.EditorTools
{
    /// <summary>
    /// The pieces every generated prototype scene is built from: serialized-field wiring,
    /// materials, boxes and the vaccine the player drives.
    /// <para>
    /// Extracted so there can be more than one generated scene without a second copy of this.
    /// The combat arena and the 허브→메인보드→구역 loop need the same player, the same input
    /// asset and the same way of writing a private <c>[SerializeField]</c> into a scene file,
    /// and a duplicate of that wiring would drift from the original the first time either
    /// changed.
    /// </para>
    /// </summary>
    internal static class NeonVoidSceneWiring
    {
        internal const string MaterialDirectory = "Assets/_NeonVoid/Content/PrototypeMaterials";

        internal const string InputAssetPath =
            "Assets/_NeonVoid/Content/Input/NeonVoidControls.inputactions";

        /// <summary>What a generated scene needs to keep hold of after building the player.</summary>
        internal struct VaccinePlayer
        {
            internal GameObject Object;
            internal PlayerInputRouter Router;
            internal PlayerSimulationDriver Driver;
            internal CombatActor Actor;
            internal PluginProcDirector Procs;
        }

        /// <summary>The materials a generated scene paints with, made once and shared.</summary>
        internal struct PrototypePalette
        {
            internal Material Floor;
            internal Material Wall;
            internal Material Player;
            internal Material Corpse;
            internal Material Field;
            internal Material EnemyShot;

            internal static PrototypePalette Create() => new PrototypePalette
            {
                Floor = GetOrCreateMaterial("NV_Floor", new Color(0.065f, 0.09f, 0.16f)),
                Wall = GetOrCreateMaterial("NV_Wall", new Color(0.17f, 0.27f, 0.37f)),
                Player = GetOrCreateMaterial("NV_Player", new Color(0.3f, 0.95f, 0.96f)),
                Corpse = GetOrCreateMaterial("NV_Corpse", new Color(0.21f, 0.13f, 0.19f)),
                Field = GetOrCreateMaterial("NV_Field", new Color(0.95f, 0.55f, 0.15f)),
                EnemyShot = GetOrCreateMaterial("NV_EnemyShot", new Color(1f, 0.45f, 0.75f)),
            };
        }

        /// <summary>
        /// The player: a capsule with a CharacterController, the deterministic motion driver,
        /// a real combat actor, auto-attack, the three skill slots and the proc director.
        /// <para>
        /// [초안] prototype stats are set here so the rules are observable rather than
        /// multiplied by zero - the starting block has no ability power, which would make every
        /// magic skill deal its flat component only, and no omnivamp, which would hide the 0.33
        /// area efficiency the field exists to demonstrate. Crit chance is above zero so the
        /// roll has something to decide.
        /// </para>
        /// </summary>
        internal static VaccinePlayer CreateVaccinePlayer(
            Vector3 at, in PrototypePalette palette, SkillAsset[] authoredSkills)
        {
            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Vaccine Player (Prototype)";
            player.transform.position = at;
            SetMaterial(player, palette.Player);

            Object.DestroyImmediate(player.GetComponent<CapsuleCollider>());
            CharacterController character = player.AddComponent<CharacterController>();
            character.center = Vector3.zero;
            character.height = 2f;
            character.radius = 0.45f;

            PlayerInputRouter router = player.AddComponent<PlayerInputRouter>();
            PlayerSimulationDriver driver = player.AddComponent<PlayerSimulationDriver>();
            AssignInputAsset(router);
            AssignSerialized(driver, "inputRouter", router);

            CombatActor actor = player.AddComponent<CombatActor>();
            AutoAttacker attacker = player.AddComponent<AutoAttacker>();
            AssignSerialized(attacker, "projectileMaterial", palette.Player);

            AssignSerializedFloat(actor, "abilityPower", 20f);
            AssignSerializedFloat(actor, "omnivampPercent", 15f);
            AssignSerializedFloat(actor, "critChancePercent", 20f);

            PlayerSkillCaster caster = player.AddComponent<PlayerSkillCaster>();
            AssignSerialized(caster, "inputRouter", router);
            AssignSerialized(caster, "motionDriver", driver);
            AssignSerialized(caster, "fieldMaterial", palette.Field);

            // Three slots, however many assets exist: the rest are authored and sitting in the
            // project for a slot to be pointed at, which is how docs/00 describes skills
            // changing.
            int slots = authoredSkills != null
                ? Mathf.Min(DesignConstants.ActiveSkillSlots, authoredSkills.Length)
                : 0;
            var equipped = new SkillAsset[slots];
            if (slots > 0)
                System.Array.Copy(authoredSkills, equipped, slots);
            AssignSerializedObjectArray(caster, "slotSkills", equipped);

            PluginProcDirector procs = player.AddComponent<PluginProcDirector>();
            AssignSerialized(procs, "owner", actor);

            // The body stops colliding and looks dead. Without this a corpse stays a solid,
            // invisible wall that can corner the player and stop their shots.
            ActorDeathReaction death = player.AddComponent<ActorDeathReaction>();
            AssignSerialized(death, "corpseMaterial", palette.Corpse);

            return new VaccinePlayer
            {
                Object = player,
                Router = router,
                Driver = driver,
                Actor = actor,
                Procs = procs,
            };
        }

        /// <summary>
        /// Wires the generated action asset into the router. A missing asset is reported
        /// loudly: the scene would still open and still look correct, but nothing would respond
        /// to the keyboard, which is the exact failure this build step exists to prevent.
        /// </summary>
        internal static void AssignInputAsset(Component reader)
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            if (asset == null)
            {
                Debug.LogError(
                    $"Input actions not found at {InputAssetPath}; the player will not respond to input.");
                return;
            }

            AssignSerialized(reader, "actions", asset);
        }

        /// <summary>
        /// Sets a private [SerializeField] through SerializedObject so the value is written
        /// into the scene file. Assigning a backing field directly would not survive the save.
        /// </summary>
        internal static void AssignSerialized(Object target, string fieldName, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError(
                    $"{target.GetType().Name} has no serialized field '{fieldName}'; scene wiring is incomplete.");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void AssignSerializedInt(Object target, string fieldName, int value)
        {
            SerializedProperty property = FindField(target, fieldName);
            if (property == null)
                return;
            property.intValue = value;
            property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void AssignSerializedFloat(Object target, string fieldName, float value)
        {
            SerializedProperty property = FindField(target, fieldName);
            if (property == null)
                return;
            property.floatValue = value;
            property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void AssignSerializedBool(Object target, string fieldName, bool value)
        {
            SerializedProperty property = FindField(target, fieldName);
            if (property == null)
                return;
            property.boolValue = value;
            property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void AssignSerializedVector3(Object target, string fieldName, Vector3 value)
        {
            SerializedProperty property = FindField(target, fieldName);
            if (property == null)
                return;
            property.vector3Value = value;
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
        internal static void AssignSerializedObjectArray(Object target, string fieldName, Object[] values)
        {
            int count = values != null ? values.Length : 0;
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError(
                    $"{target.GetType().Name} has no serialized field '{fieldName}'; scene wiring is incomplete.");
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
            {
                if (values[i] != null && !AssetDatabase.Contains(values[i]))
                {
                    Debug.LogError(
                        $"{target.GetType().Name}.{fieldName}[{i}] was handed an object the AssetDatabase " +
                        "does not hold; the reference will be lost on save.");
                }
            }

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
                {
                    Debug.LogError(
                        $"{target.GetType().Name}.{fieldName}[{i}] did not take the reference to " +
                        $"'{values[i].name}'; scene wiring is incomplete.");
                }
            }
        }

        internal static SerializedProperty FindField(Object target, string fieldName)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
                Debug.LogError($"{target.GetType().Name} has no serialized field '{fieldName}'; scene wiring is incomplete.");
            return property;
        }

        internal static Material GetOrCreateMaterial(string name, Color color)
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

            var material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        internal static GameObject CreateBox(string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            SetMaterial(go, material);
            return go;
        }

        internal static void SetMaterial(GameObject obj, Material material)
        {
            Renderer renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
        }

        /// <summary>
        /// A directional light, so a generated scene is not lit by ambient alone.
        /// </summary>
        internal static void CreateDirectionalLight()
        {
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.68f, 0.85f, 1f);
            light.intensity = 1.5f;
        }
    }
}
#endif
