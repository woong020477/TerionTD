using UnityEngine;

public interface ITowerSelectable
{
    void OnSelected();
    void OnDeSelected();
}
public interface IEnemySelectable
{
    void OnSelected();
    void OnDeSelected();
}

public interface ILabSelectable
{
    void OnSelected();
    void OnDeSelected();
}