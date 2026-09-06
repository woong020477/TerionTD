using System;

/// <summary>
/// 각 클라이언트가 1부터 발급하는 로컬 ID를 플레이어 인덱스와 결합한 매치 내 식별자입니다.
/// 서로 다른 플레이어가 같은 로컬 ID를 발급해도 레지스트리에서 충돌하지 않습니다.
/// </summary>
public readonly struct NetworkEntityKey : IEquatable<NetworkEntityKey>
{
    public NetworkEntityKey(int ownerIndex, int localId)
    {
        OwnerIndex = ownerIndex;
        LocalId = localId;
    }

    public int OwnerIndex { get; }
    public int LocalId { get; }
    public bool IsValid => OwnerIndex >= 0 && LocalId > 0;

    public bool Equals(NetworkEntityKey other)
    {
        return OwnerIndex == other.OwnerIndex && LocalId == other.LocalId;
    }

    public override bool Equals(object obj)
    {
        return obj is NetworkEntityKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (OwnerIndex * 397) ^ LocalId;
        }
    }

    public override string ToString()
    {
        return $"P{OwnerIndex}:{LocalId}";
    }
}
