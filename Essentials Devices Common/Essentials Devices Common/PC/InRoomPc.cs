﻿using System;
using System.Collections.Generic;
using System.Linq;
using Crestron.SimplSharpPro;

using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Routing;
using PepperDash.Core;

namespace PepperDash.Essentials.Devices.Common
{
	/// <summary>
	/// This DVD class should cover most IR, one-way DVD and Bluray fuctions
	/// </summary>
	public class InRoomPc : Device, IHasFeedback, IRoutingOutputs, IAttachVideoStatus, IUiDisplayInfo
	{
		public uint DisplayUiType { get { return DisplayUiConstants.TypeLaptop; } }
		public string IconName { get; set; }
		public BoolFeedback HasPowerOnFeedback { get; private set; }

		public RoutingOutputPort AnyVideoOut { get; private set; }

		#region IRoutingOutputs Members

		/// <summary>
		/// Options: hdmi
		/// </summary>
		public RoutingPortCollection<RoutingOutputPort> OutputPorts { get; private set; }

		#endregion

		public InRoomPc(string key, string name)
			: base(key, name)
		{
			IconName = "PC";
			HasPowerOnFeedback = new BoolFeedback(CommonBoolCue.HasPowerFeedback, 
				() => this.GetVideoStatuses() != VideoStatusOutputs.NoStatus);
			OutputPorts = new RoutingPortCollection<RoutingOutputPort>();
			OutputPorts.Add(AnyVideoOut = new RoutingOutputPort(RoutingPortNames.AnyVideoOut, eRoutingSignalType.AudioVideo, 
				eRoutingPortConnectionType.None, 0, this));
		}

		#region IHasFeedback Members

		/// <summary>
		/// Passes through the VideoStatuses list
		/// </summary>
		public List<Feedback> Feedbacks
		{
			get { return this.GetVideoStatuses().ToList(); }
		}

		#endregion
	}
}