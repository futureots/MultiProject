using Unity.Cinemachine;
using UnityEngine;

namespace Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class GameFollowCamera : MonoBehaviour
    {
        [SerializeField] private CinemachineCamera playerCamera;
        [SerializeField] private CinemachineCamera overviewCamera;
        [SerializeField] private Collider2D mapBounds;

        private Camera outputCamera;
        public Camera OutputCamera => outputCamera;

        private void Awake()
        {
            outputCamera = GetComponent<Camera>();
        }

        internal void Track(Transform player)
        {
            playerCamera.Follow = player;
            playerCamera.enabled = true;
        }

        internal void StopTracking(Transform player)
        {
            if (playerCamera == null || playerCamera.Follow != player)
                return;

            playerCamera.enabled = false;
            playerCamera.Follow = null;
        }

        private void LateUpdate()
        {
            if (playerCamera.enabled)
                return;

            Vector2 halfSize = mapBounds.bounds.extents;
            float size = Mathf.Max(halfSize.y, halfSize.x / outputCamera.aspect) + 1f;
            LensSettings lens = overviewCamera.Lens;
            if (!Mathf.Approximately(lens.OrthographicSize, size))
            {
                lens.OrthographicSize = size;
                overviewCamera.Lens = lens;
            }
        }
    }
}
