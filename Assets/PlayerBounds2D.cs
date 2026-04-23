using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerBounds2D : MonoBehaviour
{
    [Header("Move Settings")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("Shoot Settings")]
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private Transform bulletTemplateInPlayer;
    [SerializeField] private Transform firePoint;
    [SerializeField] private float shootCooldown = 0.12f;
    [SerializeField] private Vector2 shootDirection = Vector2.up;
    [SerializeField] private float fireOffsetY = 0.8f;
    [SerializeField] private float sideBulletXOffset = 0.02f;
    [SerializeField] private float sideBulletScale = 0.7f;

    [Header("Bounds Settings")]
    [SerializeField] private bool useCameraBounds = true;
    [SerializeField] private Camera boundsCamera;

    [Header("Manual Allowed Area (World Coordinates)")]
    [SerializeField] private Vector2 minBounds = new Vector2(-8f, -4f);
    [SerializeField] private Vector2 maxBounds = new Vector2(8f, 4f);

    private Rigidbody2D rb;
    private Collider2D cachedCollider;
    private SpriteRenderer cachedSpriteRenderer;
    private Vector2 input;
    private float nextShootTime;

    private void Awake()
    {
        EnsureEnemySpawnerExists();

        rb = GetComponent<Rigidbody2D>();
        cachedCollider = GetComponent<Collider2D>();
        cachedSpriteRenderer = GetComponent<SpriteRenderer>();
        if (boundsCamera == null) boundsCamera = Camera.main;
        if (bulletTemplateInPlayer == null) bulletTemplateInPlayer = FindBulletTemplate();
        if (bulletTemplateInPlayer != null) bulletTemplateInPlayer.gameObject.SetActive(false);
    }

    private void EnsureEnemySpawnerExists()
    {
        if (FindObjectOfType<EnemySpawner2D>() != null) return;
        GameObject auto = new GameObject("EnemySpawner_Auto_FromPlayer");
        auto.AddComponent<EnemySpawner2D>();
    }

    private void Start()
    {
        // Ensure initial spawn is also kept inside the allowed area.
        Vector2 clamped = ClampInsideBounds(rb.position);
        rb.position = clamped;
        transform.position = clamped;
    }

    private void Update()
    {
        input = ReadMoveInput().normalized;
        TryShoot();
    }

    private void FixedUpdate()
    {
        Vector2 nextPosition = rb.position + input * moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(ClampInsideBounds(nextPosition));
    }

    private Vector2 ClampInsideBounds(Vector2 position)
    {
        Vector2 halfSize = GetHalfSize();
        GetActiveBounds(out Vector2 activeMin, out Vector2 activeMax);

        float minX = activeMin.x + halfSize.x;
        float maxX = activeMax.x - halfSize.x;
        float minY = activeMin.y + halfSize.y;
        float maxY = activeMax.y - halfSize.y;

        position.x = ClampAxis(position.x, minX, maxX);
        position.y = ClampAxis(position.y, minY, maxY);
        return position;
    }

    private void GetActiveBounds(out Vector2 activeMin, out Vector2 activeMax)
    {
        if (useCameraBounds && boundsCamera != null)
        {
            if (boundsCamera.orthographic)
            {
                float camHalfHeight = boundsCamera.orthographicSize;
                float camHalfWidth = camHalfHeight * boundsCamera.aspect;
                Vector3 camPos = boundsCamera.transform.position;

                activeMin = new Vector2(camPos.x - camHalfWidth, camPos.y - camHalfHeight);
                activeMax = new Vector2(camPos.x + camHalfWidth, camPos.y + camHalfHeight);
                return;
            }

            // Perspective fallback: use viewport corners on player's Z plane.
            float zDistance = Mathf.Abs(transform.position.z - boundsCamera.transform.position.z);
            Vector3 worldMin = boundsCamera.ViewportToWorldPoint(new Vector3(0f, 0f, zDistance));
            Vector3 worldMax = boundsCamera.ViewportToWorldPoint(new Vector3(1f, 1f, zDistance));
            activeMin = new Vector2(worldMin.x, worldMin.y);
            activeMax = new Vector2(worldMax.x, worldMax.y);
            return;
        }

        activeMin = minBounds;
        activeMax = maxBounds;
    }

    private float ClampAxis(float value, float min, float max)
    {
        // If object is bigger than allowed range, keep it centered.
        if (min > max) return (min + max) * 0.5f;
        return Mathf.Clamp(value, min, max);
    }

    private Vector2 GetHalfSize()
    {
        if (cachedCollider != null)
        {
            // Collider bounds are in world units, so this matches movement boundaries.
            return cachedCollider.bounds.extents;
        }

        if (cachedSpriteRenderer != null)
        {
            // Secondary fallback if collider is not set.
            return cachedSpriteRenderer.bounds.extents;
        }

        // Final fallback when both collider and sprite renderer are missing.
        return Vector2.zero;
    }

    private Vector2 ReadMoveInput()
    {
#if ENABLE_INPUT_SYSTEM
        Vector2 raw = Vector2.zero;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.leftArrowKey.isPressed || Keyboard.current.aKey.isPressed) raw.x -= 1f;
            if (Keyboard.current.rightArrowKey.isPressed || Keyboard.current.dKey.isPressed) raw.x += 1f;
            if (Keyboard.current.downArrowKey.isPressed || Keyboard.current.sKey.isPressed) raw.y -= 1f;
            if (Keyboard.current.upArrowKey.isPressed || Keyboard.current.wKey.isPressed) raw.y += 1f;
        }
        return raw;
#else
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
    }

    private void TryShoot()
    {
        if (!ReadShootHeld()) return;
        if (Time.time < nextShootTime) return;

        nextShootTime = Time.time + shootCooldown;
        FireBullet();
    }

    private bool ReadShootHeld()
    {
        bool legacyPressed = Input.GetKey(KeyCode.Space);
#if ENABLE_INPUT_SYSTEM
        bool newInputPressed = Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
        return newInputPressed || legacyPressed;
#else
        return legacyPressed;
#endif
    }

    private void FireBullet()
    {
        GameObject source = ResolveBulletSource();
        if (source == null)
        {
            Debug.LogWarning("Bullet source is missing. Assign bulletPrefab or create a bullet child under player.", this);
            return;
        }

        Vector3 spawnPos = firePoint != null
            ? firePoint.position
            : transform.position + Vector3.up * fireOffsetY;
        Quaternion spawnRot = firePoint != null ? firePoint.rotation : transform.rotation;
        GameObject bullet = Instantiate(source, spawnPos, spawnRot);
        bullet.SetActive(true);
        AdjustSideBulletsLayout(bullet.transform);

        ProjectileMover2D mover = bullet.GetComponent<ProjectileMover2D>();
        if (mover == null) mover = bullet.AddComponent<ProjectileMover2D>();
        if (bullet.GetComponent<PlayerBulletHit2D>() == null) bullet.AddComponent<PlayerBulletHit2D>();

        Vector2 dir = shootDirection.sqrMagnitude > 0.0001f ? shootDirection.normalized : Vector2.up;
        if (dir.y < 0f) dir = Vector2.up;
        mover.SetDirection(dir);
    }

    private void AdjustSideBulletsLayout(Transform bulletRoot)
    {
        if (bulletRoot == null) return;

        Transform leftChild = null;
        Transform rightChild = null;
        Transform[] parts = bulletRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < parts.Length; i++)
        {
            Transform part = parts[i];
            if (part == bulletRoot) continue;

            string n = part.name.ToLowerInvariant();
            if (leftChild == null && (n.Contains("left") || n.Contains("좌")))
            {
                leftChild = part;
                continue;
            }
            if (rightChild == null && (n.Contains("right") || n.Contains("우")))
            {
                rightChild = part;
            }
        }

        // Fallback when names are different: infer left/right by x position.
        if (leftChild == null || rightChild == null)
        {
            SpriteRenderer[] spriteParts = bulletRoot.GetComponentsInChildren<SpriteRenderer>(true);
            Transform minXPart = null;
            Transform maxXPart = null;
            for (int i = 0; i < spriteParts.Length; i++)
            {
                Transform t = spriteParts[i].transform;
                if (t == bulletRoot) continue;
                if (minXPart == null || t.localPosition.x < minXPart.localPosition.x) minXPart = t;
                if (maxXPart == null || t.localPosition.x > maxXPart.localPosition.x) maxXPart = t;
            }
            if (leftChild == null) leftChild = minXPart;
            if (rightChild == null) rightChild = maxXPart;
        }

        float x = Mathf.Max(0.001f, Mathf.Abs(sideBulletXOffset));

        if (leftChild != null)
        {
            Vector3 lp = leftChild.localPosition;
            leftChild.localPosition = new Vector3(-x, lp.y, lp.z);
            leftChild.localScale = Vector3.one * Mathf.Clamp(sideBulletScale, 0.05f, 1f);
        }
        if (rightChild != null)
        {
            Vector3 rp = rightChild.localPosition;
            rightChild.localPosition = new Vector3(x, rp.y, rp.z);
            rightChild.localScale = Vector3.one * Mathf.Clamp(sideBulletScale, 0.05f, 1f);
        }
    }

    private GameObject ResolveBulletSource()
    {
        if (bulletTemplateInPlayer == null) bulletTemplateInPlayer = FindBulletTemplate();
        if (bulletPrefab != null) return bulletPrefab;
        if (bulletTemplateInPlayer != null) return bulletTemplateInPlayer.gameObject;
        return null;
    }

    private Transform FindBulletTemplate()
    {
        Transform[] allChildren = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < allChildren.Length; i++)
        {
            Transform child = allChildren[i];
            if (child == transform) continue;
            string childName = child.name.ToLowerInvariant();
            if (childName.Contains("bullet")) return child;
        }

        // Fallback: pick a child that looks like a bullet set
        // (center sprite with left/right child sprites).
        for (int i = 0; i < allChildren.Length; i++)
        {
            Transform child = allChildren[i];
            if (child == transform) continue;

            int spriteCount = child.GetComponentsInChildren<SpriteRenderer>(true).Length;
            if (spriteCount >= 3) return child;
        }

        // Final fallback: pick any child with a visible sprite.
        for (int i = 0; i < allChildren.Length; i++)
        {
            Transform child = allChildren[i];
            if (child == transform) continue;
            if (child.GetComponentInChildren<SpriteRenderer>(true) != null) return child;
        }

        // Scene-wide fallback: find a likely bullet root even when it is not under player.
        Transform[] allSceneTransforms = Resources.FindObjectsOfTypeAll<Transform>();
        Transform bestCandidate = null;
        int bestScore = -1;
        for (int i = 0; i < allSceneTransforms.Length; i++)
        {
            Transform t = allSceneTransforms[i];
            if (t == null) continue;
            if (!t.gameObject.scene.IsValid()) continue; // ignore project assets/prefabs
            if (t == transform) continue;
            if (t.GetComponentInParent<PlayerBounds2D>() != null) continue; // skip player hierarchy
            if (t.GetComponent<Rigidbody2D>() != null) continue; // likely player/enemy root, not bullet template

            int spriteCount = t.GetComponentsInChildren<SpriteRenderer>(true).Length;
            if (spriteCount <= 0) continue;

            string n = t.name.ToLowerInvariant();
            bool nameHint = n.Contains("bullet") || n.Contains("center") || n.Contains("shot");
            int score = spriteCount * 10 + (nameHint ? 5 : 0);
            if (score > bestScore)
            {
                bestScore = score;
                bestCandidate = t;
            }
        }
        if (bestCandidate != null) return bestCandidate;

        return null;
    }
}

public class ProjectileMover2D : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 12f;
    [SerializeField] private Vector2 moveDirection = Vector2.up;
    [SerializeField] private float lifeTime = 3f;

    private void Start()
    {
        Destroy(gameObject, lifeTime);
    }

    private void Update()
    {
        Vector3 delta = (Vector3)(moveDirection.normalized * moveSpeed * Time.deltaTime);
        transform.position += delta;
    }

    public void SetDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f) return;
        moveDirection = direction.normalized;
    }
}

public class PlayerBulletHit2D : MonoBehaviour
{
    private void Awake()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col == null)
        {
            CircleCollider2D circle = gameObject.AddComponent<CircleCollider2D>();
            circle.radius = 0.12f;
            col = circle;
        }
        col.isTrigger = true;
        EnsureChildSpriteHitColliders();

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    /// <summary>
    /// 좌/우 보조 탄환 스프라이트에도 충돌 판정을 붙여 시각과 판정을 일치시킵니다.
    /// </summary>
    private void EnsureChildSpriteHitColliders()
    {
        SpriteRenderer[] sprites = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < sprites.Length; i++)
        {
            SpriteRenderer sr = sprites[i];
            if (sr == null) continue;
            Transform t = sr.transform;
            if (t == transform) continue;

            Collider2D c = t.GetComponent<Collider2D>();
            if (c == null)
            {
                BoxCollider2D box = t.gameObject.AddComponent<BoxCollider2D>();
                if (sr.sprite != null)
                {
                    box.size = sr.sprite.bounds.size;
                    box.offset = sr.sprite.bounds.center;
                }
                c = box;
            }
            c.isTrigger = true;
        }
    }
}

/// <summary>자식(스프라이트)에 콜라이더가 있을 때, 트리거 콜백은 콜라이더가 붙은 오브젝트로만 갑니다. 부모 EnemyUnit2D는 호출되지 않을 수 있으므로 전달용입니다.</summary>
public class EnemyHurtboxRelay2D : MonoBehaviour
{
    private EnemyUnit2D _unit;

    public void SetUnit(EnemyUnit2D unit)
    {
        _unit = unit;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_unit == null) return;
        _unit.TryHitByBulletTrigger(other);
    }
}

public class EnemyUnit2D : MonoBehaviour
{
    [SerializeField] private float fallSpeed = 2.5f;
    [SerializeField] private float destroyY = -7f;
    [SerializeField] private Vector2 fallbackBoxSize = new Vector2(0.6f, 0.6f);
    [SerializeField] private int maxHealth = 3;
    [SerializeField] private float hitEffectLifetime = 0.2f;

    private bool _killed;
    private int _health;
    private GameObject _lastBulletObject;
    private int _lastHitFrame = -1;
    private GameObject _hitOverlayInstance;
    private float _hitOverlayHideAt = -1f;

    private void Awake()
    {
        _health = Mathf.Max(1, maxHealth);
        SetupHurtboxes();
        SetupHitOverlay();
    }

    private void Update()
    {
        transform.position += Vector3.down * (fallSpeed * Time.deltaTime);
        if (_hitOverlayInstance != null)
        {
            _hitOverlayInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            if (_hitOverlayHideAt > 0f && Time.time >= _hitOverlayHideAt)
            {
                _hitOverlayInstance.SetActive(false);
                _hitOverlayHideAt = -1f;
            }
        }
        if (transform.position.y < destroyY) Destroy(gameObject);
    }

    /// <summary>모든(루트+자식) Collider2D에 트리거 + 히트 전달. 자식 콜라이더 가장자리맞이도 먹히게 합니다.</summary>
    private void SetupHurtboxes()
    {
        Collider2D[] cols = GetComponentsInChildren<Collider2D>(true);
        if (cols == null || cols.Length == 0)
        {
            var box = gameObject.AddComponent<BoxCollider2D>();
            if (!TrySizeBoxToSprite(box))
            {
                box.size = fallbackBoxSize;
            }
            cols = new[] { box };
        }

        for (int i = 0; i < cols.Length; i++)
        {
            Collider2D c = cols[i];
            if (c == null) continue;
            c.isTrigger = true;
            if (c.GetComponent<EnemyHurtboxRelay2D>() == null)
            {
                var relay = c.gameObject.AddComponent<EnemyHurtboxRelay2D>();
                relay.SetUnit(this);
            }
        }
    }

    private bool TrySizeBoxToSprite(BoxCollider2D box)
    {
        var sr = GetComponentInChildren<SpriteRenderer>(true);
        if (sr == null) return false;
        var b = sr.bounds;
        var lossy = transform.lossyScale;
        if (Mathf.Abs(lossy.x) < 0.0001f || Mathf.Abs(lossy.y) < 0.0001f) return false;
        box.offset = transform.InverseTransformPoint(b.center);
        box.size = new Vector2(2f * b.extents.x / Mathf.Abs(lossy.x), 2f * b.extents.y / Mathf.Abs(lossy.y));
        return true;
    }

    internal void TryHitByBulletTrigger(Collider2D other)
    {
        if (_killed) return;

        PlayerBulletHit2D bullet = other.GetComponentInParent<PlayerBulletHit2D>();
        if (bullet == null) return;

        GameObject bulletObject = bullet.gameObject;
        if (_lastHitFrame == Time.frameCount && _lastBulletObject == bulletObject) return;
        _lastHitFrame = Time.frameCount;
        _lastBulletObject = bulletObject;

        Destroy(bulletObject);

        _health--;
        ShowHitOverlay();
        if (_health > 0) return;

        _killed = true;
        Destroy(gameObject);
    }

    private void SetupHitOverlay()
    {
        GameObject template = FindHitTemplateForEnemyName(gameObject.name);
        if (template == null) return;
        _hitOverlayInstance = Instantiate(template, transform);
        _hitOverlayInstance.name = template.name + "_Overlay";
        _hitOverlayInstance.transform.localPosition = Vector3.zero;
        _hitOverlayInstance.transform.localRotation = Quaternion.identity;
        _hitOverlayInstance.transform.localScale = Vector3.one;
        StripGameplayComponents(_hitOverlayInstance);
        _hitOverlayInstance.SetActive(false);
    }

    private void ShowHitOverlay()
    {
        if (_hitOverlayInstance == null) return;
        _hitOverlayInstance.transform.localPosition = Vector3.zero;
        _hitOverlayInstance.transform.localRotation = Quaternion.identity;
        _hitOverlayInstance.SetActive(true);
        _hitOverlayHideAt = Time.time + Mathf.Max(0.02f, hitEffectLifetime);
    }

    private void StripGameplayComponents(GameObject root)
    {
        if (root == null) return;

        Collider2D[] cols = root.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            Destroy(cols[i]);
        }
        Rigidbody2D[] rbs = root.GetComponentsInChildren<Rigidbody2D>(true);
        for (int i = 0; i < rbs.Length; i++)
        {
            Destroy(rbs[i]);
        }
        EnemyUnit2D[] units = root.GetComponentsInChildren<EnemyUnit2D>(true);
        for (int i = 0; i < units.Length; i++)
        {
            Destroy(units[i]);
        }
        EnemyHurtboxRelay2D[] relays = root.GetComponentsInChildren<EnemyHurtboxRelay2D>(true);
        for (int i = 0; i < relays.Length; i++)
        {
            Destroy(relays[i]);
        }
        TimedDestroy2D[] timers = root.GetComponentsInChildren<TimedDestroy2D>(true);
        for (int i = 0; i < timers.Length; i++)
        {
            Destroy(timers[i]);
        }
    }

    private static GameObject FindHitTemplateForEnemyName(string enemyObjectName)
    {
        string type = ExtractEnemyType(enemyObjectName);
        if (string.IsNullOrEmpty(type)) return null;

        string exact1 = ("enenmy " + type + " hit").ToLowerInvariant();
        string exact2 = ("enemy " + type + " hit").ToLowerInvariant();

        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject go = allObjects[i];
            if (go == null) continue;
            if (!go.scene.IsValid()) continue;
            string n = go.name.ToLowerInvariant().Trim();
            if (n == exact1 || n == exact2) return go;
        }
        return null;
    }

    private static string ExtractEnemyType(string enemyObjectName)
    {
        if (string.IsNullOrEmpty(enemyObjectName)) return "";
        string n = enemyObjectName.ToLowerInvariant();
        if (n.Contains("enemy a") || n.Contains("enenmy a")) return "A";
        if (n.Contains("enemy b") || n.Contains("enenmy b")) return "B";
        if (n.Contains("enemy c") || n.Contains("enenmy c")) return "C";
        return "";
    }
}

public class TimedDestroy2D : MonoBehaviour
{
    [SerializeField] private float lifeTime = 0.2f;

    public void SetLifetime(float seconds)
    {
        lifeTime = Mathf.Max(0.01f, seconds);
    }

    private void OnEnable()
    {
        CancelInvoke(nameof(DestroySelf));
        Invoke(nameof(DestroySelf), Mathf.Max(0.01f, lifeTime));
    }

    private void DestroySelf()
    {
        Destroy(gameObject);
    }
}

public class EnemySpawner2D : MonoBehaviour
{
    [SerializeField] private GameObject[] enemyPrefabs;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Vector2 spawnIntervalRange = new Vector2(0.6f, 1.2f);
    [SerializeField] private float spawnTopPadding = 0.8f;
    [Tooltip("씬에 직접 둔 적(스폰에 쓰는 복제 원본)은 제외. 그 외 같은 이름의 적에만 떨어짐/피격 로직을 붙입니다.")]
    [SerializeField] private bool autoMoveSceneEnemies = true;
    [Tooltip("시작 시 씬에 올려둔 Enemy/HIT 템플릿을 자동으로 비활성화합니다.")]
    [SerializeField] private bool hideSceneTemplatesOnStart = true;

    private float nextSpawnTime;
    private bool templatesPrepared;
    private bool didSceneEnemyAttach;
    private bool didSceneTemplateCleanup;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (enemyPrefabs == null || enemyPrefabs.Length == 0) enemyPrefabs = FindEnemySpawnSources();
        TryPrepareAndAttachForScene();
        ScheduleNext();
    }

    private void Update()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return;
        if (enemyPrefabs == null || enemyPrefabs.Length == 0) enemyPrefabs = FindEnemySpawnSources();
        TryPrepareAndAttachForScene();
        if (enemyPrefabs == null || enemyPrefabs.Length == 0) return;
        if (Time.time < nextSpawnTime) return;

        SpawnEnemy();
        ScheduleNext();
    }

    private void SpawnEnemy()
    {
        GameObject prefab = enemyPrefabs[Random.Range(0, enemyPrefabs.Length)];
        if (prefab == null) return;

        GetCameraTopRange(out float minX, out float maxX, out float topY);
        Vector3 spawnPos = new Vector3(Random.Range(minX, maxX), topY + spawnTopPadding, 0f);
        GameObject enemy = Instantiate(prefab, spawnPos, Quaternion.identity);
        enemy.SetActive(true);
        if (enemy.GetComponent<EnemyUnit2D>() == null) enemy.AddComponent<EnemyUnit2D>();
    }

    private void GetCameraTopRange(out float minX, out float maxX, out float topY)
    {
        if (targetCamera.orthographic)
        {
            float halfH = targetCamera.orthographicSize;
            float halfW = halfH * targetCamera.aspect;
            Vector3 p = targetCamera.transform.position;
            minX = p.x - halfW;
            maxX = p.x + halfW;
            topY = p.y + halfH;
            return;
        }

        // z는 카메라에서 월드 평면(보통 z=0)까지의 거리. 잘못 쓰면 X가 화면 중앙에 몰립니다.
        float zDistance = Mathf.Abs(0f - targetCamera.transform.position.z);
        if (zDistance < 0.01f) zDistance = 10f;
        Vector3 min = targetCamera.ViewportToWorldPoint(new Vector3(0f, 0f, zDistance));
        Vector3 max = targetCamera.ViewportToWorldPoint(new Vector3(1f, 1f, zDistance));
        minX = min.x;
        maxX = max.x;
        topY = max.y;
    }

    private void ScheduleNext()
    {
        float min = Mathf.Min(spawnIntervalRange.x, spawnIntervalRange.y);
        float max = Mathf.Max(spawnIntervalRange.x, spawnIntervalRange.y);
        nextSpawnTime = Time.time + Random.Range(min, max);
    }

    /// <summary>
    /// 씬에 둔 "Enemy A" 같은 오브젝트는 Instantiate 복제용으로만 쓰고, 본체는 비활성화해 둡니다.
    /// 본체에 EnemyUnit2D를 붙이면 떨어지며 Destroy되어 이후 스폰 소스가 사라집니다.
    /// </summary>
    private void PrepareSceneSpawnTemplates()
    {
        if (templatesPrepared) return;
        if (enemyPrefabs == null || enemyPrefabs.Length == 0) return;
        for (int i = 0; i < enemyPrefabs.Length; i++)
        {
            GameObject go = enemyPrefabs[i];
            if (go == null) continue;
            if (!go.scene.IsValid()) continue;
            go.SetActive(false);
        }
        templatesPrepared = true;
    }

    /// <summary>스폰 소스 목록이 비어 있을 수 있어, Awake 이후에도 다시 시도합니다. 스폰용 씬 오브젝트는 먼저 끄고(Prepare) 그다음에만 남은 씬 적에 붙입니다.</summary>
    private void TryPrepareAndAttachForScene()
    {
        CleanupSceneTemplatesIfNeeded();
        if (enemyPrefabs == null || enemyPrefabs.Length == 0) return;
        PrepareSceneSpawnTemplates();
        if (!autoMoveSceneEnemies) return;
        if (didSceneEnemyAttach) return;
        didSceneEnemyAttach = true;
        AttachEnemyUnitToSceneEnemies();
    }

    /// <summary>
    /// 히에라키에 템플릿으로 둔 Enemy/HIT 오브젝트가 시작부터 보이지 않게 정리합니다.
    /// </summary>
    private void CleanupSceneTemplatesIfNeeded()
    {
        if (!hideSceneTemplatesOnStart) return;
        if (didSceneTemplateCleanup) return;
        didSceneTemplateCleanup = true;

        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject go = allObjects[i];
            if (go == null) continue;
            if (!go.scene.IsValid()) continue;

            string n = go.name.ToLowerInvariant();
            bool isEnemyTemplate = IsEnemyName(go.name);
            bool isHitTemplate = IsHitTemplateName(n);
            if (!isEnemyTemplate && !isHitTemplate) continue;

            go.SetActive(false);
        }
    }

    private bool IsSpawnSourceReference(GameObject go)
    {
        if (go == null || enemyPrefabs == null) return false;
        for (int i = 0; i < enemyPrefabs.Length; i++)
        {
            if (enemyPrefabs[i] == go) return true;
        }
        return false;
    }

    private void AttachEnemyUnitToSceneEnemies()
    {
        GameObject[] candidates = FindSceneEnemyObjects();
        for (int i = 0; i < candidates.Length; i++)
        {
            GameObject c = candidates[i];
            if (c == null) continue;
            if (IsSpawnSourceReference(c)) continue;
            if (c.GetComponent<EnemyUnit2D>() == null) c.AddComponent<EnemyUnit2D>();
        }
    }

    private GameObject[] FindEnemySpawnSources()
    {
        System.Collections.Generic.List<GameObject> list = new System.Collections.Generic.List<GameObject>();
        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject go = allObjects[i];
            if (go == null) continue;
            if (!IsEnemyName(go.name)) continue;
            if (go.GetComponent<SpriteRenderer>() == null && go.GetComponentInChildren<SpriteRenderer>(true) == null) continue;
            if (!go.scene.IsValid())
            {
                // Accept prefab/assets as spawn source.
                if (!list.Contains(go)) list.Add(go);
                continue;
            }
            // Scene object is also valid as a spawn source.
            if (!list.Contains(go)) list.Add(go);
        }
        return list.ToArray();
    }

    private GameObject[] FindSceneEnemyObjects()
    {
        System.Collections.Generic.List<GameObject> list = new System.Collections.Generic.List<GameObject>();
        SpriteRenderer[] allSprites = FindObjectsOfType<SpriteRenderer>(true);
        for (int i = 0; i < allSprites.Length; i++)
        {
            GameObject go = allSprites[i].gameObject;
            if (go == null) continue;
            if (!go.scene.IsValid()) continue;
            if (!IsEnemyName(go.name)) continue;
            if (!list.Contains(go)) list.Add(go);
        }
        return list.ToArray();
    }

    private bool IsEnemyName(string objectName)
    {
        string n = objectName.ToLowerInvariant();
        if (n.Contains(" hit")) return false;
        return n.Contains("enemy a") || n.Contains("enemy b") || n.Contains("enemy c")
            || n.Contains("enenmy a") || n.Contains("enenmy b") || n.Contains("enenmy c")
            || n == "a" || n == "b" || n == "c" || n.Contains("적");
    }

    private bool IsHitTemplateName(string loweredName)
    {
        if (string.IsNullOrEmpty(loweredName)) return false;
        if (!loweredName.Contains(" hit")) return false;
        return loweredName.Contains("enemy a") || loweredName.Contains("enemy b") || loweredName.Contains("enemy c")
            || loweredName.Contains("enenmy a") || loweredName.Contains("enenmy b") || loweredName.Contains("enenmy c");
    }
}
