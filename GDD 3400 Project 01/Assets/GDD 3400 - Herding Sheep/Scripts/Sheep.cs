using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace GDD3400.Project01
{
    [SelectionBase]
    [RequireComponent(typeof(Rigidbody))]
    public class Sheep : MonoBehaviour
    {        
        private Rigidbody _rb;
        private Level _level;

        private bool _inSafeZone = false;
        public bool InSafeZone => _inSafeZone;

        private bool _isActive = true;
        public bool IsActive 
        {
            get => _isActive;
            set => _isActive = value;
        }

        // Layers - Set In Project Settings
        private LayerMask _targetsLayer;

        // Tags - Set In Project Settings
        private const string _friendTag = "Friend";
        private const string _threatTag = "Threat";
        private const string _safeZoneTag = "SafeZone";

        // Movement Settings
        [NonSerialized] private float _stoppingDistance = 1.5f;
        [NonSerialized] private float _flockingDistance = 3.5f;
        [NonSerialized] private float _wanderSpeed = .5f;
        [NonSerialized] private float _walkSpeed = 2.5f;
        [NonSerialized] private float _runSpeed = 5f;
        [NonSerialized] private float _turnRate = 5f;

        // Perception Settings
        [NonSerialized] private float _sightRadius = 7.5f;

        // Dynamic Movement Variables
        private Vector3 _velocity;
        private float _targetSpeed;
        private Vector3 _target;
        private Vector3 _floatingTarget;
        private Collider[] _tmpTargets = new Collider[16]; // Maximum of 16 targets in each perception check

        private Collider _threatTarget;
        private Collider _safeZoneTarget;
        private List<Collider> _friendTargets = new List<Collider>();

        public void Awake()
        {
            // Find the layers in the project settings
            _targetsLayer = LayerMask.GetMask("Targets");

            _rb = GetComponent<Rigidbody>();
        }

        public void Initialize(Level level, int index)
        {
            this.name = $"Sheep {index}";

            this._level = level;

            // Randomize the sheep's forward direction
            transform.forward = new Vector3(UnityEngine.Random.Range(-1f, 1f), 0, UnityEngine.Random.Range(-1f, 1f));
        }

        public void Start()
        {
            // For sheep that are already in the scene, they will not have a level assigned, so we need to assign it here
            if (_level == null)
            {
                _level = Level.Instance;
            }

            transform.forward = new Vector3(UnityEngine.Random.Range(-1f, 1f), 0, UnityEngine.Random.Range(-1f, 1f));
        }

        private void Update()
        {
            if (!_isActive) return;
            
            Perception();
            DecisionMaking();
        }

        #region Perception
        private void Perception()
        {
            _friendTargets.Clear();
            _threatTarget = null;
            _safeZoneTarget = null;

            // Collect all target colliders within the sight radius
            int t = Physics.OverlapSphereNonAlloc(transform.position, _sightRadius, _tmpTargets, _targetsLayer);
            for (int i = 0; i < t; i++)
            {
                var c = _tmpTargets[i];
                if (c==null || c.gameObject == gameObject) continue;

                // Store the friends, threat, and safe zone targets
                switch (c.tag)
                {
                    case _friendTag:
                        _friendTargets.Add(c);
                        break;
                    case _threatTag:
                        _threatTarget = c;
                        break;
                    case _safeZoneTag:
                        _safeZoneTarget = c;
                        break;
                }
            }
        }
        #endregion

        #region Decision Making
        private void DecisionMaking()
        {
            CalculateMoveTarget();
        }

        public void CalculateMoveTarget()
        {
            _floatingTarget = Vector3.Lerp(_floatingTarget, _target, Time.deltaTime * 10f);

            // First calculate the centroid of the friends, this is useful for both flocking and fleeing
            Vector3 centroid = Vector3.zero;

            // Calculate the centroid of all friend targets
            if (_friendTargets.Count > 0)
            {
                foreach (var friend in _friendTargets)
                {
                    centroid += friend.transform.position;
                }

                centroid /= _friendTargets.Count;
            }

            // Primary Behavior: Check if the sheep can see the safe zone, if so head towards it at a run
            if (_safeZoneTarget != null)
            {
                _target = _safeZoneTarget.transform.position;
                _targetSpeed = _runSpeed;
                return;
            }

            // Secondary Behavior: Check if the sheep can see a threat, if so head towards it at a run
            if (_threatTarget != null)
            {
                _target = this.transform.position + (this.transform.position - _threatTarget.transform.position).normalized * 5f;
                float normalizedDistance = Vector3.Distance(transform.position, _threatTarget.transform.position)/_sightRadius;
                _targetSpeed = Mathf.Lerp(_wanderSpeed, _runSpeed, normalizedDistance + 0.25f);

                // If we're fleeing, we also want to weight our target slightly towards the centroid, this keeps the flock a little together
                _target = Vector3.Lerp(_target, centroid, 0.5f);

                return;
            }

            // Default to walk speed
            _targetSpeed = _walkSpeed;

            // HACK!!! Check if the the sheep can see the dog, and it it's tag as friendly, if so head towards it at a walk
            foreach (var friend in _friendTargets)
            {
                if (friend.GetComponentInParent<Dog>() != null)
                {
                    _target = friend.transform.position;
                    _targetSpeed = _walkSpeed;
                    return;
                }
            }

            // If the centroid is outside the flocking distance, we are not in the flock, move towards the centroid
            if (Vector3.Distance(transform.position, centroid) > _flockingDistance)
            {
                _target = centroid;
                _targetSpeed = _walkSpeed;
                return;
            }

            _target = transform.position;

            // Bring the velocity down if not doing anything
            _velocity *= 0.9f;
        }

        #endregion

        #region Action
        private void FixedUpdate()
        {
            if (!_isActive) return;
            
            if (_floatingTarget != Vector3.zero && Vector3.Distance(transform.position, _floatingTarget) > _stoppingDistance)
            {
                // Calculate the direction to the target position
                Vector3 direction = (_floatingTarget - transform.position).normalized;

                // Calculate the movement vector
                _velocity = direction * Mathf.Min(_targetSpeed, Vector3.Distance(transform.position, _floatingTarget));
            }

            // Calculate the desired rotation towards the movement vector
            if (_velocity != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(_velocity);
                
                // Smoothly rotate towards the target rotation based on the turn rate
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, _turnRate);
            }

            _rb.linearVelocity = _velocity;
        }
        #endregion

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag(_safeZoneTag) && !_inSafeZone)
            {
                _inSafeZone = true;

                if (_level != null)
                {
                    _level.SheepEnteredSafeZone(this);
                }
            }
        }

        #region Gizmos
        private void OnDrawGizmosSelected()
        {
            // Draw the sight and obstacle radii
            DrawCircleGizmo(transform.position, _sightRadius, Color.yellow);
            DrawCircleGizmo(transform.position, _flockingDistance, Color.cyan);

            if (_target != Vector3.zero && _target != transform.position)
            {
                DrawCircleGizmo(_target, 1f, Color.magenta);
            }

            // Draw the targets and obstacles
            if (_friendTargets.Count > 0)
            {
                foreach (var target in _friendTargets)
                {
                    DrawColoredLine(transform.position, target.transform.position, Color.cyan);
                }
            }
            if (_threatTarget != null)
            {
                DrawColoredLine(transform.position, _threatTarget.transform.position, Color.red);
            }
            if (_safeZoneTarget != null)
            {
                DrawColoredLine(transform.position, _safeZoneTarget.transform.position, Color.green);
            }
        }

        private void DrawCircleGizmo(Vector3 center, float radius, Color color)
        {
            int segments = 64;
            Vector3[] linePoints = new Vector3[segments * 2];
            
            float angleStep = 2 * Mathf.PI / segments;
            for (int i = 0; i < segments; i++)
            {
                float angleCurrent = i * angleStep;
                float angleNext = (i + 1) * angleStep;

                Vector3 pointCurrent = new Vector3(Mathf.Cos(angleCurrent), 0f, Mathf.Sin(angleCurrent)) * radius + center;
                Vector3 pointNext = new Vector3(Mathf.Cos(angleNext), 0f, Mathf.Sin(angleNext)) * radius + center;

                linePoints[i * 2] = pointCurrent;
                linePoints[i * 2 + 1] = pointNext;
            }

            Gizmos.color = color;
            Gizmos.DrawLineList(linePoints);
        }

        private void DrawColoredLine(Vector3 start, Vector3 end, Color color)
        {
            Gizmos.color = color;
            Gizmos.DrawLine(start, end);
        }
        #endregion

    }
}
