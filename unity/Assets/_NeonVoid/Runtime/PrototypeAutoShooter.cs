using UnityEngine;

namespace NeonVoid.Prototype
{
    /// <summary>
    /// Fires automatically in the player's mouse-facing direction, irrespective of target.
    /// Demo DPS/timing is NOT a finalized design rule.
    /// </summary>
    public sealed class PrototypeAutoShooter : MonoBehaviour
    {
        [SerializeField] private float secondsBetweenShots = 0.5f;
        private float _timer;

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f)
                return;
            _timer = secondsBetweenShots;

            Vector3 direction = transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f)
                return;
            direction.Normalize();
            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "Prototype Shot";
            orb.transform.position = transform.position + direction * 1.0f;
            orb.transform.localScale = Vector3.one * 0.26f;
            SphereCollider col = orb.GetComponent<SphereCollider>();
            col.isTrigger = true;
            orb.AddComponent<PrototypeProjectile>().direction = direction;
        }
    }
}
