using System;
using UnityEngine;

public class InputManager
{
    public event Action NormAction = null;

    public void OnUpdate()
    {
        if (Input.anyKey == false)          //아무 입력이 없으면 그대로 리턴 (불필요한 반복문을 안 거쳐도 됨)
            return;

        if (NormAction != null)             //NormAction에 구독된 메서드가 있을 시 실행 
        {
            NormAction.Invoke();
        }
    }
}
