using System.Collections.Generic;
using UnityEngine;

/// <summary>공격 범위 트리거 안의 살아 있는 적을 추적합니다. 클릭 선택 판정과는 별개입니다.</summary>
public class TowerRangeDetect : MonoBehaviour
{
    private TowerController towerController;
    private readonly HashSet<Enemy> inRange = new(); // 적이 범위 내에 있는지 추적
    public bool Contains(Enemy enemy) => enemy != null && enemy.IsAlive && inRange.Contains(enemy);
    private void Awake()
    {
        towerController = GetComponentInParent<TowerController>();
    }

    private void OnTriggerEnter(Collider other)
    {
        var enemy = other.GetComponentInParent<Enemy>();
        if (!enemy)
            return;
        if (inRange.Add(enemy))
            towerController.HandleEnemyInRange(other);
    }

    private void OnTriggerExit(Collider other)
    {
        var enemy = other.GetComponentInParent<Enemy>();
        if (!enemy)
            return;
        if (inRange.Remove(enemy))
            towerController.HandleEnemyExit(other);
    }

    public Enemy GetNextTarget(Transform from)
    {
        inRange.RemoveWhere(enemy => enemy == null || !enemy.IsAlive);
        Enemy best = null;
        float bestSqr = float.MaxValue;
        foreach (var e in inRange)
        {
            if (e == null || !e.IsAlive)
                continue; // 사망 연출 중인 보스도 표적에서 제외합니다.
            float s = (e.transform.position - from.position).sqrMagnitude;
            if (s < bestSqr)
            {
                bestSqr = s;
                best = e;
            }
        }

        return best;
    }

    private void OnDisable() => inRange.Clear();
}
