using UnityEngine;

namespace MyGame.Network
{
    public sealed class PlayerCamera : MonoBehaviour
    {
        [SerializeField] private Vector3 offset = new Vector3(0f, 14f, -14f);

        private Transform target;

        private void LateUpdate()
        {
            if (target == null)
            {
                foreach (Player player in FindObjectsByType<Player>(FindObjectsInactive.Exclude))
                {
                    if (player.NetworkObject != null && player.NetworkObject.IsOwner)
                    {
                        target = player.transform;
                        break;
                    }
                }
            }

            if (target == null)
            {
                return;
            }

            transform.position = target.position + offset;
            transform.LookAt(target.position + Vector3.up * 0.5f);
        }
    }
}
