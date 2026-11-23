using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Gameplay
{
    public class FieldOfView : MonoBehaviour
    {
        public float viewRadius;
        [Range(0,360)]
        public float viewAngle;
        public float meshResolution;
        private Mesh _viewMesh;
        public MeshFilter viewMeshFilter;

        public LayerMask targetLayerMask,obstacleLayerMask;

        private void Start()
        {
            _viewMesh = new Mesh();
            _viewMesh.name = "ViewMesh";
            viewMeshFilter.mesh = _viewMesh;
            
            StartCoroutine(FindTargetWithDelay(0.5f));
        }

        IEnumerator FindTargetWithDelay(float delay)
        {
            while (true)
            {
                FindVisibleTargets();
                yield return new WaitForSeconds(delay);
            }
        }

        public List<Transform> visibleTargets = new();
        void FindVisibleTargets()
        {
            visibleTargets.Clear();
            Collider2D[] targetInViewRadius = Physics2D.OverlapCircleAll(transform.position, viewRadius, targetLayerMask);
            
            foreach (Collider2D target in targetInViewRadius)
            {
                if (target.gameObject == gameObject) continue;
                var dirToTarget = (target.transform.position - transform.position).normalized;
                if (Vector2.Angle(transform.right, dirToTarget) < viewAngle / 2)
                {
                    float dstToTarget = Vector2.Distance(transform.position, target.transform.position);

                    if (!Physics2D.Raycast(transform.position, dirToTarget, dstToTarget, obstacleLayerMask))
                    {
                        visibleTargets.Add(target.transform);
                    }
                }
            }
            Debug.Log(visibleTargets.Count);
        }
        
        public Vector2 DirFromAngle(float angleInDegrees, bool angleIsGlobal = true)
        {
            if (!angleIsGlobal)
            {
                angleInDegrees += transform.eulerAngles.z;
            }
            return new Vector2(Mathf.Cos(angleInDegrees * Mathf.Deg2Rad), Mathf.Sin(angleInDegrees * Mathf.Deg2Rad));
        }
        
        #region ViewMesh

        struct ViewCastInfo
        {
            public bool hit;
            public Vector2 point;
            public float dst;
            public float angle;
            public ViewCastInfo(bool hit, Vector3 point, float dst, float angle)
            {
                this.hit = hit;
                this.point = point;
                this.dst = dst;
                this.angle = angle;
            }
        }
        
        ViewCastInfo ViewCast(float globalAngle)
        {
            Vector3 dir = DirFromAngle(globalAngle);
            var hit = Physics2D.Raycast(transform.position, dir, viewRadius, obstacleLayerMask);
            if (hit)
            {
                return new ViewCastInfo(true, hit.point, hit.distance, globalAngle);
            }
            return new ViewCastInfo(false, transform.position + dir * viewRadius, viewRadius, globalAngle);
        }

        void DrawFieldOfView()
        {
            int stepCount = Mathf.RoundToInt(viewAngle * meshResolution);
            float stepAngleSize = viewAngle / stepCount;
            List<Vector3> viewPoints = new();

            for (int i = 0; i <= stepCount; i++)
            {
                float angle = transform.eulerAngles.z - viewAngle / 2 + stepAngleSize * i;
                var newView = ViewCast(angle);
                viewPoints.Add(newView.point);
            }
            
            int vertexCount = viewPoints.Count + 1;
            Vector3[] vertices = new Vector3[vertexCount];
            int[] triangles = new int[(vertexCount-2) * 3];
            vertices[0] = Vector3.zero;

            for (int i = 0; i < vertexCount - 1; i++)
            {
                vertices[i +1] = transform.InverseTransformPoint(viewPoints[i]);
                if (i < vertexCount - 2)
                {
                    triangles[i * 3] = 0;
                    triangles[i * 3 + 1] = i + 2;
                    triangles[i * 3 + 2] = i + 1;
                }
            }
            
            _viewMesh.Clear();
            _viewMesh.vertices = vertices;
            _viewMesh.triangles = triangles;
            _viewMesh.RecalculateNormals();
        }

        private void LateUpdate()
        {
            DrawFieldOfView();
        }

        #endregion
    }
}
