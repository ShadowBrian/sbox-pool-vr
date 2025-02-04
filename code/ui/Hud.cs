﻿
using Sandbox;
using Sandbox.UI;
using Sandbox.UI.Construct;
using System;
using System.Threading.Tasks;

namespace Facepunch.Pool
{
	[Library]
	public partial class Hud : RootPanel
	{
		public Panel Header { get; private set; }
		public Panel Footer { get; private set; }

		public Hud()
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
	}
}
