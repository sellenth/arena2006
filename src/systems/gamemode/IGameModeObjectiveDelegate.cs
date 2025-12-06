using Godot;

public interface IGameModeObjectiveDelegate
{
    bool IsRoundActive { get; }
    bool IsAttacker(int playerId);
    bool IsDefender(int playerId);
    bool CanPlant(PlayerCharacter player, BombSite site);
    void OnPlantCompleted(PlayerCharacter player, BombSite site);
    void OnDefuseCompleted(PlayerCharacter player);
    ObjectiveState GetObjectiveState();
}
