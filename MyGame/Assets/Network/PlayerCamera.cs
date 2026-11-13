using UnityEngine;

namespace MyGame.Network
{
    /// <summary>
    /// Camera đuổi theo sau lưng người chơi của mình (góc nhìn thứ ba), thay cho camera nhìn thẳng xuống từ trên cao.
    /// Xoay: giữ chuột phải kéo, hoặc phím Q / E. Zoom: lăn chuột. Có tránh vật cản (bục) bằng cách kéo camera vào gần.
    /// Di chuyển (WASD) tính theo hướng camera: xem <see cref="Yaw"/> trong Player.Gather.
    /// Trước khi có nhân vật (đang kết nối), camera quay quanh khu vực bục để có cái nhìn tổng quan.
    /// </summary>
    public sealed class PlayerCamera : MonoBehaviour
    {
        [SerializeField] private float distance = 7f;
        [SerializeField] private float minDistance = 3f;
        [SerializeField] private float maxDistance = 14f;
        [SerializeField] private float lookHeight = 1.4f;
        [SerializeField] private float mouseSensitivity = 3f;
        [SerializeField] private float keyTurnSpeed = 110f;
        [SerializeField] private float followSharpness = 12f;

        private static PlayerCamera active;

        private Transform target;
        private float yaw;
        private float pitch = 22f;
        private float idleSince;

        /// <summary>Hướng nhìn ngang của camera (độ); client dùng để xoay đầu vào di chuyển. 0 khi chưa có camera.</summary>
        public static float Yaw => active != null ? active.yaw : 0f;

        private void OnEnable()
        {
            active = this;
            idleSince = Time.unscaledTime;
        }

        private void OnDisable()
        {
            if (active == this)
            {
                active = null;
            }
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                target = FindOwnPlayer();
            }

            ReadLookInput();

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pivot;
            float arm;
            if (target != null)
            {
                pivot = target.position + Vector3.up * (lookHeight - 1f);
                arm = distance;
            }
            else
            {
                // Chưa có nhân vật: quay chậm quanh tâm sàn để nhìn bao quát các bục.
                yaw += 6f * Time.unscaledDeltaTime;
                pivot = new Vector3(0f, 8f, 0f);
                arm = 26f;
            }

            Vector3 desired = pivot - rotation * Vector3.forward * arm;
            if (target != null && Physics.Linecast(pivot, desired, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
            {
                desired = hit.point + hit.normal * 0.25f;
            }

            float blend = target != null ? 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime) : 1f;
            transform.position = Vector3.Lerp(transform.position, desired, blend);
            transform.rotation = Quaternion.LookRotation(pivot - transform.position, Vector3.up);

            if (target == null && Time.unscaledTime - idleSince > 8f && FindOwnPlayer() == null)
            {
                idleSince = float.PositiveInfinity;
                Debug.Log("[Camera] have not seen my character after 8 seconds (not connected to a pod or not spawned yet).");
            }
        }

        private void ReadLookInput()
        {
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, -5f, 70f);
            }

            if (Input.GetKey(KeyCode.Q))
            {
                yaw -= keyTurnSpeed * Time.unscaledDeltaTime;
            }

            if (Input.GetKey(KeyCode.E))
            {
                yaw += keyTurnSpeed * Time.unscaledDeltaTime;
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                distance = Mathf.Clamp(distance - scroll, minDistance, maxDistance);
            }
        }

        private static Transform FindOwnPlayer()
        {
            foreach (Player player in FindObjectsByType<Player>(FindObjectsInactive.Exclude))
            {
                if (player.NetworkObject != null && player.NetworkObject.IsOwner)
                {
                    return player.transform;
                }
            }

            return null;
        }
    }
}
