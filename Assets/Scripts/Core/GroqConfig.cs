using UnityEngine;

namespace ARcadeRush.Core
{
    [CreateAssetMenu(fileName = "GroqConfig", menuName = "ARcadeRush/GroqConfig")]
    public class GroqConfig : ScriptableObject
    {
        [SerializeField] private string _apiKey;
        public string ApiKey => _apiKey;
    }
}
