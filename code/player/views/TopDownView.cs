﻿using Sandbox;
using Sandbox.UI;
using System;

namespace PoolGame
{
	public class TopDownView : BaseView
	{
		public float CueRoll { get; set; }
		public float CueYaw { get; set; }
		public bool IsMakingShot { get; set; }
		public float ShotPower { get; set; }

		private float _cuePullBackOffset;
		private float _lastPowerDistance;
		private float _maxCueRoll = 35f;
		private float _minCueRoll = 5f;

		public override void UpdateCamera( PoolCamera camera )
		{
			camera.Pos = camera.Pos.LerpTo( Viewer.WorldPos, Time.Delta );
			camera.Rot = Rotation.Lerp( camera.Rot, Viewer.WorldRot, Time.Delta );
		}

		public override void Tick()
		{
			var zoomOutDistance = 900f;

			if ( Viewer.Input.Down( InputButton.Attack1 ) )
				zoomOutDistance = 750f;

			Viewer.WorldPos = new Vector3( 0f, 0f, zoomOutDistance );
			Viewer.WorldRot = Rotation.LookAt( Vector3.Down );

			if ( !Viewer.IsTurn || Viewer.IsFollowingBall )
				return;

			var whiteBall = Game.Instance.WhiteBall;
			var input = Viewer.Input;
			var cue = Viewer.Cue;

			if ( !whiteBall.IsValid || !cue.IsValid )
				return;

			if ( Viewer.IsPlacingWhiteBall )
			{
				HandleWhiteBallPlacement( input, whiteBall );
				ShowWhiteArea( true );
				return;
			}
			else
			{
				ShowWhiteArea( false );
			}

			if ( !input.Down( InputButton.Attack1 ) )
			{
				if ( !IsMakingShot )
				{
					RotateCueToCursor( input, whiteBall, cue );
				}
				else
				{
					TakeShot( cue, whiteBall );
					return;
				}
			}
			else
			{
				HandlePowerSelection( input, cue );
			}

			cue.Entity.WorldPos = whiteBall.Entity.WorldPos - cue.Entity.WorldRot.Left * (250f + _cuePullBackOffset + (CueRoll * 0.3f));

			var trace = Trace.Ray( whiteBall.Entity.WorldPos, whiteBall.Entity.WorldPos + cue.Entity.DirectionTo( whiteBall.Entity) * 1000f )
				.Ignore( whiteBall.Entity )
				.Ignore( cue.Entity )
				.Run();

			DebugOverlay.Sphere( trace.EndPos, 10f, Color.White );
			DebugOverlay.Line( trace.StartPos, trace.EndPos, Color.White );
		}

		private void ShowWhiteArea( bool shouldShow )
		{
			if ( Host.IsServer ) return;

			var whiteArea = Game.Instance.WhiteArea;

			if ( whiteArea != null && whiteArea.IsValid() )
				whiteArea.Quad.IsEnabled = shouldShow;
		}

		private void TakeShot( EntityHandle<PoolCue> cue, EntityHandle<PoolBall> whiteBall )
		{
			IsMakingShot = false;

			if ( ShotPower >= 5f )
			{
				using ( Prediction.Off() )
				{
					Log.Info( "Shot Power: " + ShotPower );
					Viewer.StrikeWhiteBall( cue, whiteBall, ShotPower * 50f );
				}
			}
		}

		private void HandleWhiteBallPlacement( UserInput input, EntityHandle<PoolBall> whiteBall )
		{
			if ( Host.IsClient ) return;

			var cursorTrace = Trace.Ray( Viewer.EyePos, Viewer.EyePos + input.CursorAim * 1000f )
				.WorldOnly()
				.Run();

			var whiteArea = Game.Instance.WhiteArea;
			var whiteAreaWorldOBB = whiteArea.CollisionBounds.ToWorldSpace( whiteArea );

			whiteBall.Entity.TryMoveTo( cursorTrace.EndPos, whiteAreaWorldOBB );

			if ( input.Released( InputButton.Attack1 ) )
				Viewer.StopPlacingWhiteBall();
		}

		private void HandlePowerSelection( UserInput input, EntityHandle<PoolCue> cue )
		{
			var cursorTrace = Trace.Ray( Viewer.EyePos, Viewer.EyePos + input.CursorAim * 1000f )
				.Run();

			var distanceToCue = cursorTrace.EndPos.Distance( cue.Entity.WorldPos );
			var cuePullBackDelta = (_lastPowerDistance - distanceToCue) * Time.Delta * 20f;

			if ( !IsMakingShot )
				cuePullBackDelta = 0f;

			_cuePullBackOffset = Math.Clamp( _cuePullBackOffset + cuePullBackDelta, 0f, 50f );
			_lastPowerDistance = distanceToCue;

			IsMakingShot = true;
			ShotPower = _cuePullBackOffset.AsPercentMinMax( 0f, 50f );
		}

		private void RotateCueToCursor( UserInput input, EntityHandle<PoolBall> whiteBall, EntityHandle<PoolCue> cue )
		{
			var direction = cue.Entity.DirectionTo( whiteBall );
			var rollTrace = Trace.Ray( whiteBall.Entity.WorldPos, whiteBall.Entity.WorldPos - direction * 100f )
				.Ignore( cue )
				.Ignore( whiteBall )
				.Run();

			var cursorTrace = Trace.Ray( Viewer.EyePos, Viewer.EyePos + input.CursorAim * 1000f )
				.WorldOnly()
				.Run();

			var cursorDir = (whiteBall.Entity.WorldPos - cursorTrace.EndPos).TemporaryNormalFix();
			var cursorRot = Rotation.LookAt( cursorDir, Vector3.Forward );

			_cuePullBackOffset = _cuePullBackOffset.LerpTo( 0f, Time.Delta * 10f );

			CueRoll = CueRoll.LerpTo( _minCueRoll + ((_maxCueRoll - _minCueRoll) * (1f - rollTrace.Fraction)), Time.Delta * 10f );
			CueYaw = ( cursorRot.Yaw() + 90f ).Normalize( 0f, 360f );

			cue.Entity.WorldRot = Rotation.From(
				cue.Entity.WorldRot.Angles()
					.WithYaw( CueYaw )
					.WithRoll( -CueRoll )
			);
		}

		public override void OnWhiteBallStruck( PoolCue cue, PoolBall whiteBall, float force )
		{
			_cuePullBackOffset = 0f;
		}
	}
}
