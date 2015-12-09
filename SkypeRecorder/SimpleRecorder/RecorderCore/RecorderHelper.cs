using NAudio.Wave;
using SKYPE4COMLib;
using Stateless;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RecorderCore
{
    public class RecorderHelper : System.ComponentModel.INotifyPropertyChanged
    {
        enum State
        {
            //Disabled,
            CallStarted,
            Recording,
            Paused,
            CallFinished,
        }

        enum Trigger
        {
            CallStarted,
            CallFinished,
            ButtonPressed,
            UserInformed,
        }

        private StateMachine<State, Trigger> recorder;

        private State currentState;
        private State CurrentState
        {
            get { return this.currentState; }
            set
            {
                this.currentState = value;
                AddToLog(value.ToString());
            }
        }

        private SkypeCallHelper callHelper;

        private Call callInProgress;

        private Call CallInProgress
        {
            set
            {
                if (this.callInProgress != value)
                {
                    if (this.callInProgress != null)
                    {
                        this.recorder.Fire(Trigger.CallFinished);
                    }

                    this.callInProgress = value;

                    if (value != null)
                    {
                        this.recorder.Fire(Trigger.CallStarted);
                    }

                    AddToLog("CallInProgress: " + (this.callInProgress != null ? this.callInProgress.Id.ToString() : "null"));

                    NotifyPropertyChanged("GetButtonEnabled");
                    NotifyPropertyChanged("GetButtonText");
                    NotifyPropertyChanged("GetButtonForegroundColor");
                }
            }
        }

        private Skype skype;
        private EventHandler recordingDoneHandler;
        private Action<string> addToLog;
        private bool isAttached;

        public string GetButtonText
        {
            get
            {
                switch (this.CurrentState)
                {
                    case State.CallStarted:
                    case State.CallFinished:
                        return "Start recording";
                    case State.Recording:
                        return "Stop recording"; // "Pause recording";
                    case State.Paused:
                        return "Start recording"; // "Resume recording";
                    default:
                        return "";
                }
            }
        }

        public bool GetButtonEnabled
        {
            get
            {
                return isAttached && this.CurrentState != State.CallFinished;
            }
        }

        public string GetButtonForegroundColor
        {
            get
            {
                return this.CurrentState == State.Recording ? "Red" : (this.CurrentState == State.CallFinished ? "LightGray" : "Black");
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged(String info)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(info));
            }
        }

        public RecorderHelper(Skype4ComHelper skypeHelper, EventHandler recordingDoneHandler, Action<string> addToLog)
        {
            this.recordingDoneHandler = recordingDoneHandler;
            this.addToLog = addToLog;

            CurrentState = State.CallFinished;
            this.recorder = new StateMachine<State, Trigger>(() => CurrentState, (s) => CurrentState = s);
            ConfigureStateMachine(this.recorder);

            // Always use try/catch with ANY Skype calls.
            try
            {
                this.skype = skypeHelper.Skype;
                var skypeEvents = (_ISkypeEvents_Event)this.skype;
                skypeEvents.CallStatus += OurCallStatus;
                //skypeEvents.AttachmentStatus += OurAttachmentStatus; // We use AttachSuccessHandler called by SkypeAttachHelper instead, so the event goes via the main form and involves some logic.
            }
            catch (Exception e)
            {
                AddToLog(e.Message);
            }
        }

        private void AddToLog(string text)
        {
            if (this.addToLog != null)
            {
                this.addToLog(text);
            }
        }

        private void ConfigureStateMachine(StateMachine<State, Trigger> sm)
        {
            sm.Configure(State.CallFinished)
                .OnEntry(() => CallDone())
                .Ignore(Trigger.CallFinished)
                .Permit(Trigger.CallStarted, State.CallStarted)
                ;
            sm.Configure(State.CallStarted)
                .OnEntry(() => CreateCallHelper())
                .Permit(Trigger.CallFinished, State.CallFinished)
                .Permit(Trigger.ButtonPressed, State.Recording)
                ;
            sm.Configure(State.Recording)
                .OnEntry(() => StartRecording())
                .OnExit(() => StopRecording())
                .Permit(Trigger.CallFinished, State.CallFinished)
                .Permit(Trigger.ButtonPressed, State.Paused)
                ;
            sm.Configure(State.Paused)
                .Permit(Trigger.CallFinished, State.CallFinished)
                .Permit(Trigger.ButtonPressed, State.Recording)
                ;
        }

        //private void OurAttachmentStatus(TAttachmentStatus Status)
        //{
        //    AddToLog("RecorderHelper.OurAttachmentStatus");
        //    isAttached = Status == TAttachmentStatus.apiAttachSuccess;
        //    if (isAttached)
        //    {
        //        if (skype.ActiveCalls.Count == 1)
        //        {
        //            var call = skype.ActiveCalls[1];
        //            OurCallStatus(call, call.Status);
        //        }
        //    }
        //}

        public bool IsCallInProgress()
        {
            var activeCalls = this.skype.ActiveCalls;
            return (activeCalls != null) && (activeCalls.Count == 1);
        }

        public void HandleCallInProgress()
        {
            /* It seems that Skype does not send a notification on call start. Skype does send notification on call end.
             * Thus only the user knows when a call has started.              
             * When attachment is done not during a call, we do not know when to enable the recording button.
             */
            if (IsCallInProgress())
            {
                isAttached = true;
                var call = this.skype.ActiveCalls[1];
                OurCallStatus(call, call.Status);
            }
        }

        public void OurCallStatus(Call call, TCallStatus status)
        {
            AddToLog(string.Format("Call: {0}, Status: {1}", call.Id.ToString(), status.ToString()));

            switch (status)
            {
                case TCallStatus.clsInProgress:
                case TCallStatus.clsLocalHold:
                case TCallStatus.clsOnHold:
                case TCallStatus.clsRemoteHold:
                    this.CallInProgress = call;
                    break;
                default:
                    this.CallInProgress = null;
                    break;
            }
        }

        public void OnButtonPressed()
        {
            if (GetButtonEnabled)
            {
                this.recorder.Fire(Trigger.ButtonPressed);
                NotifyPropertyChanged("GetButtonText");
                NotifyPropertyChanged("GetButtonForegroundColor");
            }
        }

        private void CreateCallHelper()
        {
            this.callHelper = new SkypeCallHelper(this.callInProgress, this.recordingDoneHandler, this.addToLog);
        }

        private void CallDone()
        {
            if (this.callHelper != null)
            {
                this.callHelper.Done();
            }
        }

        private void StartRecording()
        {
            this.callHelper.StartRecording();
        }

        private void StopRecording()
        {
            this.callHelper.StopRecording();
        }

        // The main window monitors the clipboard. The user uses the PrintScreen key as a remark spot marker.
        public void RemarkSpotEventHandler(object sender, EventArgs e)
        {
            this.callHelper.WriteRemarkSpot();
        }

        //private void AddInfo(WaveFormat format)
        //{
        //    // WAV files created by Skype have format 16bit, 16kHz, 1 channel(Mono), PCM, 256kbit.
        //    AddToLog(string.Format("{0} {1} {2} {3} {4}", format.BitsPerSample, format.SampleRate, format.Channels, format.Encoding, format.AverageBytesPerSecond));
        //}

    }
}
