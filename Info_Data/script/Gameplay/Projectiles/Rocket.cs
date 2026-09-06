using UnityEngine;

/// <summary>다연장 로켓의 명중 이펙트와 스턴 시간을 지정합니다. 합산 피해는 고정 표적 한 명에게만 적용합니다.</summary>
public class Rocket : BurstProjectile
{
    private ParticleSystem explosionEffectPrefab;
    protected override float StunDuration => 3f;

    public void SetTarget(Transform enemyTarget, float rocketDamage, ParticleSystem explosionParticle, bool replicated = false, int ownerIndex = -1, float presentationDuration = 0f)
    {
        explosionEffectPrefab = explosionParticle;
        Configure(enemyTarget, rocketDamage, replicated, ownerIndex, presentationDuration);
    }

    protected override void PlayImpact()
    {
        if (explosionEffectPrefab == null)
            return;
        var effect = Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
        effect.Play();
        Destroy(effect.gameObject, effect.main.duration + effect.main.startLifetime.constantMax);
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundKey.Fire_Boom);
    }
}
