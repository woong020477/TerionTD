using UnityEngine;

/// <summary>머신건 탄환의 모델 방향과 스턴 시간을 지정합니다. 추적·반환은 BurstProjectile이 담당합니다.</summary>
public class Bullet : BurstProjectile
{
    protected override Quaternion ModelRotation => Quaternion.Euler(90f, 0f, 0f);
    protected override float StunDuration => 2f;

    // 첫 탄환만 합산 피해를 가지고, 나머지 탄환은 동일한 표적을 향한 연출입니다.
    public void SetTarget(Transform enemyTarget, float bulletDamage, bool replicated = false, int ownerIndex = -1, float presentationDuration = 0f)
    {
        Configure(enemyTarget, bulletDamage, replicated, ownerIndex, presentationDuration);
    }
}
