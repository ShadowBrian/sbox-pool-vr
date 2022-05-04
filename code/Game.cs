﻿using Sandbox;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Facepunch.Pool
{
	public struct PotHistoryItem
	{
		public PoolBallNumber Number;
		public PoolBallType Type;
	}

	[Library( "pool", Title = "Pool" )]
	partial class Game : Sandbox.Game
	{
		public TriggerWhiteArea WhiteArea { get; set; }
		public Player PreviousWinner { get; set; }
		public Player PreviousLoser { get; set; }
		public List<PoolBall> AllBalls { get; private set; }
		public Hud Hud { get; set; }

		public static Game Instance
		{
			get => Current as Game;
		}

		[Net] public PoolCue Cue { get; private set; }
		[Net] public BaseRound Round { get; private set; }
		[Net] public PoolBall WhiteBall { get; set; }
		[Net] public PoolBall BlackBall { get; set; }
		[Net] public Player CurrentPlayer { get; set; }
		[Net] public Player PlayerOne { get; set; }
		[Net] public Player PlayerTwo { get; set; }
		[Net] public IList<PotHistoryItem> PotHistory { get; set; }
		[Net, Change] public bool IsFastForwarding { get; set; }

		private FastForward _fastForwardHud;
		private WinSummary _winSummaryHud;
		private Dictionary<long, int> _ratings;
		private BaseRound _lastRound;

		[ServerVar( "pool_min_players", Help = "The minimum players required to start." )]
		public static int MinPlayers { get; set; } = 2;

		public Game()
		{
			if ( IsServer )
			{
				LoadRatings();
			}

			if ( IsClient )
			{
				if ( !Global.IsRunningInVR )
				{
					Local.Hud = new Hud();
				}
				else
				{
					Local.Hud = new HudVR();
				}
			}

			Global.PhysicsSubSteps = 10;
		}
		public override void PostCameraSetup( ref CameraSetup camSetup )
		{
			if ( Local.Pawn != null )
			{
				// VR anchor default is at the pawn's location
				//VR.Anchor = Local.Pawn.Transform;

				Local.Pawn.PostCameraSetup( ref camSetup );
			}

			if ( Input.VR.IsActive )
				camSetup.ZNear = 2.5f;
			//
			// Position any viewmodels
			//
			BaseViewModel.UpdateAllPostCamera( ref camSetup );

			CameraModifier.Apply( ref camSetup );
		}

		public async Task RespawnBallAsync( PoolBall ball, bool shouldAnimate = false )
		{
			if ( shouldAnimate )
				await ball.AnimateIntoPocket();

			var entities = All.Where( ( e ) => e is PoolBallSpawn );

			foreach ( var entity in entities )
			{
				if ( entity is PoolBallSpawn spawner )
				{
					if ( spawner.Type == ball.Type && spawner.Number == ball.Number )
					{
						ball.Scale = 1f;
						ball.Position = spawner.Position;
						ball.RenderColor = ball.RenderColor.WithAlpha( 1.0f );
						ball.PhysicsBody.AngularVelocity = Vector3.Zero;
						ball.PhysicsBody.Velocity = Vector3.Zero;
						ball.PhysicsBody.ClearForces();
						ball.ResetInterpolation();

						return;
					}
				}
			}
		}

		public async Task RemoveBallAsync( PoolBall ball, bool shouldAnimate = false )
		{
			if ( shouldAnimate )
				await ball.AnimateIntoPocket();

			AllBalls.Remove( ball );
			ball.Delete();
		}

		[ClientRpc]
		public void ShowWinSummary( EloOutcome outcome, Player opponent )
		{
			HideWinSummary();

			_winSummaryHud = Local.Hud.AddChild<WinSummary>();
			_winSummaryHud.Update( outcome, opponent );
		}

		[ClientRpc]
		public void HideWinSummary()
		{
			if ( _winSummaryHud != null )
			{
				_winSummaryHud.Delete();
				_winSummaryHud = null;
			}
		}

		[ClientRpc]
		public void AddToast( Player player, string text, string iconClass = "" )
		{
			if ( player == null )
			{
				Log.Warning( "Player was NULL in Game.AddToast!" );
				return;
			}

			ToastList.Current.AddItem( player, text, iconClass );
		}

		public void RemoveAllBalls()
		{
			if ( AllBalls != null )
			{
				foreach ( var entity in AllBalls )
					entity.Delete();

				AllBalls.Clear();
			}
			else
				AllBalls = new();
		}

		public Player GetBallPlayer( PoolBall ball )
		{
			if ( PlayerOne.BallType == ball.Type )
				return PlayerOne;
			else if ( PlayerTwo.BallType == ball.Type )
				return PlayerTwo;
			else
				return null;
		}

		public Player GetOtherPlayer( Player player )
		{
			if ( player == PlayerOne )
				return PlayerTwo;
			else
				return PlayerOne;
		}

		public void UpdatePotHistory()
		{
			if ( BallHistory.Current != null )
			{
				BallHistory.Current.Clear();

				foreach ( var item in PotHistory )
					BallHistory.Current.AddByType( item.Type, item.Number );
			}
		}

		public void RespawnAllBalls()
		{
			RemoveAllBalls();

			var entities = All.Where( ( e ) => e is PoolBallSpawn );

			Log.Warning( "Found " + entities.Count() + " Ball Spawners" );

			var spawners = new List<Entity>();
			spawners.AddRange( entities );

			foreach ( var entity in spawners )
			{
				if ( entity is PoolBallSpawn spawner )
				{
					var ball = new PoolBall
					{
						Position = spawner.Position,
						Rotation = Rotation.LookAt( Vector3.Random )
					};

					ball.SetType( spawner.Type, spawner.Number );

					if ( ball.Type == PoolBallType.White )
						WhiteBall = ball;
					else if ( ball.Type == PoolBallType.Black )
						BlackBall = ball;

					AllBalls.Add( ball );
				}
				else
				{
					Log.Warning( entity.EngineEntityName + " was not a spawner!" );
				}
			}
		}

		public void UpdateRating( Player player )
		{
			_ratings[player.Client.PlayerId] = player.Elo.Rating;
		}

		public void SaveRatings()
		{
			//FileSystem.Mounted.WriteAllText( "data/pool/ratings.json", JsonSerializer.Serialize( _ratings ) );
		}

		public void ChangeRound( BaseRound round )
		{
			Assert.NotNull( round );

			Round?.Finish();
			Round = round;
			Round?.Start();
		}

		public async Task StartSecondTimer()
		{
			while ( true )
			{
				await Task.DelaySeconds( 1 );
				OnSecond();
			}
		}

		public override void DoPlayerNoclip( Client client )
		{
			// Do nothing. The player can't noclip in this mode.
		}

		public override void DoPlayerSuicide( Client client )
		{
			if ( client.Pawn.LifeState == LifeState.Alive && Round?.CanPlayerSuicide == true )
			{
				// This simulates the player being killed.
				client.Pawn.LifeState = LifeState.Dead;
				client.Pawn.OnKilled();
				OnKilled( client.Pawn );
			}
		}

		public override void PostLevelLoaded()
		{
			_ = StartSecondTimer();

			if ( IsServer )
			{
				var cue = new PoolCue();
				Cue = cue;
			}

			base.PostLevelLoaded();
		}

		public override void OnKilled( Entity entity )
		{
			if ( entity is Player player )
				Round?.OnPlayerKilled( player );

			base.OnKilled( entity );
		}

		public override void ClientDisconnect( Client client, NetworkDisconnectionReason reason )
		{
			Log.Info( client.Name + " left, checking minimum player count..." );

			Round?.OnPlayerLeave( client.Pawn as Player );

			base.ClientDisconnect( client, reason );
		}

		public override void ClientJoined( Client client )
		{
			var player = new Player();

			if ( _ratings.TryGetValue( client.PlayerId, out var rating ) )
				player.Elo.Rating = rating;

			client.Pawn = player;

			if ( Input.VR.IsActive )
			{
				player.VRPlayer = new VRPlayer();
				player.VRPlayer.Owner = player;
				player.VRPlayer.Predictable = true;
				player.VRPlayer.Position = Vector3.Zero - Vector3.Up * 33f;
			}

			Round?.OnPlayerJoin( player );

			base.ClientJoined( client );
		}

		public override void Simulate( Client client )
		{
			if ( Cue != null && Cue.IsValid() && Cue.Client == client )
				Cue.Simulate( client );

			base.Simulate( client );
		}

		private void OnIsFastForwardingChanged( bool oldValue, bool newValue )
		{
			if ( _fastForwardHud != null )
			{
				_fastForwardHud.Delete();
				_fastForwardHud = null;
			}

			if ( newValue )
				_fastForwardHud = Local.Hud.AddChild<FastForward>();
		}

		private void OnSecond()
		{
			CheckMinimumPlayers();
			Round?.OnSecond();
		}

		[Event( EventType.Tick )]
		private void Tick()
		{
			Round?.OnTick();

			Global.PhysicsTimeScale = IsFastForwarding ? 5f : 1f;

			if ( IsClient )
			{
				// We have to hack around this for now until we can detect changes in net variables.
				if ( _lastRound != Round )
				{
					_lastRound?.Finish();
					_lastRound = Round;
					_lastRound.Start();
				}

				if ( WhiteArea == null )
				{
					WhiteArea = All.OfType<TriggerWhiteArea>().FirstOrDefault();

					if ( WhiteArea != null )
						WhiteArea.MakeAreaQuad();
				}

				if ( Time.Tick % 30 == 0 )
				{
					// TODO: This is temporary, only update history when the list changes.
					UpdatePotHistory();
				}
			}
		}

		private void LoadRatings()
		{
			_ratings = FileSystem.Mounted.ReadJsonOrDefault<Dictionary<long, int>>( "data/pool/ratings.json" ) ?? new();
		}

		private void CheckMinimumPlayers()
		{
			if ( Client.All.Count >= MinPlayers )
			{
				if ( Round is LobbyRound || Round == null )
				{
					ChangeRound( new PlayRound() );
				}
			}
			else if ( Round is not LobbyRound )
			{
				ChangeRound( new LobbyRound() );
			}
		}
	}
}
