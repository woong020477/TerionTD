using System.Collections.Generic;
using UnityEngine;

public class TowerRangeDetect : MonoBehaviour
{
    private TowerController towerController;
    private readonly HashSet<Enemy> inRange = new();                        // 적이 범위 내에 있는지 추적
    public bool Contains(Enemy enemy) => enemy && inRange.Contains(enemy);

    private void Awake()
    {
        towerController = GetComponentInParent<TowerController>();          // 타워 컨트롤러 컴포넌트 가져오기
    }

    private void OnTriggerEnter(Collider other)                             // 적이 범위에 들어오면
    {
        var enemy = other.GetComponentInParent<Enemy>();                    // 적 컴포넌트 가져오기
        if (!enemy) return;                                                 // 적이 없으면 무시
        if (inRange.Add(enemy))                                             // 적이 새로 범위에 들어오면 추가
            towerController.HandleEnemyInRange(other);                      // 타워 컨트롤러에 알림
    }

    private void OnTriggerExit(Collider other)                              // 적이 범위에서 나가면
    {
        var enemy = other.GetComponentInParent<Enemy>();                    // 적 컴포넌트 가져오기
        if (!enemy) return;                                                 // 적이 없으면 무시
        if (inRange.Remove(enemy))                                          // 적이 범위에서 나가면 제거
            towerController.HandleEnemyExit(other);                         // 타워 컨트롤러에 알림
    }

    public Enemy GetNextTarget(Transform from)                              // 타워에서 가장 가까운 적을 찾는 메소드
    {
        Enemy best = null;                                                  // 가장 가까운 적을 저장할 변수
        float bestSqr = float.MaxValue;                                     // 가장 가까운 거리의 제곱을 저장할 변수
        foreach (var e in inRange)                                          // 범위 내의 모든 적을 순회
        {
            if (!e) continue;                                               // 적이 없으면 무시
            float s = (e.transform.position - from.position).sqrMagnitude;  // 현재 적과 타워 사이의 거리 제곱 계산
            if (s < bestSqr) { bestSqr = s; best = e; }                     // 가장 가까운 적을 찾으면 bestSqr와 best 업데이트
        }
        return best;                                                        // 가장 가까운 적 반환
    }
}
