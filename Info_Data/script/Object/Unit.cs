using UnityEngine;

public class Unit : MonoBehaviour
{
    /*----- 기본 스테이터스 -----*/
    public int EnemyId;         //적 고유 ID
    public double MaxHP;        //적 최대 체력
    public double HP;           //적 개체 HP
    public float Armor;         //적 방어력
    public long KillGold;       //적 처치시 플레이어에게 주는 골드
    public int spawnerId;       //적이 생성된 스포너 ID
    public float Speed;         //적 이동속도
    public string EnemyName;    //적 이름
    public EnemyType EnemyType; //적 타입
    /*----- 부가적 스테이터스 -----*/

    //상황에 따라 추가
}
