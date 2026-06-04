using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using CubiWare.Core.Logging;
using ARcadeRush.Core;

namespace CubiWare.Core
{
    /// <summary>
    /// Static registry that maps minigame scene names to their corresponding IMiniGame types.
    /// Provides build-index lookup by scene name and auto-registers known minigames
    /// in the static constructor.
    ///
    /// This decouples UI code (e.g., MainMenuController) from hardcoded build indices
    /// and makes minigame scene references discoverable by type.
    /// </summary>
    public static class MiniGameRegistry
    {
        /// <summary>
        /// Maps a scene name (e.g., "Shooter") to its IMiniGame implementing Type.
        /// </summary>
        private static readonly Dictionary<string, Type> _registry = new Dictionary<string, Type>();

        /// <summary>
        /// Maps a friendly scene name to its full scene path in the build settings.
        /// Used by <see cref="GetSceneIndex"/> to resolve build indices.
        /// </summary>
        private static readonly Dictionary<string, string> _scenePaths = new Dictionary<string, string>
        {
            { "Bootstrap", "Assets/Scenes/Bootstrap.unity" },
            { "MainMenu", "Assets/Scenes/MainMenu.unity" },
            { "DummyTest", "Assets/Scenes/DummyTest.unity" },
            { "Shooter", "Assets/Scenes/Shooter.unity" },
            { "FruitNinja", "Assets/Scenes/FruitNinja.unity" },
            { "EmotionTest", "Assets/Scenes/EmotionTest.unity" },
            { "Simon", "Assets/Scenes/Simon.unity" },
            { "MainMenuNinjaFruit", "Assets/Scenes/MainMenuNinjaFruit.unity" },
            { "Director", "Assets/Scenes/Director.unity" }
        };

        private static readonly ServiceLogger _logger = ServiceLogger.Instance;

        /// <summary>
        /// Static constructor: auto-registers all known minigames.
        /// Each registration is wrapped in a try-catch because the assembly
        /// may not be loadable in all configurations.
        /// </summary>
        static MiniGameRegistry()
        {
            _logger.LogInfo("MiniGameRegistry", "Static constructor: auto-registering known minigames.");

            // Register known minigames — wrapped in try-catch for assembly load safety
            TryRegister("Shooter", "ARcadeRush.Minigames.Shooter.ShooterGame");
            TryRegister("FruitNinja", "GameManagerFruit");
            TryRegister("EmotionTest", null);        // Placeholder — no implementation yet
            TryRegister("Simon", "ARcadeRush.Minigames.Simon.SimonGame");
            TryRegister("Director", "ARcadeRush.Minigames.SceneDirector.DirectorGame");

            _logger.LogInfo("MiniGameRegistry", $"Auto-registration complete. {_registry.Count} minigames registered.");
        }

        /// <summary>
        /// Attempts to resolve a type by its full assembly-qualified name and register it.
        /// Returns true if the type was found and registered; false otherwise.
        /// </summary>
        private static bool TryRegister(string sceneName, string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                _logger.LogInfo("MiniGameRegistry", $"Skipping registration for '{sceneName}' — no type specified (placeholder).");
                return false;
            }

            try
            {
                Type type = Type.GetType(typeName);
                if (type != null && typeof(IMiniGame).IsAssignableFrom(type))
                {
                    _registry[sceneName] = type;
                    _logger.LogInfo("MiniGameRegistry", $"Registered '{sceneName}' → {typeName}");
                    return true;
                }

                if (type == null)
                {
                    _logger.LogWarning("MiniGameRegistry", $"Type '{typeName}' could not be resolved (assembly not loaded). Skipping '{sceneName}'.");
                }
                else
                {
                    _logger.LogWarning("MiniGameRegistry", $"Type '{typeName}' does not implement IMiniGame. Skipping '{sceneName}'.");
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("MiniGameRegistry",
                    $"Failed to register '{sceneName}' with type '{typeName}': {ex.Message}");
                return false;
            }
        }

        // ── Public API ─────────────────────────────────────────────────────

        /// <summary>
        /// Registers a minigame type for a given scene name.
        /// </summary>
        /// <typeparam name="T">The type implementing <see cref="IMiniGame"/>.</typeparam>
        /// <param name="sceneName">The scene name to associate with this minigame.</param>
        public static void Register<T>(string sceneName) where T : IMiniGame
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                _logger.LogError("MiniGameRegistry", "Cannot register minigame with null or empty scene name.",
                    ServiceErrorCode.MinigameInitFailed);
                return;
            }

            Type type = typeof(T);
            _registry[sceneName] = type;
            _logger.LogInfo("MiniGameRegistry", $"Registered '{sceneName}' → {type.FullName}");
        }

        /// <summary>
        /// Gets the IMiniGame type registered for the given scene name.
        /// </summary>
        /// <param name="sceneName">The scene name to look up.</param>
        /// <returns>The registered Type, or null if not found.</returns>
        public static Type GetMinigameType(string sceneName)
        {
            if (_registry.TryGetValue(sceneName, out Type type))
            {
                return type;
            }

            _logger.LogWarning("MiniGameRegistry", $"No minigame type registered for scene '{sceneName}'.");
            return null;
        }

        /// <summary>
        /// Gets the build index for a scene by its name.
        /// Uses <see cref="SceneUtility.GetBuildIndexByScenePath"/> with the known scene path.
        /// </summary>
        /// <param name="sceneName">The friendly scene name (e.g., "Shooter", "MainMenu").</param>
        /// <returns>The build index, or -1 if the scene is not in the build settings.</returns>
        public static int GetSceneIndex(string sceneName)
        {
            if (_scenePaths.TryGetValue(sceneName, out string scenePath))
            {
                int index = SceneUtility.GetBuildIndexByScenePath(scenePath);
                if (index >= 0)
                {
                    return index;
                }

                _logger.LogError("MiniGameRegistry",
                    $"Scene path '{scenePath}' for '{sceneName}' is not in Build Settings.",
                    ServiceErrorCode.SceneNotFound);
                return -1;
            }

            _logger.LogError("MiniGameRegistry",
                $"No scene path registered for '{sceneName}'. Add it to _scenePaths.",
                ServiceErrorCode.SceneNotFound);
            return -1;
        }
    }
}
