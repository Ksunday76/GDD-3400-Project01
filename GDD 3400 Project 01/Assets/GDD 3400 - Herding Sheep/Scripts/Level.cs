using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Collections;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GDD3400.Project01
{
    public class Level : MonoBehaviour
    {

        public static Level Instance;

        [Header("Level Settings")]
        [SerializeField] Vector2 _levelBounds = new Vector2(25f, 25f);
        public Vector2 LevelBounds => _levelBounds;

        [SerializeField] private bool _generateLevel = true;


        [Header("Game Settings")]
        [SerializeField] int _sheepCount = 12;
        [SerializeField] float _levelPlaytime = 120f;


        [Header("Prefabs")]
        [SerializeField] Sheep _sheepPrefab;
        [SerializeField] Dog _dogPrefab;
        [SerializeField] GameObject _safeZonePrefab;
        //[SerializeField] GameObject _obstaclePrefab;
        [SerializeField] ParticleSystem _sheepSafeParticles;

        [Header("UI")]
        [SerializeField] TextMeshProUGUI _sheepSafeCountText;
        [SerializeField] TextMeshProUGUI _levelTimerText;


        private LayerMask _obstaclesLayerMask;

        private Dog _dog;
        private List<Sheep> _sheep = new List<Sheep>();
        private List<GameObject> _obstacles = new List<GameObject>();
        private GameObject _safeZone;

        private Transform _levelContainer;

        private int _sheepSafeCount = 0;
        private float _levelTimer = 0f;

        private bool _levelComplete = false;

        
        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            Application.targetFrameRate = 60;
        }

        private void Start()
        {
            _obstaclesLayerMask = LayerMask.NameToLayer("Obstacles");

            if (_generateLevel)
            {
                GenerateLevel();
            }

            _levelTimer = _levelPlaytime;
            UpdateUI();
        }

        #region Generate Level

        public void GenerateLevel()
        {
            _levelContainer = new GameObject("Level Container").transform;

            // Generate the safe zone, place on a random edge of the level
            Vector3 safeZonePosition = Vector3.zero;
            int randomDirection = Random.Range(0, 4);

            switch (randomDirection)
            {
                case 0: // Z+
                    safeZonePosition = new Vector3(0, 0, _levelBounds.y);
                    break;
                case 1: // Z-
                    safeZonePosition = new Vector3(0, 0, -_levelBounds.y);
                    break;
                case 2: // X+
                    safeZonePosition = new Vector3(_levelBounds.x, 0, 0);
                    break;
                case 3: // X-
                    safeZonePosition = new Vector3(-_levelBounds.x, 0, 0);
                    break;
            }

            // Instantiate the safe zone
            _safeZone = Instantiate(_safeZonePrefab, safeZonePosition, Quaternion.identity);
            _safeZone.transform.SetParent(_levelContainer);


            // Spawn the sheep
            for (int i = 0; i < _sheepCount; i++)
            {
                var sheep = Instantiate(_sheepPrefab, _levelContainer);
                sheep.Initialize(this, i);
                _sheep.Add(sheep);
            }

            // Position the sheep around the level, avoiding the safe zone
            PositionSheep();

            // float obstacleSize = 1.5f;

            // // Now randomly place between 8 and 12 obstacles around the level, randomizing their size between 1 and 3
            // int obstacleCount = Random.Range(6, 8);
            // for (int i = 0; i < obstacleCount; i++)
            // {
            //     var obstacle = Instantiate(_obstaclePrefab, _levelContainer);
            //     obstacleSize = Random.Range(_obstacleSize.x, _obstacleSize.y);
            //     obstacle.transform.localScale = new Vector3(obstacleSize, obstacleSize, obstacleSize);
            //     _obstacles.Add(obstacle);
            // }

            // // Position the obstacles around the level
            // PositionObstacles();

            // Spawn Dog
            _dog = Instantiate(_dogPrefab, _levelContainer);
            _dog.transform.position = _safeZone.transform.position -(_safeZone.transform.position.normalized * 5f);
            _dog.transform.forward = -_safeZone.transform.position;
        }

        /// <summary>
        /// Position the sheep around the level, avoiding the safe zone, stay 7.5 units away from any other sheep
        /// </summary>
        public void PositionSheep()
        {
            float boundsBuffer = 3.5f;
            int tries = 100;

            foreach (var sheep in _sheep)
            {
                // INSERT_YOUR_CODE
                bool positionFound = false;
                Vector3 position = Vector3.zero;

                while (!positionFound)
                {
                    if (tries <= 0)
                    {
                        Debug.LogError("Failed to find a position for the sheep");
                        break;
                    }

                    tries--;

                    // Generate a random position within the level bounds
                    position = new Vector3(
                        Random.Range(-_levelBounds.x + boundsBuffer, _levelBounds.x - boundsBuffer),
                        0,
                        Random.Range(-_levelBounds.y + boundsBuffer, _levelBounds.y - boundsBuffer)
                    );

                    // Check distance from the safe zone
                    if (Vector3.Distance(position, _safeZone.transform.position) < 15)
                    {
                        continue; // Too close to the safe zone, try another position
                    }

                    // Check distance from other sheep
                    bool tooCloseToOtherSheep = false;
                    foreach (var otherSheep in _sheep)
                    {
                        if (Vector3.Distance(position, otherSheep.transform.position) < 7.5f)
                        {
                            tooCloseToOtherSheep = true;
                            break;
                        }
                    }

                    if (!tooCloseToOtherSheep)
                    {
                        positionFound = true;
                    }
                }

                // Set the sheep's position
                sheep.transform.position = position;
            }
        }

        /// <summary>
        /// Position the obstacles around the level, avoiding the safe zone, staying 5 units away from any other obstacle and sheep
        /// </summary>
        public void PositionObstacles()
        {
            float boundsBuffer = 5f;
            int tries = 100;

            foreach (var obstacle in _obstacles)
            {
                bool positionFound = false;
                Vector3 position = Vector3.zero;

                while (!positionFound && tries > 0)
                {
                    tries--;

                    // Generate a random position within the level bounds
                    position = new Vector3(
                        Random.Range(-_levelBounds.x + boundsBuffer, _levelBounds.x - boundsBuffer),
                        0,
                        Random.Range(-_levelBounds.y + boundsBuffer, _levelBounds.y - boundsBuffer)
                    );

                    // Check distance from the safe zone
                    if (Vector3.Distance(position, _safeZone.transform.position) < 10f)
                    {
                        continue; // Too close to the safe zone, try another position
                    }

                    // Check distance from other obstacles
                    bool tooCloseToOtherObstacles = false;
                    foreach (var otherObstacle in _obstacles)
                    {
                        if (otherObstacle != obstacle && Vector3.Distance(position, otherObstacle.transform.position) < 5f)
                        {
                            tooCloseToOtherObstacles = true;
                            break;
                        }
                    }

                    // Check distance from sheep
                    bool tooCloseToSheep = false;
                    foreach (var sheep in _sheep)
                    {
                        if (Vector3.Distance(position, sheep.transform.position) < 5f)
                        {
                            tooCloseToSheep = true;
                            break;
                        }
                    }

                    if (!tooCloseToOtherObstacles && !tooCloseToSheep)
                    {
                        positionFound = true;
                    }
                }

                // Set the obstacle's position
                obstacle.transform.position = position;
            }
        }

        public void ClearLevel()
        {
            Destroy(_levelContainer.gameObject);

            _sheep = new List<Sheep>();
            _obstacles = new List<GameObject>();
            _safeZone = null;
            _dog = null;
        }

        #endregion

        public void Update()
        {
            _levelTimer -= Time.deltaTime;

            if (_levelComplete) return;

            if (_levelTimer <= 0f)
            {
                foreach (var sheep in _sheep)
                {
                    sheep.IsActive = false;
                }
                if (_dog != null) _dog.IsActive = false;

                _levelComplete = true;
            }

            UpdateUI();
        }

        public void SheepEnteredSafeZone(Sheep sheep)
        {
            Debug.Log("Sheep entered safe zone: " + sheep.name);

            StartCoroutine(FlagSheepAsSafe(sheep));
        }

        private IEnumerator FlagSheepAsSafe(Sheep sheep)
        {
            yield return new WaitForSeconds(2f);

            _sheepSafeCount++;

            Destroy(sheep.gameObject);

            if (_sheepSafeParticles != null)
            {
                ParticleSystem ps = Instantiate(_sheepSafeParticles, sheep.transform.position, Quaternion.identity, null);
                ps.Play();
            }
        }

        private void UpdateUI()
        {
            _sheepSafeCountText.text = _sheepSafeCount + " / " + _sheepCount + " Sheep Safe";
            _levelTimerText.text = _levelTimer.ToString("F0") + " Seconds Left";
        }
    }

        #if UNITY_EDITOR
        [CustomEditor(typeof(Level))]
        public class LevelEditor : Editor
        {
            public override void OnInspectorGUI()
            {
                DrawDefaultInspector();

                if (!Application.isPlaying)
                {
                    return;
                }

                GUILayout.Space(20);

                Level level = (Level)target;
                if (GUILayout.Button("Regenerate Level"))
                {
                    level.ClearLevel();
                    level.GenerateLevel();
                }
            }
        }
        #endif
    }




