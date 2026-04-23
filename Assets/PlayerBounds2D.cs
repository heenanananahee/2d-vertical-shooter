using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using System.Collections.Generic;
using System.Text.RegularExpressions;

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
    [SerializeField] private float fallSpeed = 1.2f;
    [SerializeField] private float destroyY = -7f;
    [SerializeField] private Vector2 fallbackBoxSize = new Vector2(0.6f, 0.6f);
    [SerializeField] private int maxHealth = 3;
    [SerializeField] private float hitEffectLifetime = 0.2f;
    [SerializeField] private bool verboseHitLogs = false;
    [SerializeField] private bool debugHitValidationLogs = true;

    private bool _killed;
    private int _health;
    private GameObject _lastBulletObject;
    private int _lastHitFrame = -1;
    private GameObject _hitOverlayTemplate;
    private GameObject _hitOverlayInstance;
    private float _hitOverlayHideAt = -1f;
    private GameObject _assignedHitTemplate;
    private string _resolvedEnemyType;

    private void Awake()
    {
        _resolvedEnemyType = ExtractEnemyType(gameObject.name);
        _health = Mathf.Max(1, GetInitialHealthByEnemyType());
        SetupHurtboxes();
    }

    private void Start()
    {
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

    public void SetFallSpeed(float speed)
    {
        fallSpeed = Mathf.Max(0.1f, speed);
    }

    public void ConfigureFromSpawner(float speed, GameObject hitTemplate, string enemyType)
    {
        SetFallSpeed(speed);
        if (!string.IsNullOrEmpty(enemyType)) _resolvedEnemyType = enemyType;
        if (hitTemplate != null) _assignedHitTemplate = hitTemplate;
        _health = Mathf.Max(1, GetInitialHealthByEnemyType());
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
        if (verboseHitLogs) Debug.Log("Enemy hit: " + gameObject.name + ", type=" + _resolvedEnemyType + ", remaining hp=" + _health, this);
        if (_health > 0)
        {
            ShowHitOverlay();
            return;
        }

        _killed = true;
        SpawnDetachedHitOverlay();
        // Hide immediately to avoid one-frame "freeze" look before Destroy is processed.
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    private void SetupHitOverlay()
    {
        GameObject template = _assignedHitTemplate;
        string debugInfo = "";
        if (template == null)
        {
            template = FindHitTemplateForEnemyName(gameObject.name, out debugInfo);
        }
        if (template == null)
        {
            Debug.LogWarning("Hit overlay template not found for enemy '" + gameObject.name + "'. " + debugInfo, this);
            return;
        }
        _hitOverlayTemplate = template;
        if (_hitOverlayInstance == null)
        {
            _hitOverlayInstance = Instantiate(_hitOverlayTemplate, transform);
            _hitOverlayInstance.name = _hitOverlayTemplate.name + "_AttachedFx";
            _hitOverlayInstance.transform.localPosition = Vector3.zero;
            _hitOverlayInstance.transform.localRotation = Quaternion.identity;
            _hitOverlayInstance.transform.localScale = Vector3.one;
            PrepareHitFxVisualOnly(_hitOverlayInstance);
            _hitOverlayInstance.SetActive(false);
        }
        if (debugHitValidationLogs)
        {
            Debug.Log("Hit template resolved for enemy '" + gameObject.name + "' => '" + _hitOverlayTemplate.name + "'.", this);
        }
    }

    private void ShowHitOverlay()
    {
        if (_hitOverlayInstance == null)
        {
            SetupHitOverlay();
            if (_hitOverlayInstance == null) return;
        }
        _hitOverlayInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        _hitOverlayInstance.SetActive(true);
        _hitOverlayHideAt = Time.time + Mathf.Max(0.02f, hitEffectLifetime);
    }

    private void SpawnDetachedHitOverlay()
    {
        if (_hitOverlayTemplate == null) return;

        Vector3 spawnPos = GetHitFxSpawnPosition();
        Quaternion spawnRot = transform.rotation;
        GameObject detached = Instantiate(_hitOverlayTemplate, spawnPos, spawnRot);
        detached.name = _hitOverlayTemplate.name + "_HitFx";
        detached.transform.SetParent(null, true);
        // Keep VFX aligned to enemy scale so left/right flip or scaling does not distort hit effect.
        detached.transform.localScale = transform.lossyScale;
        PrepareHitFxVisualOnly(detached);
        detached.SetActive(true);

        TimedDestroy2D timer = detached.GetComponent<TimedDestroy2D>();
        if (timer == null) timer = detached.AddComponent<TimedDestroy2D>();
        timer.SetLifetime(Mathf.Max(0.02f, hitEffectLifetime));

        HitFxDrift2D drift = detached.GetComponent<HitFxDrift2D>();
        if (drift == null) drift = detached.AddComponent<HitFxDrift2D>();
        drift.SetVelocity(Vector3.down * fallSpeed);

        if (verboseHitLogs)
        {
            Debug.Log(
                "Spawn hit VFX: enemy='" + gameObject.name +
                "', template='" + _hitOverlayTemplate.name +
                "', pos=" + spawnPos +
                ", scale=" + detached.transform.localScale + ".",
                this
            );
        }
    }

    private Vector3 GetHitFxSpawnPosition()
    {
        SpriteRenderer sr = GetComponentInChildren<SpriteRenderer>(true);
        if (sr != null) return sr.bounds.center;
        return transform.position;
    }

    private void PrepareHitFxVisualOnly(GameObject fxRoot)
    {
        if (fxRoot == null) return;

        // Disable gameplay scripts immediately so hit-fx cannot move/fall/collide for even one frame.
        Behaviour[] behaviours = fxRoot.GetComponentsInChildren<Behaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            Behaviour b = behaviours[i];
            if (b == null) continue;
            if (b is SpriteRenderer) continue;
            if (b is Animator) continue; // keep animation if template has one
            if (b is TimedDestroy2D) continue;
            b.enabled = false;
        }

        Collider2D[] cols = fxRoot.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null) continue;
            cols[i].enabled = false;
        }

        Rigidbody2D[] rbs = fxRoot.GetComponentsInChildren<Rigidbody2D>(true);
        for (int i = 0; i < rbs.Length; i++)
        {
            Rigidbody2D rb = rbs[i];
            if (rb == null) continue;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.simulated = false;
        }

        // Keep hit-fx visible over enemy body.
        int topOrder = FindTopEnemySortingOrder();
        SpriteRenderer[] fxRenderers = fxRoot.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < fxRenderers.Length; i++)
        {
            SpriteRenderer r = fxRenderers[i];
            if (r == null) continue;
            r.sortingOrder = topOrder + 2;
        }
    }

    private int FindTopEnemySortingOrder()
    {
        SpriteRenderer[] enemyRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        int maxOrder = 0;
        for (int i = 0; i < enemyRenderers.Length; i++)
        {
            SpriteRenderer r = enemyRenderers[i];
            if (r == null) continue;
            if (i == 0 || r.sortingOrder > maxOrder) maxOrder = r.sortingOrder;
        }
        return maxOrder;
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

    private static GameObject FindHitTemplateForEnemyName(string enemyObjectName, out string debugInfo)
    {
        string type = ExtractEnemyType(enemyObjectName);
        if (string.IsNullOrEmpty(type))
        {
            debugInfo = "Could not extract enemy type from name.";
            return null;
        }

        string exact1 = NormalizeNameForLookup("enenmy " + type + " hit");
        string exact2 = NormalizeNameForLookup("enemy " + type + " hit");
        string exact3 = NormalizeNameForLookup(type + " hit");
        string typeToken = type.ToLowerInvariant();

        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        GameObject best = null;
        int bestScore = int.MinValue;
        List<string> sampleCandidates = new List<string>();
        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject go = allObjects[i];
            if (go == null) continue;
            if (go.GetComponentInParent<EnemyUnit2D>() != null) continue; // ignore runtime enemy descendants

            string n = NormalizeNameForLookup(go.name);
            bool candidateLike = n.Contains("hit") || Regex.IsMatch(n, @"\b" + typeToken + @"\b");
            if (candidateLike && sampleCandidates.Count < 8)
            {
                sampleCandidates.Add(go.name + (go.scene.IsValid() ? " [scene]" : " [asset]"));
            }

            int score = ScoreHitTemplateCandidate(n, typeToken, exact1, exact2, exact3, go.scene.IsValid());
            if (score > bestScore)
            {
                bestScore = score;
                best = go;
            }
        }

        if (best != null && bestScore >= 40)
        {
            debugInfo = "Selected template: '" + best.name + "' (score " + bestScore + ").";
            return best;
        }

        string candidateText = sampleCandidates.Count > 0 ? string.Join(", ", sampleCandidates.ToArray()) : "none";
        debugInfo = "Type=" + type + ", bestScore=" + bestScore + ", sampled candidates: " + candidateText;
        return null;
    }

    public static string ExtractEnemyType(string enemyObjectName)
    {
        if (string.IsNullOrEmpty(enemyObjectName)) return "";
        string n = NormalizeNameForLookup(enemyObjectName);
        if (n.Contains("enemy a") || n.Contains("enenmy a")) return "A";
        if (n.Contains("enemy b") || n.Contains("enenmy b")) return "B";
        if (n.Contains("enemy c") || n.Contains("enenmy c")) return "C";
        // Support short names like A, B, C and clone variants like A(Clone), enemy_a(Clone)
        if (Regex.IsMatch(n, @"\ba\b")) return "A";
        if (Regex.IsMatch(n, @"\bb\b")) return "B";
        if (Regex.IsMatch(n, @"\bc\b")) return "C";
        return "";
    }

    private static string NormalizeNameForLookup(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        string n = raw.ToLowerInvariant().Trim();
        n = n.Replace("(clone)", "");
        n = n.Replace("_", " ").Replace("-", " ");
        n = Regex.Replace(n, @"\s+", " ").Trim();
        return n;
    }

    private static int ScoreHitTemplateCandidate(string normalizedName, string typeToken, string exact1, string exact2, string exact3, bool isSceneObject)
    {
        if (string.IsNullOrEmpty(normalizedName)) return int.MinValue;

        int score = int.MinValue;
        if (normalizedName == exact1 || normalizedName == exact2) score = 120;
        else if (normalizedName == exact3) score = 110;
        else if ((normalizedName.Contains("enemy " + typeToken) || normalizedName.Contains("enenmy " + typeToken)) && normalizedName.Contains("hit")) score = 90;
        else if (normalizedName.Contains("hit") && Regex.IsMatch(normalizedName, @"\b" + typeToken + @"\b")) score = 70;
        else if (Regex.IsMatch(normalizedName, @"\b" + typeToken + @"\b")) score = 20;
        else return int.MinValue;

        // Prefer prefab/assets or inactive templates over random scene objects with similar names.
        if (!isSceneObject) score += 5;
        return score;
    }

    private int GetInitialHealthByEnemyType()
    {
        int baseHealth = Mathf.Max(1, maxHealth);
        string type = string.IsNullOrEmpty(_resolvedEnemyType) ? ExtractEnemyType(gameObject.name) : _resolvedEnemyType;
        if (type == "B") return baseHealth * 2;
        if (type == "C") return baseHealth * 3;
        return baseHealth; // A or unknown: keep original health
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

public class HitFxDrift2D : MonoBehaviour
{
    private Vector3 _velocity = Vector3.zero;

    public void SetVelocity(Vector3 velocity)
    {
        _velocity = velocity;
    }

    private void Update()
    {
        if (_velocity == Vector3.zero) return;
        transform.position += _velocity * Time.deltaTime;
    }
}

public class EnemySpawner2D : MonoBehaviour
{
    [SerializeField] private GameObject[] enemyPrefabs;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Vector2 spawnIntervalRange = new Vector2(1.4f, 2.1f);
    [SerializeField] private float spawnTopPadding = 0.8f;
    [Tooltip("씬에 직접 둔 적(스폰에 쓰는 복제 원본)은 제외. 그 외 같은 이름의 적에만 떨어짐/피격 로직을 붙입니다.")]
    [SerializeField] private bool autoMoveSceneEnemies = false;
    [Tooltip("시작 시 씬에 올려둔 Enemy/HIT 템플릿을 자동으로 비활성화합니다.")]
    [SerializeField] private bool hideSceneTemplatesOnStart = true;
    [Header("Enemy Tuning")]
    [SerializeField] private float enemyFallSpeed = 1.2f;
    [SerializeField] private int maxAliveEnemies = 6;
    [Header("Hit Templates (recommended explicit binding)")]
    [SerializeField] private GameObject enemyAHitTemplate;
    [SerializeField] private GameObject enemyBHitTemplate;
    [SerializeField] private GameObject enemyCHitTemplate;
    [SerializeField] private bool logHitTemplateValidation = true;

    private float nextSpawnTime;
    private bool templatesPrepared;
    private bool didSceneEnemyAttach;
    private bool didSceneTemplateCleanup;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (enemyPrefabs == null || enemyPrefabs.Length == 0) enemyPrefabs = FindEnemySpawnSources();
        AutoAssignHitTemplatesIfMissing();
        ValidateHitTemplateBindings();
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
        if (CountAliveEnemies() >= Mathf.Max(1, maxAliveEnemies)) return;

        GameObject prefab = enemyPrefabs[Random.Range(0, enemyPrefabs.Length)];
        if (prefab == null) return;

        GetCameraTopRange(out float minX, out float maxX, out float topY);
        Vector3 spawnPos = new Vector3(Random.Range(minX, maxX), topY + spawnTopPadding, 0f);
        GameObject enemy = Instantiate(prefab, spawnPos, Quaternion.identity);
        enemy.SetActive(true);
        EnemyUnit2D unit = enemy.GetComponent<EnemyUnit2D>();
        if (unit == null) unit = enemy.AddComponent<EnemyUnit2D>();
        string type = EnemyUnit2D.ExtractEnemyType(enemy.name);
        unit.ConfigureFromSpawner(enemyFallSpeed, GetHitTemplateByType(type), type);
    }

    private int CountAliveEnemies()
    {
        EnemyUnit2D[] units = FindObjectsOfType<EnemyUnit2D>();
        int count = 0;
        for (int i = 0; i < units.Length; i++)
        {
            if (units[i] != null && units[i].gameObject.activeInHierarchy) count++;
        }
        return count;
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
            EnemyUnit2D unit = c.GetComponent<EnemyUnit2D>();
            if (unit == null) unit = c.AddComponent<EnemyUnit2D>();
            string type = EnemyUnit2D.ExtractEnemyType(c.name);
            unit.ConfigureFromSpawner(enemyFallSpeed, GetHitTemplateByType(type), type);
        }
    }

    private GameObject GetHitTemplateByType(string type)
    {
        if (type == "A") return enemyAHitTemplate;
        if (type == "B") return enemyBHitTemplate;
        if (type == "C") return enemyCHitTemplate;
        return null;
    }

    private void ValidateHitTemplateBindings()
    {
        if (!logHitTemplateValidation) return;
        if (enemyAHitTemplate == null) Debug.LogWarning("Enemy A hit template is not assigned.", this);
        if (enemyBHitTemplate == null) Debug.LogWarning("Enemy B hit template is not assigned.", this);
        if (enemyCHitTemplate == null) Debug.LogWarning("Enemy C hit template is not assigned.", this);
    }

    private void AutoAssignHitTemplatesIfMissing()
    {
        if (enemyAHitTemplate == null) enemyAHitTemplate = FindHitTemplateInSceneOrAssets("a");
        if (enemyBHitTemplate == null) enemyBHitTemplate = FindHitTemplateInSceneOrAssets("b");
        if (enemyCHitTemplate == null) enemyCHitTemplate = FindHitTemplateInSceneOrAssets("c");
    }

    private GameObject FindHitTemplateInSceneOrAssets(string typeToken)
    {
        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject go = allObjects[i];
            if (go == null) continue;
            string n = NormalizeForTemplateLookup(go.name);
            if (!n.Contains("hit")) continue;
            if (!Regex.IsMatch(n, @"\b" + typeToken + @"\b")) continue;
            return go;
        }
        return null;
    }

    private static string NormalizeForTemplateLookup(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        string n = raw.ToLowerInvariant().Trim();
        n = n.Replace("(clone)", "");
        n = n.Replace("_", " ").Replace("-", " ");
        n = Regex.Replace(n, @"\s+", " ").Trim();
        return n;
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
