using UnityEngine;

namespace NeonVoid.Prototype
{
    /// <summary>Temporary visual projectile; replace with pooled, fixed-tick hit events at P1.</summary>
    [RequireComponent(typeof(SphereCollider))]
    public sealed class PrototypeProjectile : MonoBehaviour
    {
        public Vector3 direction;
        public float speed = 15f;
        public float damage = 1f;
        public float ttl = 3f;

        private void Update()
        {
            float step = speed * Time.deltaTime;
            Vector3 start = transform.position;
            // Avoid missing targets between frames (prototype hit check only).
            if (Physics.SphereCast(start, 0.13f, direction, out RaycastHit hit, step))
            {
                PrototypeDummyTarget target = hit.collider.GetComponent<PrototypeDummyTarget>();
                if (target != null)
                    target.ApplyPrototypeHit(damage);
                Destroy(gameObject);
                return;
            }
            transform.position += direction * step;
            ttl -= Time.deltaTime;
            if (ttl <= 0f)
                Destroy(gameObject);
        }
    }
}
