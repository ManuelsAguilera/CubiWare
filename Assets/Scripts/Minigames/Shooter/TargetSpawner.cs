using System.Collections;
using UnityEngine;

namespace ARcadeRush.Minigames.Shooter
{
    /// <summary>
    /// Spawns targets (bandits and innocents) in 3 depth rows.
    /// Rows: Near (z=5), Mid (z=12), Far (z=20) in front of the camera.
    /// Each row spans horizontally from x=-6 to x=6.
    /// Configurable spawn interval and bandit/innocent ratio.
    /// Uses object pooling for performance.
    /// </summary>
    public class TargetSpawner : MonoBehaviour
    {
        [System.Serializable]
        public struct RowConfig
        {
            public string label;
            public float zPosition;
            public int maxTargets;
        }

        [Header("Row Definitions")]
        [SerializeField] private RowConfig[] _rows = new RowConfig[]
        {
            new RowConfig { label = "Near",  zPosition = 5f,  maxTargets = 3 },
            new RowConfig { label = "Mid",   zPosition = 12f, maxTargets = 4 },
            new RowConfig { label = "Far",   zPosition = 20f, maxTargets = 5 }
        };

        [Header("Spawning")]
        [SerializeField] private GameObject _banditPrefab;
        [SerializeField] private GameObject _innocentPrefab;
        [SerializeField] private float _spawnInterval = 2f;
        [SerializeField, Range(0f, 1f)] private float _banditRatio = 0.7f;
        [SerializeField] private float _xMin = -6f;
        [SerializeField] private float _xMax = 6f;
        [SerializeField] private float _yPosition = 2f;

        [Header("Object Pooling")]
        [SerializeField] private int _poolSize = 20;

        private Transform _poolRoot;
        private bool _isSpawning = false;

        private void Awake()
        {
            // Create pool root
            _poolRoot = new GameObject("TargetPool").transform;
            _poolRoot.SetParent(transform);

            // Pre-warm the pool
            for (int i = 0; i < _poolSize; i++)
            {
                GameObject bandit = CreatePooledObject(_banditPrefab);
                GameObject innocent = CreatePooledObject(_innocentPrefab);
                bandit.SetActive(false);
                innocent.SetActive(false);
            }
        }

        private GameObject CreatePooledObject(GameObject prefab)
        {
            if (prefab == null) return null;
            GameObject obj = Instantiate(prefab, _poolRoot);
            return obj;
        }

        private GameObject GetFromPool(GameObject prefab)
        {
            // Find an inactive pooled object of this type, or create a new one
            foreach (Transform child in _poolRoot)
            {
                if (!child.gameObject.activeSelf && child.name.StartsWith(prefab.name))
                {
                    return child.gameObject;
                }
            }

            // Pool exhausted — create a new one
            GameObject newObj = CreatePooledObject(prefab);
            return newObj;
        }

        /// <summary>Start spawning targets on a loop.</summary>
        public void StartSpawning()
        {
            if (_isSpawning) return;
            _isSpawning = true;
            StartCoroutine(CoSpawnLoop());
        }

        /// <summary>Stop spawning and clear all active targets.</summary>
        public void StopSpawning()
        {
            _isSpawning = false;
            StopAllCoroutines();
            ClearAllTargets();
        }

        private IEnumerator CoSpawnLoop()
        {
            while (_isSpawning)
            {
                SpawnTargetAtRandomRow();
                yield return new WaitForSeconds(_spawnInterval);
            }
        }

        private void SpawnTargetAtRandomRow()
        {
            RowConfig row = _rows[Random.Range(0, _rows.Length)];

            // Count how many targets are currently active in this row
            int activeInRow = 0;
            foreach (Transform child in _poolRoot)
            {
                if (child.gameObject.activeSelf && Mathf.Approximately(child.position.z, row.zPosition))
                {
                    activeInRow++;
                }
            }

            if (activeInRow >= row.maxTargets) return;

            // Decide bandit or innocent
            bool isBandit = Random.value < _banditRatio;
            GameObject prefab = isBandit ? _banditPrefab : _innocentPrefab;
            if (prefab == null)
            {
                Debug.LogWarning("[TargetSpawner] Prefab is null! Assign Bandit and Innocent prefabs.");
                return;
            }

            GameObject targetObj = GetFromPool(prefab);
            if (targetObj == null) return;

            // Position
            float xPos = Random.Range(_xMin, _xMax);
            targetObj.transform.position = new Vector3(xPos, _yPosition, row.zPosition);
            targetObj.transform.rotation = Quaternion.identity;
            targetObj.SetActive(true);

            // Ensure Target component is alive
            Target target = targetObj.GetComponent<Target>();
            if (target != null)
            {
                // Re-enable via reflection — private field, so just ensure it's a fresh instance
                // The Target component was reset when returned to pool
            }
        }

        private void ClearAllTargets()
        {
            foreach (Transform child in _poolRoot)
            {
                if (child.gameObject.activeSelf)
                {
                    child.gameObject.SetActive(false);
                }
            }
        }
    }
}
