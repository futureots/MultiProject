using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CircleCollider2D))]
    public sealed class GrayBoxPlayer : NetworkBehaviour
    {
        private const float SkinWidth = .02f;

        [SerializeField] private SpriteRenderer body;
        [SerializeField, Min(0f)] private float speed = 4f;

        private readonly List<RaycastHit2D> movementHits = new(8);
        private readonly ContactFilter2D solidFilter = new() { useTriggers = false };
        private CircleCollider2D bodyCollider;
        private PlayerInputAction inputActions;
        private Vector2 moveInput;
        private GameFollowCamera followCamera;

        private bool HasLocalControl => OnStartClientCalled && IsOwner;

        private void Awake()
        {
            bodyCollider = GetComponent<CircleCollider2D>();
            inputActions = new PlayerInputAction();
            inputActions.Player.Move.performed += OnMoveChanged;
            inputActions.Player.Move.canceled += OnMoveChanged;
        }

        private void OnEnable() => RefreshInput();
        private void OnDisable() => DisableInput();

        private void OnDestroy()
        {
            inputActions.Dispose();
            inputActions = null;
        }

        public override void OnStartClient()
        {
            RefreshInput();
            if (!IsOwner)
                return;

            body.color = new Color32(200, 200, 200, 255);
            Camera mainCamera = Camera.main;
            if (mainCamera != null && mainCamera.TryGetComponent(out followCamera))
                followCamera.Track(transform);
        }

        public override void OnOwnershipClient(NetworkConnection previousOwner) => RefreshInput();

        public override void OnStopClient()
        {
            DisableInput();
            if (followCamera != null)
                followCamera.StopTracking(transform);
            followCamera = null;
        }

        private void RefreshInput()
        {
            if (isActiveAndEnabled && HasLocalControl)
            {
                inputActions.Player.Move.Enable();
                inputActions.UI.Point.Enable();
            }
            else
            {
                DisableInput();
            }
        }

        private void DisableInput()
        {
            if (inputActions != null)
            {
                inputActions.Player.Move.Disable();
                inputActions.UI.Point.Disable();
            }

            moveInput = Vector2.zero;
        }

        private void OnMoveChanged(InputAction.CallbackContext context)
        {
            moveInput = context.ReadValue<Vector2>();
        }

        private void FixedUpdate()
        {
            if (!HasLocalControl || moveInput == Vector2.zero)
                return;

            Vector2 step = moveInput.normalized * (speed * Time.fixedDeltaTime);
            Vector2 center = transform.TransformPoint(bodyCollider.offset);
            float radius = bodyCollider.bounds.extents.x;
            // Check each axis separately so the player can slide along a wall.
            float horizontal = Sweep(center, radius, Vector2.right, step.x);
            center.x += horizontal;
            float vertical = Sweep(center, radius, Vector2.up, step.y);
            transform.position += new Vector3(horizontal, vertical, 0f);
        }

        private void Update()
        {
            if (!HasLocalControl || followCamera == null)
                return;

            Vector2 point = inputActions.UI.Point.ReadValue<Vector2>();
            Vector3 worldPosition = followCamera.OutputCamera.ScreenToWorldPoint(point);
            Vector2 aimDirection = worldPosition - transform.position;
            if (aimDirection.sqrMagnitude > 0f)
            {
                float zRotation = Mathf.Atan2(aimDirection.y, aimDirection.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0f, 0f, zRotation);
            }
        }

        private float Sweep(Vector2 center, float radius, Vector2 axis, float step)
        {
            if (step == 0f)
                return 0f;

            float direction = Mathf.Sign(step);
            float distance = Mathf.Abs(step);
            Physics2D.CircleCast(center, radius, axis * direction, solidFilter, movementHits,
                distance + SkinWidth);

            foreach (RaycastHit2D hit in movementHits)
            {
                if (hit.collider.transform.IsChildOf(transform))
                    continue;

                distance = Mathf.Clamp(hit.distance - SkinWidth, 0f, distance);
                break;
            }

            return distance * direction;
        }
    }
}
