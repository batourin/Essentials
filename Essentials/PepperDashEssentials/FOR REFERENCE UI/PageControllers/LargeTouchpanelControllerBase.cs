﻿//using System;
//using System.Collections.Generic;
//using System.Linq;
//using Crestron.SimplSharp;
//using Crestron.SimplSharpPro;
//using Crestron.SimplSharpPro.DeviceSupport;
//using Crestron.SimplSharpPro.UI;

//using PepperDash.Core;


//namespace PepperDash.Essentials.Core
//{
//    /// <summary>
//    /// 
//    /// </summary>
//    public class LargeTouchpanelControllerBase : SmartGraphicsTouchpanelControllerBase
//    {
//        public string PresentationShareButtonInVideoText = "Share";
//        public string PresentationShareButtonNotInVideoText = "Presentation";

//        SourceListSubpageReferenceList SourceSelectSRL;
//        DevicePageControllerBase CurrentPresentationSourcePageController;

//        public LargeTouchpanelControllerBase(string key, string name,
//            BasicTriListWithSmartObject triList, string sgdFilePath)
//            : base(key, name, triList, sgdFilePath)
//        {
//        }

//        /// <summary>
//        /// Static factory method
//        /// </summary>
//        public static LargeTouchpanelControllerBase GetController(string key, string name,
//            string type, CrestronTswPropertiesConfig props)
//        {
//            var id = Convert.ToUInt32(props.IpId, 16);
//            type = type.ToLower();
//            Tswx52ButtonVoiceControl tsw = null;
//            if (type == "tsw752")
//                tsw = new Tsw752(id, Global.ControlSystem);
//            else if (type == "tsw1052")
//                tsw = new Tsw1052(id, Global.ControlSystem);
//            else
//            {
//                Debug.Console(0, "WARNING: Cannot create TSW controller with type '{0}'", type);
//                return null;
//            }

//            var sgdPath = string.Format(@"\NVRAM\Program{0}\SGD\{1}", 
//                InitialParametersClass.ApplicationNumber, props.SgdFile);
//            var controller = new LargeTouchpanelControllerBase(key, name, tsw, sgdPath);
//            controller.UsesSplashPage = props.UsesSplashPage;
			
//            // Get the room and add it after everthing is ready
//            var room = DeviceManager.GetDeviceForKey(props.DefaultRoomKey) as EssentialsRoom;
//            controller.AddPostActivationAction(() =>
//                {
//                    controller.SetCurrentRoom(room);

//                });

//            return controller;
//        }

//        public override bool CustomActivate()
//        {
//            var baseSuccess = base.CustomActivate();
//            if (!baseSuccess) return false;

//            SourceSelectSRL = new SourceListSubpageReferenceList(this.TriList, n =>
//                { if (CurrentRoom != null) CurrentRoom.SelectSource(n); });

//            var lm = Global.LicenseManager;
//            if (lm != null)
//            {
//                lm.LicenseIsValid.LinkInputSig(TriList.BooleanInput[UiCue.ShowLicensed.Number]);
//                //others
//            }

//            // Wire up buttons
//            TriList.SetSigFalseAction(15003, () => SetMainMode(eMainModeType.Presentation));
//            TriList.SetSigFalseAction(15008, PowerOffWithConfirmPressed);
//            TriList.SetSigFalseAction(15101, () => SetMainMode(eMainModeType.Presentation));
//            TriList.SetSigFalseAction(15013, ShowHelp);
//            TriList.SetSigFalseAction(15014, () => SetMainMode(eMainModeType.Tech));

//            // Temp things -----------------------------------------------------------------------
//            TriList.StringInput[UiCue.SplashMessage.Number].StringValue = SplashMessage;
//            //------------------------------------------------------------------------------------

//            // Initialize initial view
//            ShowSplashOrMain();
//            return true;
//        }

//        /// <summary>
//        /// In Essentials, this should NEVER be called, since it's a one-room solution
//        /// </summary>
//        protected override void HideRoomUI()
//        {
//            // UI Cleanup here????

//            //SwapAudioDeviceControls(CurrentRoom.CurrentAudioDevice, null);
//            //CurrentRoom.AudioDeviceWillChange -= CurrentRoom_AudioDeviceWillChange;

//            CurrentRoom.IsCoolingDownFeedback.OutputChange -= CurrentRoom_IsCoolingDown_OutputChange;
//            CurrentRoom.IsWarmingUpFeedback.OutputChange -= CurrentRoom_IsWarmingUp_OutputChange;

//            SourceSelectSRL.DetachFromCurrentRoom();
//        }

//        /// <summary>
//        /// Ties this panel controller to the Room and gets updates.
//        /// </summary>
//        protected override void ShowRoomUI()
//        {
//            Debug.Console(1, this, "connecting to system '{0}'", CurrentRoom.Key);

//            TriList.StringInput[RoomCue.Name.Number].StringValue = CurrentRoom.Name;
//            TriList.StringInput[RoomCue.Description.Number].StringValue = CurrentRoom.Description;

//            CurrentRoom.IsCoolingDownFeedback.OutputChange -= CurrentRoom_IsCoolingDown_OutputChange;
//            CurrentRoom.IsWarmingUpFeedback.OutputChange -= CurrentRoom_IsWarmingUp_OutputChange;
//            CurrentRoom.IsCoolingDownFeedback.OutputChange += CurrentRoom_IsCoolingDown_OutputChange;
//            CurrentRoom.IsWarmingUpFeedback.OutputChange += CurrentRoom_IsWarmingUp_OutputChange;

//            SourceSelectSRL.AttachToRoom(CurrentRoom);
//        }

//        void CurrentRoom_IsCoolingDown_OutputChange(object sender, EventArgs e)
//        {
//            Debug.Console(2, this, "Received room in cooldown={0}", CurrentRoom.IsCoolingDownFeedback.BoolValue);
//            if (CurrentRoom.IsCoolingDownFeedback.BoolValue) // When entering cooldown
//            {
//                // Do we need to check for an already-running cooldown - like in the case of room switches?
//                new ModalDialog(TriList).PresentModalTimerDialog(0, "Power Off", "Power", "Please wait, shutting down",
//                    "", "", CurrentRoom.CooldownTime, true, b =>
//                    {
//                        ShowSplashOrMain();
//                    });
//            }
//        }

//        void CurrentRoom_IsWarmingUp_OutputChange(object sender, EventArgs e)
//        {
//            Debug.Console(2, this, "Received room in warmup={0}", CurrentRoom.IsWarmingUpFeedback.BoolValue);
//            if (CurrentRoom.IsWarmingUpFeedback.BoolValue) // When entering warmup
//            {
//                // Do we need to check for an already-running cooldown - like in the case of room switches?
//                new ModalDialog(TriList).PresentModalTimerDialog(0, "Power On", "Power", "Please wait, powering on",
//                    "", "", CurrentRoom.WarmupTime, false, b =>
//                    {
//                        // Reveal sources - or has already been done behind modal
//                    });
//            }
//        }

//        // Handler for source change events.
//        void CurrentRoom_PresentationSourceChange(object sender, EssentialsRoomSourceChangeEventArgs args)
//        {
//            // Put away the old source and set up the new source.
//            Debug.Console(2, this, "Received source change={0}", args.NewSource != null ? args.NewSource.SourceKey : "none");

//            // If we're in tech, don't switch screen modes.  Add any other modes we may want to switch away from
//            // inside the if below.
//            if (MainMode == eMainModeType.Splash)
//                SetMainMode(eMainModeType.Presentation);
//            SetControlSource(args.NewSource);
//        }

//        //***********************************************************************
//        //** UI Manipulation
//        //***********************************************************************

//        /// <summary>
//        /// Shows the splash page or the main presentation page, depending on config setting
//        /// </summary>
//        void ShowSplashOrMain()
//        {
//            if (UsesSplashPage)
//                SetMainMode(eMainModeType.Splash);
//            else
//                SetMainMode(eMainModeType.Presentation);
//        }

//        /// <summary>
//        /// Switches between main modes
//        /// </summary>
//        void SetMainMode(eMainModeType mode)
//        {
//            MainMode = mode;
//            switch (mode)
//            {
//                case eMainModeType.Presentation:
//                    TriList.BooleanInput[UiCue.VisibleCommonFooter.Number].BoolValue = true;
//                    TriList.BooleanInput[UiCue.VisibleCommonHeader.Number].BoolValue = true;
//                    TriList.BooleanInput[UiCue.VisibleSplash.Number].BoolValue = false;
//                    TriList.BooleanInput[UiCue.VisiblePresentationSourceList.Number].BoolValue = true;
//                    ShowCurrentPresentationSourceUi();
//                    break;
//                case eMainModeType.Splash:
//                    TriList.BooleanInput[UiCue.VisibleCommonFooter.Number].BoolValue = false;
//                    TriList.BooleanInput[UiCue.VisibleCommonHeader.Number].BoolValue = false;
//                    TriList.BooleanInput[UiCue.VisiblePresentationSourceList.Number].BoolValue = false;
//                    TriList.BooleanInput[UiCue.VisibleSplash.Number].BoolValue = true;
//                    HideCurrentPresentationSourceUi();
//                    break;
//                case eMainModeType.Tech:
//                    new ModalDialog(TriList).PresentModalTimerDialog(1, "Tech page", "Info",
//                        "Tech page will be here soon!<br>I promise",
//                        "Bueno!", "", 0, false, null);
//                    MainMode = eMainModeType.Presentation;
//                    break;
//                default:
//                    break;
//            }
//        }

//        /// <summary>
//        /// 
//        /// </summary>
//        void PowerOffWithConfirmPressed()
//        {
//            if (CurrentRoom == null)
//                return;
//            if (!CurrentRoom.RoomIsOnFeedback.BoolValue) 
//                return;
//            // Timeout or button 1 press will shut down
//            var modal = new ModalDialog(TriList);
//            uint seconds = CurrentRoom.UnattendedShutdownTimeMs / 1000;
//            var message = string.Format("Meeting will end in {0} seconds", seconds);
//            modal.PresentModalTimerDialog(2, "End Meeting", "Info", message,
//                "End Meeting Now", "Cancel", CurrentRoom.UnattendedShutdownTimeMs, true,
//                but => { if (but != 2) CurrentRoom.RoomOff(); });
//        }

//        /// <summary>
//        /// Reveals the basic UI for the current device
//        /// </summary>
//        protected override void ShowCurrentPresentationSourceUi()
//        {
//            if (MainMode == eMainModeType.Splash && CurrentRoom.RoomIsOnFeedback.BoolValue)
//                SetMainMode(eMainModeType.Presentation);

//            if (CurrentPresentationControlDevice == null)
//            {
//                // If system is off, do one thing

//                // Otherwise, do something else - shouldn't be in this condition

//                return;
//            }

//            // If a controller is already loaded, use it
//            if (LoadedPageControllers.ContainsKey(CurrentPresentationControlDevice))
//                CurrentPresentationSourcePageController = LoadedPageControllers[CurrentPresentationControlDevice];
//            else
//            {
//                // This is by no means optimal, but for now....
//                if (CurrentPresentationControlDevice.Type == PresentationSourceType.SetTopBox
//                    && CurrentPresentationControlDevice is ISetTopBoxControls)
//                    CurrentPresentationSourcePageController = new PageControllerLargeSetTopBoxGeneric(TriList,
//                        CurrentPresentationControlDevice as ISetTopBoxControls);

//                else if (CurrentPresentationControlDevice.Type == PresentationSourceType.Laptop)
//                    CurrentPresentationSourcePageController = new PageControllerLaptop(TriList);

//                // separate these...
//                else if (CurrentPresentationControlDevice.Type == PresentationSourceType.Dvd)
//                    CurrentPresentationSourcePageController =
//                        new PageControllerLargeDvd(TriList, CurrentPresentationControlDevice as IDiscPlayerControls);

//                else
//                    CurrentPresentationSourcePageController = null;

//                // Save it.
//                if (CurrentPresentationSourcePageController != null)
//                    LoadedPageControllers[CurrentPresentationControlDevice] = CurrentPresentationSourcePageController;
//            }

//            if (CurrentPresentationSourcePageController != null)
//                CurrentPresentationSourcePageController.SetVisible(true);
//        }

//        protected override void HideCurrentPresentationSourceUi()
//        {
//            if (CurrentPresentationControlDevice != null && CurrentPresentationSourcePageController != null)
//                CurrentPresentationSourcePageController.SetVisible(false);
//        }



//        void ShowHelp()
//        {
//            new ModalDialog(TriList).PresentModalTimerDialog(1, "Help", "Help", CurrentRoom.HelpMessage,
//                    "OK", "", 0, false, null);
//        }

//        protected void ListSmartObjects()
//        {
//            Debug.Console(0, this, "Smart objects IDs:");
//            var list = TriList.SmartObjects.OrderBy(s => s.Key);
//            foreach (var kvp in list)
//                Debug.Console(0, "  {0}", kvp.Key);
//        }
//    }
//}