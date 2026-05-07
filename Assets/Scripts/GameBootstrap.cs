using UnityEngine;

namespace MatchRogue
{
    public sealed class GameBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            if (FindObjectOfType<MatchRogueGame>() == null)
            {
                gameObject.AddComponent<MatchRogueGame>();
            }
        }
    }
}
