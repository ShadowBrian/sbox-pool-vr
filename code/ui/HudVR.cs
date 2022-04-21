
using Sandbox;
using Sandbox.UI;
using Sandbox.UI.Construct;
using System;
using System.Threading.Tasks;

namespace Facepunch.Pool
{
	[Library]
	public partial class HudVR : WorldPanel
	{
		public Panel Header { get; private set; }
		public Panel Footer { get; private set; }

		public HudVR()
		{
			StyleSheet.Load( "/ui/Hud.scss" );

			AddChild<RoundInfo>();
			AddChild<VoiceList>();
			AddChild<ChatBox>();

			Header = Add.Panel( "header" );
			Header.AddChild<PlayerDisplay>();

			Footer = Add.Panel( "footer" );
			Footer.AddChild<ToastList>();
			Footer.AddChild<BallHistory>();

			AddChild<LoadingScreen>();
			AddChild<CursorController>();
		}

		public override void Tick()
		{
			base.Tick();

			Transform = Input.VR.LeftHand.Transform.WithRotation( Input.VR.LeftHand.Transform.Rotation * new Angles( -85f, 180f, 0 ).ToRotation() ).WithPosition( Input.VR.LeftHand.Transform.Position + Input.VR.LeftHand.Transform.Rotation.Up * 1f + Input.VR.LeftHand.Transform.Rotation.Forward * -3f + Input.VR.LeftHand.Transform.Rotation.Right * (11f) ).WithScale( 0.165f );

			PanelBounds = new Rect( -1920 / 2f, -1080 / 2f, 1920, 1080 );
		}
	}
}
