using System.Collections.Generic;
using NeonVoid.Plugins;
using NeonVoid.Progression.Save;
using UnityEngine;

namespace NeonVoid.Progression
{
    /// <summary>
    /// Hands the plugin director what the vaccine is 출전 소지 with, so a 의뢰 starts with what
    /// the player put in the hub rather than with a fixed prototype set.
    /// <para>
    /// <b>It registers itself rather than being found.</b> Plugins declares
    /// <see cref="IDeployedPluginSource"/> and must not reach into Progression.Save for the
    /// ids - the same direction <c>IMissionCommitGate</c> points. Registering in Awake means
    /// the director has the source before its own Start, with no execution order to get right
    /// beyond this component's own.
    /// </para>
    /// <para>
    /// <b>With no save there is no source.</b> The director then falls back to its prototype
    /// set, which is what a scene with no save is for. A source that answered "nothing" when it
    /// simply could not read would start every mission empty and look like a rule.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class DeployedLoadoutDirector : MonoBehaviour, IDeployedPluginSource
    {
        [Tooltip("Found automatically when left empty.")]
        [SerializeField] private VaccineSaveDirector saveDirector;

        [Tooltip("Found automatically when left empty.")]
        [SerializeField] private PluginProcDirector procs;

        private readonly List<string> _ids = new List<string>();

        /// <summary>Whether the save can be read at all.</summary>
        public bool Available => saveDirector != null && saveDirector.Store != null;

        /// <summary>
        /// The 출전 instance ids, read fresh each time. The snapshot changes under this whenever
        /// anything commits - a move in the hub, a death clearing the loadout - and a cached list
        /// would deploy what the player had before their last decision.
        /// </summary>
        public IReadOnlyList<string> DeployedInstanceIds
        {
            get
            {
                _ids.Clear();
                if (!Available)
                    return _ids;

                List<string> carried = saveDirector.Store.Snapshot.vaccine.carriedPluginInstanceIds;
                if (carried == null)
                    return _ids;

                for (int i = 0; i < carried.Count; ++i)
                    _ids.Add(carried[i]);

                return _ids;
            }
        }

        /// <summary>Wires it up without a scene, for a test.</summary>
        public void Configure(VaccineSaveDirector saves, PluginProcDirector director)
        {
            saveDirector = saves;
            procs = director;
            Register();
        }

        private void Awake()
        {
            if (saveDirector == null)
                saveDirector = FindFirstObjectByType<VaccineSaveDirector>();
            if (procs == null)
                procs = FindFirstObjectByType<PluginProcDirector>();

            Register();
        }

        private void Register()
        {
            if (procs == null)
            {
                Debug.LogWarning(
                    $"{name}: no plugin director, so 출전 소지 플러그인 will not reach the 의뢰.", this);
                return;
            }

            procs.UseDeployedSource(this);
        }
    }
}
