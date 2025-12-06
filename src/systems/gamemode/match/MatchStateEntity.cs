using Godot;
using System;

public partial class MatchStateEntity : Node, IReplicatedEntity
{
	public const int MatchStateEntityId = 1;

	[Export] public bool IsAuthority { get; set; } = false;

	private MatchState _serverState;
	private MatchStateClient _clientState;

	public int NetworkId => MatchStateEntityId;

	public event Action<MatchStateSnapshot> SnapshotReceived;

	public override void _Ready()
	{
		if (IsAuthority)
		{
			_serverState = new MatchState();
			EntityReplicationRegistry.Instance?.RegisterEntity(this, this);
		}
		else
		{
			_clientState = new MatchStateClient();
			AddChild(_clientState);
			var remoteManager = GetTree().Root.GetNodeOrNull<RemoteEntityManager>("RemoteEntityManager");
			remoteManager?.RegisterRemoteEntity(NetworkId, this);
		}
	}

	public override void _ExitTree()
	{
		if (IsAuthority)
		{
			EntityReplicationRegistry.Instance?.UnregisterEntity(NetworkId);
		}
	}

	public MatchState GetServerState() => _serverState;
	public MatchStateClient GetClientState() => _clientState;

	public void WriteSnapshot(StreamPeerBuffer buffer)
	{
		if (_serverState == null)
			return;

		buffer.PutU8((byte)_serverState.Phase);
		buffer.PutFloat(_serverState.PhaseTimeRemaining);
		buffer.PutU16((ushort)_serverState.RoundNumber);
		buffer.PutU32((uint)_serverState.ServerTick);
		buffer.PutU8((byte)(_serverState.WinningTeam + 1));

		var scores = _serverState.GetAllTeamScores();
		buffer.PutU8((byte)scores.Length);
		foreach (var score in scores)
		{
			buffer.PutU16((ushort)Mathf.Clamp(score, 0, ushort.MaxValue));
		}

		var modeId = _serverState.CurrentModeId ?? string.Empty;
		var modeIdBytes = System.Text.Encoding.UTF8.GetBytes(modeId);
		buffer.PutU8((byte)Math.Min(modeIdBytes.Length, 255));
		if (modeIdBytes.Length > 0)
			buffer.PutData(modeIdBytes);

		// Objective State
		var manager = GameModeManager.Instance;
		var objState = new ObjectiveState();
		if (manager?.ActiveMode is IGameModeObjectiveDelegate objDelegate)
		{
			objState = objDelegate.GetObjectiveState();
		}
		buffer.PutU8((byte)objState.Status);
		buffer.PutFloat(objState.TimeRemaining);
		buffer.Put8((sbyte)objState.SiteIndex);
	}

	public void ReadSnapshot(StreamPeerBuffer buffer)
	{
		if (buffer.GetAvailableBytes() < 10)
			return;

		var snapshot = new MatchStateSnapshot
		{
			Phase = (MatchPhase)buffer.GetU8(),
			PhaseTimeRemaining = buffer.GetFloat(),
			RoundNumber = buffer.GetU16(),
			ServerTick = (int)buffer.GetU32(),
			WinningTeam = buffer.GetU8() - 1
		};

		var scoreCount = buffer.GetU8();
		snapshot.TeamScores = new int[MatchState.MaxTeams];
		for (int i = 0; i < scoreCount && buffer.GetAvailableBytes() >= 2; i++)
		{
			snapshot.TeamScores[i] = buffer.GetU16();
		}

		if (buffer.GetAvailableBytes() >= 1)
		{
			var modeIdLen = buffer.GetU8();
			if (modeIdLen > 0 && buffer.GetAvailableBytes() >= modeIdLen)
			{
				var modeIdData = buffer.GetData(modeIdLen);
				if ((Error)(int)modeIdData[0] == Error.Ok)
				{
					snapshot.ModeId = System.Text.Encoding.UTF8.GetString(modeIdData[1].AsByteArray());
				}
			}
		}

		if (buffer.GetAvailableBytes() >= 1 + 4 + 1)
		{
			snapshot.Objective = new ObjectiveState
			{
				Status = buffer.GetU8(),
				TimeRemaining = buffer.GetFloat(),
				SiteIndex = buffer.Get8()
			};
		}

		_clientState?.ApplySnapshot(snapshot);
		SnapshotReceived?.Invoke(snapshot);
	}

	public int GetSnapshotSizeBytes()
	{
		var modeIdLen = _serverState?.CurrentModeId?.Length ?? 0;
		// Base: 1+4+2+4+1 + 1+(Teams*2) + 1+Len
		// Objective: 1+4+1 = 6 bytes
		return 1 + 4 + 2 + 4 + 1 + 1 + (MatchState.MaxTeams * 2) + 1 + modeIdLen + 6;
	}
}

