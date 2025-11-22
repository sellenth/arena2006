using Godot;

public enum PlayerMovementStateKind
{
        Grounded,
        Airborne,
        Sliding,
        WallRunning
}

public readonly struct PlayerMovementContext
{
        public PlayerMovementContext(
                Vector3 velocity,
                Vector2 moveInput,
                bool jump,
                bool sprint,
                bool crouch,
                bool onFloor,
                bool onWall,
                Vector3 wallNormal,
                Basis referenceBasis)
        {
                Velocity = velocity;
                MoveInput = moveInput;
                Jump = jump;
                Sprint = sprint;
                Crouch = crouch;
                OnFloor = onFloor;
                OnWall = onWall;
                WallNormal = wallNormal;
                ReferenceBasis = referenceBasis;
        }

        public Vector3 Velocity { get; }
        public Vector2 MoveInput { get; }
        public bool Jump { get; }
        public bool Sprint { get; }
        public bool Crouch { get; }
        public bool OnFloor { get; }
        public bool OnWall { get; }
        public Vector3 WallNormal { get; }
        public Basis ReferenceBasis { get; }
}

public sealed class PlayerMovementSettings
{
        public float Gravity = 9.8f;
        public float JumpVelocity = 3f;
        public float GroundAcceleration = 40f;
        public float GroundSpeedLimit = 60f;
        public float GroundFriction = 0.9f;
        public float SprintSpeedMultiplier = 1.2f;
        public float AirAcceleration = 80f;
        public float AirSpeedLimit = 0.8f;
        public float AirDrag = 0f;

        public float SlideEnterSpeed = 8f;
        public float SlideDuration = 1f;
        public float SlideFriction = 3f;
        public float SlideSteerResponsiveness = 10f;
        public float SlideExitSpeed = 3f;

        public float WallRunGravityScale = 0.35f;
        public float WallRunAcceleration = 18f;
        public float WallRunSpeedLimit = 14f;
        public float WallRunDuration = 1.2f;
        public float WallJumpUpImpulse = 6f;
        public float WallJumpNormalImpulse = 5f;

        public static PlayerMovementSettings CreateDefaults()
        {
                return new PlayerMovementSettings();
        }
}

public sealed class PlayerMovementController
{
        private readonly PlayerMovementSettings _settings;
        private readonly IMovementState _groundState;
        private readonly IMovementState _airState;
        private readonly IMovementState _slideState;
        private readonly IMovementState _wallRunState;
        private IMovementState _currentState;
        private PlayerMovementContext _lastContext;

        public PlayerMovementController(PlayerMovementSettings settings = null)
        {
                _settings = settings ?? PlayerMovementSettings.CreateDefaults();
                _groundState = new GroundedState(this);
                _airState = new AirborneState(this);
                _slideState = new SlideState(this);
                _wallRunState = new WallRunState(this);
                _currentState = _airState;
        }

        public PlayerMovementStateKind State => _currentState.StateKind;
        public bool JumpedThisFrame { get; private set; }
        public float LastPlanarSpeed { get; private set; }
        public Vector3 LastWallNormal { get; internal set; } = Vector3.Zero;

        public Vector3 Step(in PlayerMovementContext context, float delta)
        {
                JumpedThisFrame = false;
                _lastContext = context;

                if (_currentState.StateKind != PlayerMovementStateKind.Sliding
                        && _currentState.StateKind != PlayerMovementStateKind.WallRunning)
                {
                        SwitchState(context.OnFloor ? PlayerMovementStateKind.Grounded : PlayerMovementStateKind.Airborne, context);
                }

                LastWallNormal = context.OnWall ? context.WallNormal : Vector3.Zero;

                var velocity = _currentState.Tick(context, delta);
                LastPlanarSpeed = new Vector3(velocity.X, 0f, velocity.Z).Length();
                return velocity;
        }

        public void Reset()
        {
                _currentState = _airState;
                JumpedThisFrame = false;
                LastPlanarSpeed = 0f;
                LastWallNormal = Vector3.Zero;
        }

        internal void MarkJumped()
        {
                MarkJumped(_lastContext);
        }

        internal void MarkJumped(in PlayerMovementContext context)
        {
                JumpedThisFrame = true;
                SwitchState(PlayerMovementStateKind.Airborne, context);
        }

        internal void RequestState(PlayerMovementStateKind target, in PlayerMovementContext context)
        {
                SwitchState(target, context);
        }

        internal Vector3 ApplyAcceleration(Vector3 velocity, Vector3 wishDir, float speedLimit, float acceleration, float delta)
        {
                if (wishDir == Vector3.Zero || speedLimit <= 0f || acceleration <= 0f)
                        return velocity;

                var currentSpeed = velocity.Dot(wishDir);
                var addSpeed = speedLimit - currentSpeed;
                if (addSpeed <= 0f)
                        return velocity;

                var accelSpeed = acceleration * delta;
                if (accelSpeed > addSpeed)
                        accelSpeed = addSpeed;

                return velocity + wishDir * accelSpeed;
        }

        internal Vector3 BuildWishDirection(Basis basis, Vector2 input)
        {
                if (input == Vector2.Zero)
                        return Vector3.Zero;

                var forward = basis.Z;
                forward.Y = 0f;
                if (!forward.IsZeroApprox())
                        forward = forward.Normalized();

                var right = basis.X;
                right.Y = 0f;
                if (!right.IsZeroApprox())
                        right = right.Normalized();

                var wish = forward * input.Y + right * input.X;
                if (wish.LengthSquared() > 1f)
                        wish = wish.Normalized();
                return wish;
        }

        private void SwitchState(PlayerMovementStateKind target, in PlayerMovementContext context)
        {
                var desired = GetState(target);
                if (_currentState == desired)
                        return;

                var previous = _currentState.StateKind;
                _currentState = desired;
                desired.OnEnter(context, previous);
        }

        private IMovementState GetState(PlayerMovementStateKind target)
        {
                return target switch
                {
                        PlayerMovementStateKind.Grounded => _groundState,
                        PlayerMovementStateKind.Sliding => _slideState,
                        PlayerMovementStateKind.WallRunning => _wallRunState,
                        _ => _airState
                };
        }

        private interface IMovementState
        {
                PlayerMovementStateKind StateKind { get; }
                void OnEnter(in PlayerMovementContext context, PlayerMovementStateKind previousState);
                Vector3 Tick(in PlayerMovementContext context, float delta);
        }

        private sealed class GroundedState : IMovementState
        {
                private readonly PlayerMovementController _controller;
                private readonly PlayerMovementSettings _settings;

                public GroundedState(PlayerMovementController controller)
                {
                        _controller = controller;
                        _settings = controller._settings;
                }

                public PlayerMovementStateKind StateKind => PlayerMovementStateKind.Grounded;

                public void OnEnter(in PlayerMovementContext context, PlayerMovementStateKind previousState)
                {
                }

                public Vector3 Tick(in PlayerMovementContext context, float delta)
                {
                        var velocity = context.Velocity;
                        var planar = new Vector3(velocity.X, 0f, velocity.Z);
                        var planarSpeed = planar.Length();

                        if (context.Crouch && planarSpeed >= _settings.SlideEnterSpeed)
                        {
                                _controller.RequestState(PlayerMovementStateKind.Sliding, context);
                                return _controller._currentState.Tick(context, delta);
                        }

                        if (context.Jump)
                        {
                                velocity.Y = _settings.JumpVelocity;
                                _controller.MarkJumped(context);
                                return velocity;
                        }

                        var friction = Mathf.Clamp(_settings.GroundFriction, 0f, 1f);
                        velocity.X *= friction;
                        velocity.Z *= friction;

                        var wishDir = _controller.BuildWishDirection(context.ReferenceBasis, context.MoveInput);
                        var speedLimit = _settings.GroundSpeedLimit * (context.Sprint ? _settings.SprintSpeedMultiplier : 1f);
                        var acceleration = _settings.GroundAcceleration * (context.Sprint ? _settings.SprintSpeedMultiplier : 1f);
                        velocity = _controller.ApplyAcceleration(velocity, wishDir, speedLimit, acceleration, delta);

                        return velocity;
                }
        }

        private sealed class AirborneState : IMovementState
        {
                private readonly PlayerMovementController _controller;
                private readonly PlayerMovementSettings _settings;

                public AirborneState(PlayerMovementController controller)
                {
                        _controller = controller;
                        _settings = controller._settings;
                }

                public PlayerMovementStateKind StateKind => PlayerMovementStateKind.Airborne;

                public void OnEnter(in PlayerMovementContext context, PlayerMovementStateKind previousState)
                {
                }

                public Vector3 Tick(in PlayerMovementContext context, float delta)
                {
                        if (context.OnWall && context.MoveInput != Vector2.Zero)
                        {
                                _controller.RequestState(PlayerMovementStateKind.WallRunning, context);
                                return _controller._currentState.Tick(context, delta);
                        }

                        var velocity = context.Velocity;
                        velocity.Y -= _settings.Gravity * delta;

                        var wishDir = _controller.BuildWishDirection(context.ReferenceBasis, context.MoveInput);
                        velocity = _controller.ApplyAcceleration(velocity, wishDir, _settings.AirSpeedLimit, _settings.AirAcceleration, delta);

                        var drag = Mathf.Clamp(_settings.AirDrag * delta, 0f, 1f);
                        velocity.X *= 1f - drag;
                        velocity.Z *= 1f - drag;

                        return velocity;
                }
        }

        private sealed class SlideState : IMovementState
        {
                private readonly PlayerMovementController _controller;
                private readonly PlayerMovementSettings _settings;
                private float _slideTimer;
                private Vector3 _slideDirection = Vector3.Forward;
                private float _slideSpeed;

                public SlideState(PlayerMovementController controller)
                {
                        _controller = controller;
                        _settings = controller._settings;
                }

                public PlayerMovementStateKind StateKind => PlayerMovementStateKind.Sliding;

                public void OnEnter(in PlayerMovementContext context, PlayerMovementStateKind previousState)
                {
                        var planar = new Vector3(context.Velocity.X, 0f, context.Velocity.Z);
                        _slideSpeed = Mathf.Max(planar.Length(), _settings.SlideEnterSpeed);
                        _slideDirection = !planar.IsZeroApprox() ? planar.Normalized() : _controller.BuildWishDirection(context.ReferenceBasis, context.MoveInput);
                        if (_slideDirection == Vector3.Zero)
                                _slideDirection = -context.ReferenceBasis.Z;
                        _slideTimer = _settings.SlideDuration;
                }

                public Vector3 Tick(in PlayerMovementContext context, float delta)
                {
                        _slideTimer -= delta;

                        if (!context.OnFloor || _slideTimer <= 0f)
                        {
                                _controller.RequestState(context.OnFloor ? PlayerMovementStateKind.Grounded : PlayerMovementStateKind.Airborne, context);
                                return _controller._currentState.Tick(context, delta);
                        }

                        var wishDir = _controller.BuildWishDirection(context.ReferenceBasis, context.MoveInput);
                        if (wishDir != Vector3.Zero)
                        {
                                var steer = 1f - Mathf.Exp(-_settings.SlideSteerResponsiveness * delta);
                                _slideDirection = _slideDirection.Lerp(wishDir, steer).Normalized();
                        }

                        var decay = Mathf.Exp(-_settings.SlideFriction * delta);
                        _slideSpeed *= decay;
                        _slideSpeed = Mathf.Max(_slideSpeed, _settings.SlideExitSpeed);

                        if (!context.Crouch && _slideSpeed <= _settings.SlideExitSpeed * 1.1f)
                        {
                                _controller.RequestState(PlayerMovementStateKind.Grounded, context);
                                return _controller._currentState.Tick(context, delta);
                        }

                        var velocity = _slideDirection * _slideSpeed;
                        velocity.Y = 0f;
                        return velocity;
                }
        }

        private sealed class WallRunState : IMovementState
        {
                private readonly PlayerMovementController _controller;
                private readonly PlayerMovementSettings _settings;
                private float _wallRunTimer;
                private Vector3 _wallNormal = Vector3.Zero;

                public WallRunState(PlayerMovementController controller)
                {
                        _controller = controller;
                        _settings = controller._settings;
                }

                public PlayerMovementStateKind StateKind => PlayerMovementStateKind.WallRunning;

                public void OnEnter(in PlayerMovementContext context, PlayerMovementStateKind previousState)
                {
                        _wallNormal = context.WallNormal;
                        _wallRunTimer = _settings.WallRunDuration;
                        _controller.LastWallNormal = _wallNormal;
                }

                public Vector3 Tick(in PlayerMovementContext context, float delta)
                {
                        _controller.LastWallNormal = context.OnWall ? context.WallNormal : _wallNormal;
                        _wallRunTimer -= delta;

                        if (!context.OnWall || _wallRunTimer <= 0f)
                        {
                                _controller.RequestState(context.OnFloor ? PlayerMovementStateKind.Grounded : PlayerMovementStateKind.Airborne, context);
                                return _controller._currentState.Tick(context, delta);
                        }

                        var velocity = context.Velocity;
                        velocity.Y -= _settings.Gravity * _settings.WallRunGravityScale * delta;

                        if (context.Jump)
                        {
                                velocity += _wallNormal * _settings.WallJumpNormalImpulse + Vector3.Up * _settings.WallJumpUpImpulse;
                                _controller.MarkJumped(context);
                                return velocity;
                        }

                        var wishDir = _controller.BuildWishDirection(context.ReferenceBasis, context.MoveInput);
                        var alongWall = wishDir - _wallNormal * wishDir.Dot(_wallNormal);
                        if (alongWall.IsZeroApprox())
                        {
                                var wallForward = _wallNormal.Cross(Vector3.Up);
                                alongWall = wallForward.Cross(_wallNormal);
                        }
                        alongWall = alongWall.Normalized();

                        velocity = _controller.ApplyAcceleration(velocity, alongWall, _settings.WallRunSpeedLimit, _settings.WallRunAcceleration, delta);
                        velocity = velocity.Slide(_wallNormal);
                        return velocity;
                }
        }
}
