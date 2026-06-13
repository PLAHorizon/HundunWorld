using System;
using FlaxEngine;

namespace Narrative
{
    /// <summary>
    /// The sample game plugin.
    /// </summary>
    /// <seealso cref="FlaxEngine.GamePlugin" />
    public class Narrative : GamePlugin
    {
        /// <inheritdoc />
        public Narrative()
        {
            _description = new PluginDescription
            {
                Name = "Narrative",
                Category = "Other",
                Author = "成阳",
                AuthorUrl = null,
                HomepageUrl = null,
                RepositoryUrl = "https://github.com/FlaxEngine/Narrative",
                Description = "This is an example plugin project.",
                Version = new Version(),
                IsAlpha = false,
                IsBeta = false,
            };
        }

        /// <inheritdoc />
        public override void Initialize()
        {
            base.Initialize();

            Debug.Log("Hello from plugin code!");
        }

        /// <inheritdoc />
        public override void Deinitialize()
        {
            // Use it to cleanup data

            base.Deinitialize();
        }
    }
}
