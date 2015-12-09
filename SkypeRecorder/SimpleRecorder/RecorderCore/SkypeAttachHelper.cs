using SKYPE4COMLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Stateless;
using System.IO;
using System.ComponentModel;

namespace RecorderCore
{
    public class SkypeAttachHelper
    {
        enum State
        {
            NotAvailable,
            Available,
            RequestSent,
            PendingAuthorization,
            Success,
            Refused,
        }

        enum Trigger
        {
            NotAvailable,
            Available,
            RequestSent,
            PendingAuthorization,
            Success,
            Refused,
        }

        private StateMachine<State, Trigger> attachment;

        private State currentState;
        private State CurrentState
        {
            get { return this.currentState; }
            set
            {
                this.currentState = value;
                AddToLog("Attachment State: " + value.ToString());
            }
        }

        private Skype skype;
        private CancelEventHandler attachResultHandler;
        private Action<string> addToLog;

        public SkypeAttachHelper(Skype4ComHelper skypeHelper, CancelEventHandler attachResultHandler, Action<string> addToLog)
        {
            this.attachResultHandler = attachResultHandler;
            this.addToLog = addToLog;

            CurrentState = State.NotAvailable;
            this.attachment = new StateMachine<State, Trigger>(() => CurrentState, (s) => CurrentState = s);
            ConfigureAttachmentStateMachine(this.attachment);

            // Always use try/catch with ANY Skype calls.
            try
            {
                this.skype = skypeHelper.Skype;
                var skypeEvents = (_ISkypeEvents_Event)this.skype;

                skypeEvents.AttachmentStatus += OurAttachmentStatus;

                SendAttachRequest();
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

        private void ConfigureAttachmentStateMachine(StateMachine<State, Trigger> sm)
        {
            sm.Configure(State.NotAvailable)
                .Ignore(Trigger.NotAvailable)
                .Permit(Trigger.RequestSent, State.RequestSent)
                .Permit(Trigger.Available, State.Available)
                ;
            sm.Configure(State.Available)
                .OnEntry(() => SendAttachRequest())
                .Permit(Trigger.NotAvailable, State.NotAvailable)
                .Permit(Trigger.RequestSent, State.RequestSent)
                ;
            sm.Configure(State.RequestSent)
                .Permit(Trigger.NotAvailable, State.NotAvailable)
                .Permit(Trigger.Success, State.Success)
                .Permit(Trigger.PendingAuthorization, State.PendingAuthorization)
                .Permit(Trigger.Refused, State.Refused)
                .Permit(Trigger.Available, State.Available)
                ;
            sm.Configure(State.PendingAuthorization)
                .Permit(Trigger.Refused, State.Refused)
                .Permit(Trigger.Success, State.Success)
                ;
            sm.Configure(State.Refused)
                .Ignore(Trigger.PendingAuthorization) // Messages may come out of order
                .Permit(Trigger.Available, State.Available)
                ;
            sm.Configure(State.Success)
                .Ignore(Trigger.PendingAuthorization) // Messages may come out of order
                .Permit(Trigger.NotAvailable, State.NotAvailable)
                ;
        }

        public void OurAttachmentStatus(TAttachmentStatus status)
        {
            this.addToLog(status.ToString());

            switch (status)
            {
                case TAttachmentStatus.apiAttachAvailable:
                    // SKYPECONTROLAPI_ATTACH_API_AVAILABLE is a broadcast message.
                    if (attachment.CanFire(Trigger.Available))
                    {
                        attachment.Fire(Trigger.Available);
                    }
                    break;
                case TAttachmentStatus.apiAttachNotAvailable:
                    attachment.Fire(Trigger.NotAvailable);
                    break;
                case TAttachmentStatus.apiAttachPendingAuthorization:
                    attachment.Fire(Trigger.PendingAuthorization);
                    break;
                case TAttachmentStatus.apiAttachRefused:
                    attachment.Fire(Trigger.Refused);
                    OnAttachResult(false);
                    break;
                case TAttachmentStatus.apiAttachSuccess:
                    attachment.Fire(Trigger.Success);
                    OnAttachResult(true);
                    break;
                case TAttachmentStatus.apiAttachUnknown:
                    attachment.Fire(Trigger.NotAvailable);
                    break;
                default:
                    break;
            }
        }

        private void SendAttachRequest()
        {
            try
            {
                this.skype.Attach(9, false); // The current is 8, force the latest protocol to be used.
            }
            catch (Exception e)
            {
                AddToLog(e.Message);
            }

            this.attachment.Fire(Trigger.RequestSent);
        }

        private void OnAttachResult(bool success)
        {
            if (this.attachResultHandler != null)
            {
                var cancel = !success;
                this.attachResultHandler(this, new CancelEventArgs(cancel));
            }
        }
    }
}
