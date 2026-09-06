using System;
using UnityEngine;

/// <summary>게임 조작 이벤트의 구독 지점을 제공합니다. 입력 처리와 플레이어 동작을 분리합니다.</summary>
public class InputManager
{
    public event Action NormAction = null;
    public void OnUpdate()
    {
        if (Input.anyKey == false)
            return;
        if (NormAction != null)
        {
            NormAction.Invoke();
        }
    }
}
