// PCG Plugin - Runtime plugin entry point

using System;
using FlaxEngine;

namespace PCG.Runtime
{
    /// <summary>
    /// Runtime entry point for the PCG plugin.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Responsibility is deliberately small: register the node types, and drop every piece of cached state
    /// when scripts reload. Everything else lives in the components and nodes so it can be unit tested
    /// without an engine running.
    /// </para>
    /// <para>
    /// <b>Script reload.</b> Flax destroys all script instances on reload and unloads the assembly. Static
    /// tables that hold delegates into that assembly must be cleared, or the next call would be into types
    /// that no longer exist. The runtime side listens to <c>Level.ScriptsReload</c>; the editor side has its
    /// own richer set of events and is handled by <c>PCGEditorPlugin</c>.
    /// </para>
    /// </remarks>
    public class PCGRuntimePlugin : GamePlugin
    {
        private static PCGRuntimePlugin _instance;

        /// <summary>The active plugin instance, or null when the plugin is not loaded.</summary>
        public static PCGRuntimePlugin Instance => _instance;

        /// <summary>Creates the plugin and its description.</summary>
        public PCGRuntimePlugin()
        {
            _description = new PluginDescription
            {
                Name = "PCG",
                Category = "Procedural",
                Author = "HundunWorld",
                Description = "PCG - procedural content generation: point-based graph evaluation over terrain",
                Version = new Version(1, 0),
                IsAlpha = true,
                IsBeta = false,
            };
        }

        /// <inheritdoc />
        public override void Initialize()
        {
            _instance = this;

            // Register unconditionally: a player build and a headless server both need the node table so a
            // PCGVolume can run at runtime.
            int count = PCGNodeRegistration.RegisterAll();
            Debug.Log($"[PCG] runtime plugin initialized with {count} node type(s).");

            Level.ScriptsReload += OnScriptsReload;
            Level.ScriptsReloaded += OnScriptsReloaded;

            base.Initialize();
        }

        /// <inheritdoc />
        public override void Deinitialize()
        {
            Level.ScriptsReload -= OnScriptsReload;
            Level.ScriptsReloaded -= OnScriptsReloaded;

            ClearCachedState();

            _instance = null;
            Debug.Log("[PCG] runtime plugin unloaded.");
            base.Deinitialize();
        }

        private void OnScriptsReload()
        {
            // Runs before the assembly is swapped, so release everything holding references into it.
            Debug.Log("[PCG] scripts reload starting: clearing cached state.");
            ClearCachedState();
        }

        private void OnScriptsReloaded()
        {
            // Runs after the new assembly is live, so the node table can be rebuilt against it.
            int count = PCGNodeRegistration.RegisterAll();
            Debug.Log($"[PCG] scripts reloaded: re-registered {count} node type(s).");
        }

        /// <summary>
        /// Releases every piece of plugin static state.
        /// </summary>
        /// <remarks>
        /// Keep this exhaustive. Anything static that survives a reload while holding a type, delegate or
        /// asset reference is a latent failure that shows up later as a missing-method or stale-asset error
        /// far from its cause.
        /// </remarks>
        public static void ClearCachedState()
        {
            PCGNodeRegistration.UnregisterAll();
            TerrainSampling.PCGTerrainCalibrationStore.Clear();
        }
    }
}
