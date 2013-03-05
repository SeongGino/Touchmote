﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WiimoteLib;
using WindowsInput;

namespace WiiTUIO.Provider
{
    class WiimoteControl
    {
        public FrameEventArgs LastFrameEvent;
        public Queue<FrameEventArgs> FrameQueue = new Queue<FrameEventArgs>(1);
        public DateTime LastWiimoteEventTime = DateTime.Now;

        public Wiimote Wiimote;

        public int ID;

        /// <summary>
        /// Used to obtain mutual exlusion over Wiimote updates.
        /// </summary>
        private Mutex pDeviceMutex = new Mutex();

        private InputSimulator inputSimulator;

        private ScreenPositionCalculator screenPositionCalculator;

        private DuoTouch duoTouch;

        private WiiKeyMapper keyMapper;

        private bool touchDownMaster = false;

        private bool touchDownSlave = false;

        private bool showPointer = true;

        private bool mouseMode = false;

        private WiimoteLib.Point lastpoint;

        /// <summary>
        /// The screen size that we use for normalising coordinates.
        /// </summary>
        public Vector ScreenSize { get; protected set; }

        public WiimoteControl(int id, Wiimote wiimote)
        {
            this.Wiimote = wiimote;
            this.ID = id;

            lastpoint = new WiimoteLib.Point();
            lastpoint.X = 0;
            lastpoint.Y = 0;

            this.ScreenSize = new Vector(Util.ScreenWidth, Util.ScreenHeight);

            ulong touchStartID = (ulong)(id - 1) * 4 + 1; //This'll make sure the touch point IDs won't be the same. DuoTouch uses a span of 4 IDs.
            this.duoTouch = new DuoTouch(this.ScreenSize, 3, touchStartID);
            this.keyMapper = new WiiKeyMapper(id);

            this.keyMapper.KeyMap.OnButtonDown += WiiButton_Down;
            this.keyMapper.KeyMap.OnButtonUp += WiiButton_Up;
            this.keyMapper.KeyMap.OnConfigChanged += WiiKeyMap_ConfigChanged;

            this.inputSimulator = new InputSimulator();
            this.screenPositionCalculator = new ScreenPositionCalculator();
        }

        private void WiiKeyMap_ConfigChanged(WiiKeyMapConfigChangedEvent evt)
        {
            if (evt.NewPointer.ToLower() == "touch")
            {
                this.mouseMode = false;
                if (this.showPointer)
                {
                    this.duoTouch.enableHover();
                }
            }
            else if (evt.NewPointer.ToLower() == "mouse")
            {
                this.mouseMode = true;
                this.duoTouch.disableHover();
                MouseSimulator.WakeCursor();
            }
        }

        private void WiiButton_Up(WiiButtonEvent evt)
        {
            if (evt.Action.ToLower() == "pointertoggle" && !evt.Handled)
            {
                this.showPointer = this.showPointer ? false : true;
                if (this.showPointer)
                {
                    this.duoTouch.enableHover();
                }
                else
                {
                    this.duoTouch.disableHover();
                }
            }
            if (evt.Action.ToLower() == "touchmaster" && !evt.Handled)
            {
                touchDownMaster = false;
            }
            if (evt.Action.ToLower() == "touchslave" && !evt.Handled)
            {
                touchDownSlave = false;
            }
        }

        private void WiiButton_Down(WiiButtonEvent evt)
        {
            if (evt.Action.ToLower() == "touchmaster" && !evt.Handled)
            {
                touchDownMaster = true;
            }
            if (evt.Action.ToLower() == "touchslave" && !evt.Handled)
            {
                touchDownSlave = true;
            }
        }

        public void handleWiimoteChanged(object sender, WiimoteChangedEventArgs e)
        {
            // Obtain mutual excluseion.
            pDeviceMutex.WaitOne();

            LastWiimoteEventTime = DateTime.Now;

            Queue<WiiContact> lFrame = new Queue<WiiContact>(1);
            // Store the state.
            WiimoteState pState = e.WiimoteState;

            bool pointerOutOfReach = false;

            WiimoteLib.Point newpoint = lastpoint;

            newpoint = screenPositionCalculator.GetPosition(e);

            if (newpoint.X < 0 || newpoint.Y < 0)
            {
                newpoint = lastpoint;
                pointerOutOfReach = true;
            }

            //Temporary solution to the "diamond cursor" problem.
            /*
            if (this.changeSystemCursor)
            {
                try
                {
                    MouseSimulator.RefreshMainCursor();
                }
                catch (Exception error)
                {
                    Console.WriteLine(error.ToString());
                }
            }
            */
            WiimoteState ws = e.WiimoteState;

            keyMapper.processButtonState(ws.ButtonState);

            if (!pointerOutOfReach)
            {

                if (this.touchDownMaster)
                {
                    duoTouch.setContactMaster();
                }
                else
                {
                    duoTouch.releaseContactMaster();
                }

                duoTouch.setMasterPosition(new System.Windows.Point(newpoint.X, newpoint.Y));

                if (this.touchDownSlave)
                {
                    duoTouch.setSlavePosition(new System.Windows.Point(newpoint.X, newpoint.Y));
                    duoTouch.setContactSlave();
                }
                else
                {
                    duoTouch.releaseContactSlave();
                }

                lastpoint = newpoint;

                lFrame = duoTouch.getFrame();

                FrameEventArgs pFrame = new FrameEventArgs((ulong)Stopwatch.GetTimestamp(), lFrame);

                this.FrameQueue.Enqueue(pFrame);
                this.LastFrameEvent = pFrame;

                if (mouseMode && !this.touchDownMaster && !this.touchDownSlave && this.showPointer) //Mouse mode
                {
                    this.inputSimulator.Mouse.MoveMouseToPositionOnVirtualDesktop((65535 * newpoint.X) / this.ScreenSize.X, (65535 * newpoint.Y) / this.ScreenSize.Y);
                    MouseSimulator.WakeCursor();
                    //MouseSimulator.SetCursorPosition(newpoint.X, newpoint.Y);
                }
            }
            //this.BatteryState = (pState.Battery > 0xc8 ? 0xc8 : (int)pState.Battery);

            // Release mutual exclusion.
            pDeviceMutex.ReleaseMutex();
        }
    }
}
