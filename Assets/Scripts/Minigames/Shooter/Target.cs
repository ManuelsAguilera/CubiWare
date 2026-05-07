using UnityEngine;
using ARcadeRush.Core;

namespace ARcadeRush.Minigames.Shooter
{
    public enum TargetType { Bandit, Innocent }

    public class Target : MonoBehaviour
    {
        [SerializeField] private TargetType _type = TargetType.Bandit;
        [SerializeField] private int _banditScore = 10;
        [SerializeField] private int _innocentScore = -20;
        [SerializeField] private ParticleSystem _hitEffect;

        public TargetType Type => _type;
        public bool IsAlive { get; private set; } = true;

        /// <summary>
        /// Called when a bullet/projectile hits this target.
        /// Awards/penalizes score via GameManager and destroys the target.
        /// </summary>
        public void OnHit()
        {
            if (!IsAlive) return;
            IsAlive = false;

            int points = _type == TargetType.Bandit ? _banditScore : _innocentScore;
            string label = _type == TargetType.Bandit ? "Bandit" : "Innocent";

            Debug.Log($"[Target] {label} hit! Score: {points}");

            if (GameManager.Instance != null)
            {
                GameManager.Instance.AddScore(points);
            }

            // Play hit effect if assigned
            if (_hitEffect != null)
            {
                Instantiate(_hitEffect, transform.position, Quaternion.identity);
            }

            // Destroy after a short delay to allow effects to show
            Destroy(gameObject, 0.1f);
        }
    }
}
