using System.Collections.Generic;
using UnityEngine;
using CubiWare.Core.Logging;
 
namespace ARcadeRush.Minigames.Shooter
{
    [System.Serializable]
    public class RowConfig
    {
        [Tooltip("Must match the _rowLabel on Target GameObjects in this row.")]
        public string label;

        public ActivationMode mode;

        [Tooltip("Used when mode = Fixed — how many targets to activate per batch.")]
        public int fixedCount = 2;

        [Tooltip("Used when mode = Percentage (0.0 - 1.0).")]
        [Range(0f, 1f)]
        public float percentage = 0.5f;

        [Header("Scoring")]
        public int banditScore = 10;
        public int innocentScore = -10;

        [Header("Timing")]
        [Tooltip("How long each activated target stays active before auto-deactivating (seconds).")]
        public float activeDuration = 3f;

        [Tooltip("Minimum cooldown between activation batches for this row (seconds).")]
        public float activationCooldown = 2f;
    }

    public enum ActivationMode { Fixed, Percentage }

    /// <summary>
    /// Manages all pre-placed Target objects in the scene.
    /// - Scans for targets on Awake and groups them by _rowLabel.
    /// - Provides round-robin batch activation via ActivateBatch(rowLabel).
    /// - Each activated target auto-deactivates after its RowConfig.activeDuration.
    /// - Targets are recycled: after deactivation they become available for the next batch.
    /// </summary>
    public class TargetManager : MonoBehaviour
    {
        [SerializeField] private RowConfig[] _rowConfigs;

        [Tooltip("Optional parent transform to scan for Target children. If null, searches the entire scene.")]
        [SerializeField] private Transform _targetParent;

        // Row label → list of targets in that row
        private Dictionary<string, List<Target>> _targetsByRow;

        // Row label → round-robin index (last activated position)
        private Dictionary<string, int> _roundRobinIndex;

        // Row label → last activation time (for cooldown)
        private Dictionary<string, float> _lastActivationTime;

        // Fast lookup: RowConfig by label
        private Dictionary<string, RowConfig> _configByLabel;

        private const string LogServiceName = "TargetManager";

        private void Awake()
        {
            DiscoverTargets();
        }

        /// <summary>
        /// (Re-)scans the scene for all Target components and groups them by row label.
        /// Call once at startup; safe to call again if targets change at runtime.
        /// </summary>
        public void DiscoverTargets()
        {
            Target[] allTargets;

            if (_targetParent != null)
            {
                allTargets = _targetParent.GetComponentsInChildren<Target>(true);
            }
            else
            {
                allTargets = FindObjectsByType<Target>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            }

            _targetsByRow = new Dictionary<string, List<Target>>();
            _roundRobinIndex = new Dictionary<string, int>();
            _lastActivationTime = new Dictionary<string, float>();
            _configByLabel = new Dictionary<string, RowConfig>();

            // Build config lookup
            if (_rowConfigs != null)
            {
                foreach (var cfg in _rowConfigs)
                {
                    if (!_configByLabel.ContainsKey(cfg.label))
                        _configByLabel.Add(cfg.label, cfg);
                }
            }

            // Group targets by row label
            foreach (var target in allTargets)
            {
                string label = target.RowLabel;
                if (string.IsNullOrEmpty(label)) continue;

                if (!_targetsByRow.ContainsKey(label))
                {
                    _targetsByRow.Add(label, new List<Target>());
                    _roundRobinIndex.Add(label, 0);
                    _lastActivationTime.Add(label, 0f);
                }

                _targetsByRow[label].Add(target);
            }

            // Auto-assign sequential IDs and sort each row
            foreach (var kvp in _targetsByRow)
            {
                var targets = kvp.Value;
                for (int i = 0; i < targets.Count; i++)
                {
                    targets[i].AssignId(i);
                }
                // Sort by the newly assigned ID for consistent ordering
                targets.Sort((a, b) => a.TargetId.CompareTo(b.TargetId));
            }
        }

        // --- Public API ---

        /// <summary>
        /// Activate the next batch of targets from the specified row.
        /// Uses round-robin selection and respects the row's activation cooldown.
        /// Returns false if the row label is unknown, on cooldown, or all targets are already active.
        /// </summary>
        public bool ActivateBatch(string rowLabel)
        {
            // Validate
            if (!_targetsByRow.ContainsKey(rowLabel))
            {
                ServiceLogger.Instance.LogWarning(LogServiceName, $"Unknown row label: '{rowLabel}'");
                return false;
            }

            if (!_configByLabel.ContainsKey(rowLabel))
            {
                ServiceLogger.Instance.LogWarning(LogServiceName, $"No RowConfig for label: '{rowLabel}'");
                return false;
            }

            // Cooldown check
            float now = Time.time;
            float lastTime = _lastActivationTime[rowLabel];
            float cooldown = _configByLabel[rowLabel].activationCooldown;
            if (now - lastTime < cooldown)
            {
                return false; // Still on cooldown
            }

            RowConfig config = _configByLabel[rowLabel];
            List<Target> allTargets = _targetsByRow[rowLabel];

            // Get inactive targets
            List<Target> inactive = new List<Target>();
            foreach (var t in allTargets)
            {
                if (!t.IsActive) inactive.Add(t);
            }

            if (inactive.Count == 0)
            {
                return false;
            }

            // Calculate how many to activate
            int countToActivate;
            if (config.mode == ActivationMode.Fixed)
            {
                countToActivate = Mathf.Min(config.fixedCount, inactive.Count);
            }
            else // Percentage
            {
                countToActivate = Mathf.Max(1, Mathf.CeilToInt(allTargets.Count * config.percentage));
                countToActivate = Mathf.Min(countToActivate, inactive.Count);
            }

            // Round-robin: pick starting from the last index
            int startIndex = _roundRobinIndex[rowLabel] % inactive.Count;

            for (int i = 0; i < countToActivate; i++)
            {
                int idx = (startIndex + i) % inactive.Count;
                Target target = inactive[idx];

                target.Activate(config.banditScore, config.innocentScore, config.activeDuration);
                target.OnTargetDeactivated += HandleTargetDeactivated;
            }

            // Advance round-robin index
            _roundRobinIndex[rowLabel] = (startIndex + countToActivate) % allTargets.Count;

            // Record activation time
            _lastActivationTime[rowLabel] = now;

            return true;
        }

        /// <summary>
        /// Returns all currently active targets in the given row.
        /// </summary>
        public Target[] GetActiveTargets(string rowLabel)
        {
            if (!_targetsByRow.ContainsKey(rowLabel))
                return System.Array.Empty<Target>();

            var active = new List<Target>();
            foreach (var t in _targetsByRow[rowLabel])
            {
                if (t.IsActive) active.Add(t);
            }
            return active.ToArray();
        }

        /// <summary>
        /// How many inactive targets remain in the given row.
        /// </summary>
        public int GetAvailableCount(string rowLabel)
        {
            if (!_targetsByRow.ContainsKey(rowLabel))
                return 0;

            int count = 0;
            foreach (var t in _targetsByRow[rowLabel])
            {
                if (!t.IsActive) count++;
            }
            return count;
        }

        /// <summary>
        /// Total number of targets in the given row (active + inactive).
        /// </summary>
        public int GetTotalCount(string rowLabel)
        {
            return _targetsByRow.TryGetValue(rowLabel, out var list) ? list.Count : 0;
        }

        /// <summary>
        /// Deactivate a specific target (called when target finishes its deactivation sequence).
        /// </summary>
        public void DeactivateTarget(Target target)
        {
            target.OnTargetDeactivated -= HandleTargetDeactivated;
            target.Deactivate();
        }

        /// <summary>
        /// Deactivate all active targets across all rows.
        /// </summary>
        public void DeactivateAll()
        {
            foreach (var kvp in _targetsByRow)
            {
                foreach (var target in kvp.Value)
                {
                    if (target.IsActive)
                    {
                        target.OnTargetDeactivated -= HandleTargetDeactivated;
                        target.Deactivate();
                    }
                }
            }
        }

        // --- Event Handler ---

        private void HandleTargetDeactivated(Target target)
        {
            target.OnTargetDeactivated -= HandleTargetDeactivated;
            // Target is now available for reactivation — no further action needed
        }
    }
}
