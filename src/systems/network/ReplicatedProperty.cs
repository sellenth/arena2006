using Godot;
using System;

public enum ReplicationMode
{
	Always,
	OnChange
}

public abstract class ReplicatedProperty
{
	public string Name { get; }
	public ReplicationMode Mode { get; }
	
	protected ReplicatedProperty(string name, ReplicationMode mode = ReplicationMode.Always)
	{
		Name = name;
		Mode = mode;
	}
	
	public abstract void Write(StreamPeerBuffer buffer);
	public abstract void Read(StreamPeerBuffer buffer);
	public abstract bool HasChanged();
	public abstract void MarkClean();
	public abstract int GetSizeBytes();
}

public class ReplicatedFloat : ReplicatedProperty
{
	private Func<float> _getter;
	private Action<float> _setter;
	private float _lastValue;
	private bool _dirty;
	
	public ReplicatedFloat(string name, Func<float> getter, Action<float> setter, ReplicationMode mode = ReplicationMode.Always)
		: base(name, mode)
	{
		_getter = getter;
		_setter = setter;
		_lastValue = getter();
		_dirty = true;
	}
	
	public override void Write(StreamPeerBuffer buffer)
	{
		var value = _getter();
		buffer.PutFloat(value);
		_lastValue = value;
		_dirty = false;
	}
	
	public override void Read(StreamPeerBuffer buffer)
	{
		var value = buffer.GetFloat();
		_setter(value);
		_lastValue = value;
		_dirty = false;
	}
	
	public override bool HasChanged()
	{
		if (_dirty)
			return true;
		var current = _getter();
		var changed = !Mathf.IsEqualApprox(current, _lastValue);
		if (changed)
			_dirty = true;
		return changed;
	}
	
	public override void MarkClean()
	{
		_lastValue = _getter();
		_dirty = false;
	}
	
	public override int GetSizeBytes() => 4;
}

public class ReplicatedVector3 : ReplicatedProperty
{
	private Func<Vector3> _getter;
	private Action<Vector3> _setter;
	private Vector3 _lastValue;
	private bool _dirty;
	
	public ReplicatedVector3(string name, Func<Vector3> getter, Action<Vector3> setter, ReplicationMode mode = ReplicationMode.Always)
		: base(name, mode)
	{
		_getter = getter;
		_setter = setter;
		_lastValue = getter();
		_dirty = true;
	}
	
	public override void Write(StreamPeerBuffer buffer)
	{
		var value = _getter();
		buffer.PutFloat(value.X);
		buffer.PutFloat(value.Y);
		buffer.PutFloat(value.Z);
		_lastValue = value;
		_dirty = false;
	}
	
	public override void Read(StreamPeerBuffer buffer)
	{
		var value = new Vector3(
			buffer.GetFloat(),
			buffer.GetFloat(),
			buffer.GetFloat()
		);
		_setter(value);
		_lastValue = value;
		_dirty = false;
	}
	
	public override bool HasChanged()
	{
		if (_dirty)
			return true;
		var current = _getter();
		var changed = !current.IsEqualApprox(_lastValue);
		if (changed)
			_dirty = true;
		return changed;
	}
	
	public override void MarkClean()
	{
		_lastValue = _getter();
		_dirty = false;
	}
	
	public override int GetSizeBytes() => 12;
}

public class ReplicatedTransform3D : ReplicatedProperty
{
	private Func<Transform3D> _getter;
	private Action<Transform3D> _setter;
	private Transform3D _lastValue;
	private bool _dirty;
	private float _positionThreshold;
	private float _rotationThreshold;
	
	public ReplicatedTransform3D(string name, Func<Transform3D> getter, Action<Transform3D> setter, 
		ReplicationMode mode = ReplicationMode.Always, float positionThreshold = 0.01f, float rotationThreshold = 0.01f)
		: base(name, mode)
	{
		_getter = getter;
		_setter = setter;
		_lastValue = getter();
		_dirty = true;
		_positionThreshold = positionThreshold;
		_rotationThreshold = rotationThreshold;
	}
	
	public override void Write(StreamPeerBuffer buffer)
	{
		var value = _getter();
		var origin = value.Origin;
		buffer.PutFloat(origin.X);
		buffer.PutFloat(origin.Y);
		buffer.PutFloat(origin.Z);
		
		var rotation = value.Basis.GetRotationQuaternion();
		buffer.PutFloat(rotation.X);
		buffer.PutFloat(rotation.Y);
		buffer.PutFloat(rotation.Z);
		buffer.PutFloat(rotation.W);
		
		_lastValue = value;
		_dirty = false;
	}
	
	public override void Read(StreamPeerBuffer buffer)
	{
		var origin = new Vector3(
			buffer.GetFloat(),
			buffer.GetFloat(),
			buffer.GetFloat()
		);
		var rotation = new Quaternion(
			buffer.GetFloat(),
			buffer.GetFloat(),
			buffer.GetFloat(),
			buffer.GetFloat()
		);
		var value = new Transform3D(new Basis(rotation), origin);
		_setter(value);
		_lastValue = value;
		_dirty = false;
	}
	
	public override bool HasChanged()
	{
		if (_dirty)
			return true;
		
		var current = _getter();
		var posChanged = current.Origin.DistanceTo(_lastValue.Origin) > _positionThreshold;
		var rotChanged = !current.Basis.GetRotationQuaternion().IsEqualApprox(_lastValue.Basis.GetRotationQuaternion());
		
		var changed = posChanged || rotChanged;
		if (changed)
			_dirty = true;
		return changed;
	}
	
	public override void MarkClean()
	{
		_lastValue = _getter();
		_dirty = false;
	}
	
	public override int GetSizeBytes() => 28;
}

public class ReplicatedInt : ReplicatedProperty
{
	private Func<int> _getter;
	private Action<int> _setter;
	private int _lastValue;
	private bool _dirty;
	
	public ReplicatedInt(string name, Func<int> getter, Action<int> setter, ReplicationMode mode = ReplicationMode.Always)
		: base(name, mode)
	{
		_getter = getter;
		_setter = setter;
		_lastValue = getter();
		_dirty = true;
	}
	
	public override void Write(StreamPeerBuffer buffer)
	{
		var value = _getter();
		buffer.PutU32((uint)value);
		_lastValue = value;
		_dirty = false;
	}
	
	public override void Read(StreamPeerBuffer buffer)
	{
		var value = (int)buffer.GetU32();
		_setter(value);
		_lastValue = value;
		_dirty = false;
	}
	
	public override bool HasChanged()
	{
		if (_dirty)
			return true;
		var current = _getter();
		var changed = current != _lastValue;
		if (changed)
			_dirty = true;
		return changed;
	}
	
	public override void MarkClean()
	{
		_lastValue = _getter();
		_dirty = false;
	}
	
	public override int GetSizeBytes() => 4;
}

