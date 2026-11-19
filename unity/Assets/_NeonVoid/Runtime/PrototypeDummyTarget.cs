using UnityEngine;

namespace NeonVoid.Prototype
{
    /// <summary>Only the graybox dummy's temporary hitpoints, not a production stat system.</summary>
    public sealed class PrototypeDummyTarget : MonoBehaviour
    {
        [SerializeField] private float hitPoints = 10f;

        public void ApplyPrototypeHit(float amount)
        {
            hitPoints -= Mathf.Max(0f, amount);
            if (hitPoints <= 0f)
                Destroy(gameObject);
        }
    }
}
