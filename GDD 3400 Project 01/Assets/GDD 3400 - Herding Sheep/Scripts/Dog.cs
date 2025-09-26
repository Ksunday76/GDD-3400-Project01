using UnityEngine;

namespace GDD3400.Project01
{
    // Used Chatgpt to clean up code and debug. Also used Chatgpt to help fix a bug where the dog would consistently
    // push a single sheep to the center and would stall.
    // Issue still persists but is a less likely.
    // Used Chatgpt to add appropriate comments to explain code better.


    // Sneak until sheep are seen. Then Threat to push them toward the SafeZone.
    // Handles the "one sheep in the center" stall with side-lock + reposition.
    [RequireComponent(typeof(Rigidbody))]
    public class Dog : MonoBehaviour
    {
        private bool _isActive = true;
        public bool IsActive { get => _isActive; set => _isActive = value; }

        // Required by the assignment
        private readonly float _maxSpeed = 5f;
        private readonly float _sightRadius = 7.5f;

        // Layers/Tags
        public LayerMask _targetsLayer;
        private readonly string _threatTag = "Threat";
        private readonly string _safeZoneTag = "SafeZone";

        private Rigidbody _rb;

        // Level bounds
        private Vector2 _levelHalfSize = new Vector2(25f, 25f);
        private const float _boundsPadding = 1f;

        // SafeZone
        private Vector3 _safePos;
        private bool _safeFound;

        // Perception
        private readonly Collider[] _hits = new Collider[48];
        private int _visibleUnsafeCount;
        private Vector3 _visibleCentroid;
        private Transform _closestSheepT;

        // Mode
        private enum Mode { Sneak, Threat }
        private Mode _mode = Mode.Sneak;

        // Movement
        private Vector3 _moveTarget;
        private Vector3 _smoothedTarget;
        private Vector3 _lastMoveDir = Vector3.forward;

        // Wander
        private float _wanderRetargetTime;
        private readonly float _wanderIntervalMin = 1.2f;
        private readonly float _wanderIntervalMax = 2.0f;
        private readonly float _wanderMinStep = 7f;

        // Threat movement
        private readonly float _behindDistance = 3.0f;
        private readonly float _minPressureThreat = 1.4f;
        private readonly float _arriveStopRadius = 1.0f;
        private readonly float _arriveSlowRadius = 2.4f;
        private readonly float _nearSafeFinish = 3.5f;

        // One-sheep handling
        private readonly float _singleCommitDelay = 3.0f;
        private float _singleTimer = 0f;
        private Transform _singleSheepT;

        // One-sheep side lock
        private int _escortSideSign = 0;           // -1 or +1
        private float _escortSideHoldTimer = 0f;
        private const float _escortSideHold = 0.6f;

        // Anti-stall: progress watchdog
        private float _progressTimer = 0f;
        private float _lastRefDist = 0f;
        private const float _progressWindow = 1.6f;
        private const float _minProgressPerWindow = 0.5f;
        private float _repositionCooldown = 0f;
        private const float _repositionCooldownTime = 1.2f;
        private int _repositionSide = 1;

        private void Awake()
        {
            _targetsLayer = LayerMask.GetMask("Targets");

            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.constraints = RigidbodyConstraints.FreezePositionY |
                              RigidbodyConstraints.FreezeRotationX |
                              RigidbodyConstraints.FreezeRotationZ;
        }

        private void Start()
        {
            var level = Level.Instance;
            if (level != null) _levelHalfSize = level.LevelBounds;

            // Optional fallback: snap near SafeZone if not already placed by Level
            var sz = GameObject.FindGameObjectWithTag(_safeZoneTag);
            if (sz != null)
            {
                _safePos = sz.transform.position;
                _safeFound = true;

                Vector3 expected = _safePos - _safePos.normalized * 5f;
                if ((transform.position - expected).sqrMagnitude > 0.25f)
                {
                    transform.position = expected;
                    Vector3 faceCenter = (-_safePos).normalized;
                    if (faceCenter.sqrMagnitude > 0.001f) transform.forward = faceCenter;
                }
            }

            SetMode(Mode.Sneak);
            PickNewWanderTarget(biasAwayFromSafe: true);
        }

        private void Update()
        {
            if (!_isActive) return;

            if (_repositionCooldown > 0f) _repositionCooldown -= Time.deltaTime;

            Sense();
            Decide();
        }

        private void FixedUpdate()
        {
            if (!_isActive) return;

            // Smooth target to reduce twitch
            _smoothedTarget = Vector3.Lerp(_smoothedTarget, _moveTarget, 0.22f);

            // Arrive
            Vector3 pos = ProjectXZ(transform.position);
            Vector3 tgt = ProjectXZ(_smoothedTarget);
            Vector3 toT = tgt - pos;
            float dist = toT.magnitude;

            Vector3 vel = Vector3.zero;

            if (dist > _arriveStopRadius)
            {
                float speed = Mathf.Clamp01(dist / _arriveSlowRadius) * _maxSpeed;
                if (_mode == Mode.Threat)
                {
                    float minP = (_visibleUnsafeCount == 1) ? 1.7f : _minPressureThreat;
                    speed = Mathf.Max(minP, speed);
                }
                vel = (toT / Mathf.Max(dist, 0.0001f)) * Mathf.Min(speed, _maxSpeed);
            }
            else if (_mode == Mode.Threat)
            {
                float minP = (_visibleUnsafeCount == 1) ? 1.7f : _minPressureThreat;
                vel = transform.forward * minP;
            }

            _rb.linearVelocity = vel;

            // Face move direction
            if (vel.sqrMagnitude > 0.05f) _lastMoveDir = vel.normalized;
            if (_lastMoveDir.sqrMagnitude > 0.1f)
            {
                var look = Quaternion.LookRotation(_lastMoveDir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 540f * Time.fixedDeltaTime);
            }

            // Clamp to bounds (XZ only; Y is frozen by Rigidbody)
            var clamped = ClampToBounds(transform.position);
            if (clamped.x != transform.position.x || clamped.z != transform.position.z)
                transform.position = new Vector3(clamped.x, transform.position.y, clamped.z);
        }

        // Look for unsafe sheep and their centroid
        private void Sense()
        {
            _visibleUnsafeCount = 0;
            _visibleCentroid = Vector3.zero;
            _closestSheepT = null;

            float bestSq = float.MaxValue;
            int n = Physics.OverlapSphereNonAlloc(transform.position, _sightRadius, _hits, _targetsLayer);

            for (int i = 0; i < n; i++)
            {
                var c = _hits[i];
                if (!c) continue;

                var s = c.GetComponent<Sheep>();
                if (s != null && s.IsActive && !s.InSafeZone)
                {
                    _visibleUnsafeCount++;
                    _visibleCentroid += s.transform.position;

                    float sq = (s.transform.position - transform.position).sqrMagnitude;
                    if (sq < bestSq) { bestSq = sq; _closestSheepT = s.transform; }
                }
            }

            if (_visibleUnsafeCount > 0)
            {
                _visibleCentroid /= _visibleUnsafeCount;
            }

            // One-sheep timer
            if (_visibleUnsafeCount == 1)
            {
                if (_singleSheepT != _closestSheepT)
                {
                    _singleSheepT = _closestSheepT;
                    _singleTimer = 0f;
                }
                else
                {
                    _singleTimer += Time.deltaTime;
                }
            }
            else
            {
                _singleTimer = 0f;
                _singleSheepT = null;

                // Reset side lock if not exactly one sheep
                _escortSideSign = 0;
                _escortSideHoldTimer = 0f;
            }
        }

        // Sneak → Threat
        private void Decide()
        {
            if (_safeFound && _visibleUnsafeCount > 0)
            {
                SetMode(Mode.Threat);

                bool commitSingle = (_visibleUnsafeCount == 1 && _singleTimer >= _singleCommitDelay);

                if (_visibleUnsafeCount == 1 && _singleSheepT != null)
                {
                    // Chatgpt note: I used Chatgpt here to reduce the "one sheep stalls in the center" bug.
                    // Side-lock + "never cross front" helped, but it only fixed it somewhat.
                    // One sheep: hold a side, don't cross in front
                    Vector3 sheep = _singleSheepT.position;
                    Vector3 dir = (sheep - _safePos); dir.y = 0f;
                    if (dir.sqrMagnitude < 0.0001f) dir = -transform.forward;
                    dir.Normalize();

                    Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;

                    if (_escortSideSign == 0)
                    {
                        float sideDot = Vector3.Dot(side, (transform.position - sheep).normalized);
                        _escortSideSign = (sideDot >= 0f) ? +1 : -1;
                        _escortSideHoldTimer = _escortSideHold;
                    }
                    else
                    {
                        _escortSideHoldTimer = Mathf.Max(0f, _escortSideHoldTimer - Time.deltaTime);
                    }

                    float behind = _behindDistance + 0.5f + (commitSingle ? 0.3f : 0f);
                    float offset = 0.8f;
                    Vector3 target = sheep + dir * behind + side * (_escortSideSign * offset);

                    // If we end up in front of the sheep, step further behind
                    Vector3 dogFromSheep = transform.position - sheep; dogFromSheep.y = 0f;
                    if (Vector3.Dot(dogFromSheep, dir) < 0f)
                    {
                        target = sheep + dir * (behind + 1.8f);
                        if (_escortSideHoldTimer <= 0f) _escortSideSign = 0;
                    }

                    _moveTarget = ClampToBounds(target);
                }
                else
                {
                    // Group: aim behind centroid with a small inward nudge
                    Vector3 anchor = _visibleCentroid;
                    Vector3 dir = (anchor - _safePos); dir.y = 0f;
                    if (dir.sqrMagnitude < 0.0001f) dir = -transform.forward;
                    dir.Normalize();

                    Vector3 pushPoint = anchor + dir * _behindDistance;
                    _moveTarget = ClampToBounds(pushPoint + (anchor - pushPoint).normalized * 0.4f);
                }

                // Progress watchdog: if no progress, swing around
                Vector3 refPos = CurrentRefPos();
                float d = HorizontalDistance(refPos, _safePos);
                if (_lastRefDist <= 0.0001f) _lastRefDist = d;

                _progressTimer += Time.deltaTime;
                if (_progressTimer >= _progressWindow)
                {
                    float progress = _lastRefDist - d;
                    if (progress < _minProgressPerWindow && _repositionCooldown <= 0f)
                    {
                        Reposition(refPos);
                    }
                    _progressTimer = 0f;
                    _lastRefDist = d;
                }

                // Close enough: go back to Sneak
                if (HorizontalDistance(refPos, _safePos) <= _nearSafeFinish)
                {
                    SetMode(Mode.Sneak);
                    _progressTimer = 0f;
                    _lastRefDist = 0f;
                    PickNewWanderTarget(biasAwayFromSafe: true);
                }
            }
            else
            {
                // Nothing in sight: wander
                SetMode(Mode.Sneak);
                _progressTimer = 0f;
                _lastRefDist = 0f;

                if (Time.time >= _wanderRetargetTime ||
                    HorizontalDistance(transform.position, _moveTarget) < 1.25f)
                {
                    PickNewWanderTarget(biasAwayFromSafe: false);
                }
            }
        }

        // Pick a better flank behind the ref point
        private void Reposition(Vector3 refPos)
        {
            Vector3 dir = (refPos - _safePos); dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = -transform.forward;
            dir.Normalize();

            Vector3 perp = Vector3.Cross(Vector3.up, dir).normalized * _repositionSide;

            float extraBehind = 2.2f;
            float lateralArc = 3.5f;

            Vector3 target = refPos + dir * (_behindDistance + extraBehind) + perp * lateralArc;

            _moveTarget = ClampToBounds(target);
            _repositionCooldown = _repositionCooldownTime;

            _escortSideSign = (_repositionSide >= 0) ? +1 : -1;
            _escortSideHoldTimer = _escortSideHold;
            _repositionSide *= -1;
        }

        private Vector3 CurrentRefPos()
        {
            if (_visibleUnsafeCount == 1 && _singleSheepT != null) return _singleSheepT.position;
            return _visibleCentroid;
        }

        // Helpers

        private void SetMode(Mode m)
        {
            if (_mode == m) return;
            _mode = m;
            gameObject.tag = (_mode == Mode.Threat) ? _threatTag : "Untagged";
        }

        private void PickNewWanderTarget(bool biasAwayFromSafe)
        {
            Vector3 pick = transform.position;

            if (biasAwayFromSafe && _safeFound)
            {
                Vector3 away = (ProjectXZ(transform.position) - ProjectXZ(_safePos)).normalized;
                if (away.sqrMagnitude < 0.01f) away = Quaternion.Euler(0f, 90f, 0f) * Vector3.forward;
                pick = transform.position + away * _wanderMinStep;
            }
            else
            {
                // Random point in bounds, not too close to current pos
                for (int i = 0; i < 16; i++)
                {
                    float x = Random.Range(-_levelHalfSize.x + _boundsPadding, _levelHalfSize.x - _boundsPadding);
                    float z = Random.Range(-_levelHalfSize.y + _boundsPadding, _levelHalfSize.y - _boundsPadding);
                    Vector3 p = new Vector3(x, transform.position.y, z);
                    if (HorizontalDistance(transform.position, p) >= _wanderMinStep) { pick = p; break; }
                }
            }

            _moveTarget = ClampToBounds(pick);
            _wanderRetargetTime = Time.time + Random.Range(_wanderIntervalMin, _wanderIntervalMax);
        }

        private static Vector3 ProjectXZ(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private Vector3 ClampToBounds(Vector3 pos)
        {
            float minX = -_levelHalfSize.x + _boundsPadding;
            float maxX = _levelHalfSize.x - _boundsPadding;
            float minZ = -_levelHalfSize.y + _boundsPadding;
            float maxZ = _levelHalfSize.y - _boundsPadding;

            float x = Mathf.Clamp(pos.x, minX, maxX);
            float z = Mathf.Clamp(pos.z, minZ, maxZ);
            return new Vector3(x, pos.y, z);
        }
    }
}
