using System;
using Project.Scripts.DataTypes;
using Project.Scripts.Gameplay;
using Project.Scripts.Interface;
using UnityEngine;
using Zenject;

namespace Project.Scripts.TimeAndWeather
{
    /// <summary>Camera shake and telegraphed falling-rock hazards for earthquakes.</summary>
    public sealed class EarthquakeWeatherController : ITickable, ILateTickable,
        IDisposable
    {
        public const string EffectId = "hazard.earthquake";

        private readonly IRegionalWeatherService _weather;
        private readonly IIndoorWeatherMask _indoorMask;
        private readonly PlayerDataController _player;
        private readonly IScreenShakeService _screenShake;
        private readonly EruptionService _eruptions;
        private float _intensity;
        private float _nextRockTime;

        public EarthquakeWeatherController(
            IRegionalWeatherService weather,
            IIndoorWeatherMask indoorMask,
            PlayerDataController player,
            IScreenShakeService screenShake,
            EruptionService eruptions)
        {
            _weather = weather;
            _indoorMask = indoorMask;
            _player = player;
            _screenShake = screenShake;
            _eruptions = eruptions;
        }

        public void Tick()
        {
            Vector2 playerPosition = _player.transform.position;
            WeatherSample sample = _weather.Sample(playerPosition);
            _intensity = GetEarthquakeIntensity(sample);

            if (_intensity <= 0f || _indoorMask?.IsViewerIndoors == true)
            {
                _nextRockTime = 0f;
                return;
            }

            if (_nextRockTime <= 0f)
                _nextRockTime = Time.time + UnityEngine.Random.Range(0.25f, 0.7f);

            if (Time.time < _nextRockTime)
                return;

            SpawnRock(playerPosition, _intensity);
            _nextRockTime = Time.time + UnityEngine.Random.Range(0.55f, 1.15f) /
                Mathf.Lerp(0.75f, 1.25f, _intensity);
        }

        public void LateTick()
        {
            bool shake = _intensity > 0f &&
                         _indoorMask?.IsViewerIndoors != true;
            if (shake)
            {
                float strength = Mathf.Lerp(0.035f, 0.16f, _intensity);
                _screenShake?.SetContinuous(
                    EffectId,
                    new ScreenShakeRequest(strength, 0f, 29f, 0f));
            }
            else
                _screenShake?.ClearContinuous(EffectId);
        }

        public void Dispose() => _screenShake?.ClearContinuous(EffectId);

        private void SpawnRock(Vector2 playerPosition, float intensity)
        {
            EruptionDefinition rock = CreateEarthquakeRockDefinition(
                intensity);
            _eruptions?.SpawnRandomArea(
                rock,
                playerPosition,
                2.5f,
                7f,
                1,
                null,
                null,
                0,
                Mathf.Lerp(0.9f, 0.6f, intensity),
                position => _indoorMask?.IsWorldPositionIndoors(position) != true,
                EntityDamageSource.Environment);
        }

        private static EruptionDefinition CreateEarthquakeRockDefinition(
            float intensity) => new()
        {
            radius = Mathf.Lerp(0.65f, 0.9f, intensity),
            damage = Mathf.RoundToInt(Mathf.Lerp(8f, 16f, intensity)),
            targetLayers = 1 << 0,
            attackType = PlayerAttackType.Magic,
            visualStyle = EruptionVisualStyle.FallingBoulder
        };

        public static float GetEarthquakeIntensity(WeatherSample sample)
        {
            foreach (WeatherEffectSample active in
                     sample.ActiveEffects ?? Array.Empty<WeatherEffectSample>())
            {
                if (active.Effect != null &&
                    string.Equals(active.Effect.EffectId, EffectId,
                        StringComparison.OrdinalIgnoreCase))
                    return active.Intensity;
            }
            return 0f;
        }
    }

    [DisallowMultipleComponent]
    public sealed class EruptionService : MonoBehaviour,
        IEruptionPatternSpawner
    {
        private IAttackService _attackService;

        [Inject]
        public void Construct(IAttackService attackService) =>
            _attackService = attackService;

        public void SpawnPattern(
            SkillActionContext context,
            EruptionPatternData pattern,
            Vector3 completionPosition)
        {
            if (pattern?.eruption == null)
                return;
            Vector3 origin = pattern.origin switch
            {
                EruptionOriginMode.Caster when context.User != null =>
                    context.User.transform.position,
                EruptionOriginMode.Target when context.Target != null =>
                    context.Target.transform.position,
                _ => completionPosition
            };
            int rings = Mathf.Max(1, pattern.ringCount);
            int perRing = Mathf.Max(1, pattern.eruptionsPerRing);
            for (int ring = 0; ring < rings; ring++)
            {
                float distance = Mathf.Max(0f,
                    pattern.firstRingDistance + pattern.ringSpacing * ring);
                int count = distance <= 0.001f ? 1 : perRing;
                float rotation = pattern.initialRotation +
                                 pattern.rotationOffsetPerRing * ring;
                float delay = Mathf.Max(0f,
                    pattern.detonationDelay + pattern.delayPerRing * ring);
                for (int index = 0; index < count; index++)
                {
                    float angle = rotation + 360f * index / count;
                    Vector2 direction = Quaternion.Euler(0f, 0f, angle) *
                                        Vector2.right;
                    SpawnSingle(
                        pattern.eruption,
                        origin + (Vector3)(direction * distance),
                        context.User,
                        context.Skill,
                        context.AttackPotential,
                        delay);
                }
            }
        }

        public void SpawnRandomArea(
            EruptionDefinition definition,
            Vector2 center,
            float minimumDistance,
            float maximumDistance,
            int count,
            GameObject source,
            SkillData skill,
            int attackPotential,
            float delay,
            Func<Vector2, bool> positionAllowed = null,
            EntityDamageSource fallbackSource = EntityDamageSource.Skill)
        {
            int remaining = Mathf.Max(0, count);
            int attempts = remaining * 8;
            while (remaining > 0 && attempts-- > 0)
            {
                Vector2 direction = UnityEngine.Random.insideUnitCircle.normalized;
                if (direction.sqrMagnitude <= Mathf.Epsilon)
                    direction = Vector2.right;
                float distance = UnityEngine.Random.Range(
                    Mathf.Max(0f, minimumDistance),
                    Mathf.Max(minimumDistance, maximumDistance));
                Vector2 position = center + direction * distance;
                if (positionAllowed != null && !positionAllowed(position))
                    continue;
                SpawnSingle(definition, position, source, skill,
                    attackPotential, delay, fallbackSource);
                remaining--;
            }
        }

        public void SpawnSingle(
            EruptionDefinition definition,
            Vector3 position,
            GameObject source,
            SkillData skill,
            int attackPotential,
            float delay,
            EntityDamageSource fallbackSource = EntityDamageSource.Skill)
        {
            if (definition == null)
                return;
            GameObject hazard = new("Eruption");
            hazard.transform.position = position;
            IEntityDamageSource damageSource =
                source != null
                    ? source.GetComponentInParent<IEntityDamageSource>()
                    : null;
            var tags = new System.Collections.Generic.List<EntityTag>();
            if (damageSource?.DamageTags != null)
                tags.AddRange(damageSource.DamageTags);
            if (skill?.tags != null)
                tags.AddRange(skill.tags);
            if (definition.damageTags != null)
                tags.AddRange(definition.damageTags);
            if (definition.element != null)
                tags.Add(definition.element);
            hazard.AddComponent<EruptionRuntime>().Initialize(
                _attackService,
                definition,
                skill,
                tags,
                damageSource?.DamageSource ?? fallbackSource,
                attackPotential,
                delay);
        }
    }

    public sealed class EruptionRuntime : MonoBehaviour
    {
        private static Sprite _discSprite;
        private IAttackService _attackService;
        private EruptionDefinition _definition;
        private SkillData _skill;
        private System.Collections.Generic.IReadOnlyList<EntityTag> _tags;
        private EntityDamageSource _source;
        private int _attackPotential;
        private float _delay;
        private float _age;
        private Transform _telegraph;
        private Transform _fallbackEffect;
        private bool _detonated;

        public void Initialize(
            IAttackService attackService,
            EruptionDefinition definition,
            SkillData skill,
            System.Collections.Generic.IReadOnlyList<EntityTag> tags,
            EntityDamageSource source,
            int attackPotential,
            float delay)
        {
            _attackService = attackService;
            _definition = definition;
            _skill = skill;
            _tags = tags;
            _source = source;
            _attackPotential = attackPotential;
            _delay = Mathf.Max(0.01f, delay);
            CreatePresentation();
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (!_detonated)
            {
                float progress = Mathf.Clamp01(_age / _delay);
                if (_telegraph != null)
                    _telegraph.localScale = Vector3.one * Mathf.Lerp(
                        0.08f, Mathf.Max(0.05f, _definition.radius) * 2f,
                        progress);
                if (_fallbackEffect != null &&
                    _definition.visualStyle == EruptionVisualStyle.FallingBoulder)
                    _fallbackEffect.localPosition = Vector3.up *
                        Mathf.Lerp(5f, 0f, progress * progress);
                if (progress >= 1f)
                    Detonate();
                return;
            }

            float lifetime = Mathf.Max(0.28f, _definition.effectLifetime);
            float fade = 1f - Mathf.Clamp01((_age - _delay) / lifetime);
            if (_fallbackEffect != null)
                _fallbackEffect.localScale = Vector3.one *
                    (_definition.radius * 1.25f * fade);
            if (fade <= 0f)
                Destroy(gameObject);
        }

        private void Detonate()
        {
            _detonated = true;
            if (_telegraph != null)
                Destroy(_telegraph.gameObject);
            if (_fallbackEffect != null)
                _fallbackEffect.gameObject.SetActive(true);
            if (_definition.visualStyle == EruptionVisualStyle.Prefab &&
                _definition.eruptionEffectPrefab != null)
            {
                GameObject effect = Instantiate(
                    _definition.eruptionEffectPrefab,
                    transform.position,
                    Quaternion.identity);
                Destroy(effect, Mathf.Max(0.1f, _definition.effectLifetime));
            }
            var damaged = new System.Collections.Generic.HashSet<PlayerDataController>();
            foreach (Collider2D hit in Physics2D.OverlapCircleAll(
                         transform.position,
                         Mathf.Max(0.05f, _definition.radius),
                         _definition.targetLayers))
            {
                PlayerDataController player =
                    hit?.GetComponentInParent<PlayerDataController>();
                if (player == null || !damaged.Add(player))
                    continue;
                IDamageable target = player.GetComponent<IDamageable>();
                if (target == null)
                    continue;
                int damage = Mathf.Max(0, _definition.damage) +
                             (_definition.includeAttackPotential
                                 ? Mathf.Max(0, _attackPotential)
                                 : 0);
                AttackContext context = new(
                    gameObject,
                    null,
                    damage,
                    _source,
                    _tags,
                    _skill,
                    _definition.attackType);
                if (_attackService != null)
                    _attackService.Attack(target, context);
                else
                    target.TakeDamage(context);
            }
        }

        private void CreatePresentation()
        {
            if (_definition.visualStyle == EruptionVisualStyle.Prefab &&
                _definition.telegraphPrefab != null)
            {
                _telegraph = Instantiate(
                    _definition.telegraphPrefab,
                    transform).transform;
            }
            else if (_definition.visualStyle == EruptionVisualStyle.FallingBoulder)
                _telegraph = CreateDisc(
                    "Growing Shadow",
                    new Color(0f, 0f, 0f, 0.52f),
                    -1);
            else
                _telegraph = CreateDisc(
                    "Eruption Telegraph",
                    _definition.telegraphColor,
                    -1);

            if (_definition.visualStyle != EruptionVisualStyle.Prefab ||
                _definition.eruptionEffectPrefab == null)
            {
                _fallbackEffect = CreateDisc(
                    _definition.visualStyle == EruptionVisualStyle.FallingBoulder
                        ? "Falling Boulder"
                        : "Eruption",
                    _definition.visualStyle == EruptionVisualStyle.FallingBoulder
                        ? new Color(0.25f, 0.20f, 0.16f, 1f)
                        : _definition.fallbackEffectColor,
                    5);
                _fallbackEffect.localScale = Vector3.one *
                    (_definition.radius * 1.25f);
                if (_definition.visualStyle == EruptionVisualStyle.FallingBoulder)
                    _fallbackEffect.localPosition = Vector3.up * 5f;
                else
                    _fallbackEffect.gameObject.SetActive(false);
            }
        }

        private Transform CreateDisc(string objectName, Color color, int order)
        {
            GameObject child = new(objectName);
            child.transform.SetParent(transform, false);
            SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = GetDiscSprite();
            renderer.color = color;
            renderer.sortingOrder = order;
            return child.transform;
        }

        private static Sprite GetDiscSprite()
        {
            if (_discSprite != null)
                return _discSprite;

            const int size = 32;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
            {
                name = "Eruption Hazard Disc",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            Color[] pixels = new Color[size * size];
            Vector2 center = Vector2.one * ((size - 1) * 0.5f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(
                    new Vector2(x, y), center) / (size * 0.5f);
                pixels[x + y * size] = new Color(
                    1f, 1f, 1f,
                    Mathf.Clamp01((1f - distance) * 5f));
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            _discSprite = Sprite.Create(
                texture,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                size);
            _discSprite.name = "Eruption Hazard Disc";
            _discSprite.hideFlags = HideFlags.HideAndDontSave;
            return _discSprite;
        }
    }

}
