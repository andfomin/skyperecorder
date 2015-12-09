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
using System.Threading;

namespace RecorderCore
{
    public class SkypeCallHelper
    {
        public const string RootFolderNameSettingKey = "RootFolderName";
        public const string DefaultRootFolderName = "Englisharium Recorder for Skype";

        public const string RecordingEventLogFileExtension = "xml";

        private Call call;
        private EventHandler recordingDoneHandler;
        private Action<string> addToLog;
        private Tuple<string, string> currentRecording;
        private BlockingCollection<Tuple<string, string>> recordings;
        private int recordingCount = 0;

        public List<string> Mp3Files { get; set; }

        public SkypeCallHelper(Call call, EventHandler recordingDoneHandler, Action<string> addToLog)
        {
            this.call = call;
            this.recordingDoneHandler = recordingDoneHandler;
            this.addToLog = addToLog;

            this.recordings = new BlockingCollection<Tuple<string, string>>();
            Mp3Files = new List<string>();

            var backgroundWorker = new BackgroundWorker();
            backgroundWorker.DoWork += BackgroundWorkerDoWork;
            backgroundWorker.RunWorkerCompleted += BackgroundWorkerRunWorkerCompleted;
            backgroundWorker.RunWorkerAsync();
        }

        public void Done()
        {
            recordings.CompleteAdding();
        }

        private void AddToLog(string text)
        {
            if (this.addToLog != null)
            {
                this.addToLog(text);
            }
        }

        public void StartRecording()
        {
            var folder = Path.Combine(Path.GetTempPath());
            ++this.recordingCount;
            var fileName = this.call.Timestamp.ToString("yyyy-MM-dd HH_mm_ss") + (this.recordingCount == 1 ? "" : string.Format(" ({0})", this.recordingCount));
            var outputPath = Path.Combine(folder, fileName + ".out");
            var captureMicPath = Path.Combine(folder, fileName + ".mic");

            try
            {
                this.call.set_OutputDevice(TCallIoDeviceType.callIoDeviceTypeFile, outputPath);
                this.call.set_CaptureMicDevice(TCallIoDeviceType.callIoDeviceTypeFile, captureMicPath);
            }
            catch (Exception ex)
            {
                AddToLog(ex.Message);
            }

            this.currentRecording = new Tuple<string, string>(outputPath, captureMicPath);

            WriteRecordingEvent(RecordingEvent.RecordingStarted);

            AddToLog(outputPath);
            AddToLog(captureMicPath);
        }

        public void StopRecording()
        {
            WriteRecordingEvent(RecordingEvent.RecordingStoped);
            var temp = this.currentRecording;
            this.currentRecording = null;

            try
            {
                if (this.call.Status == TCallStatus.clsInProgress)
                {
                    this.call.set_OutputDevice(TCallIoDeviceType.callIoDeviceTypeFile, "");
                    this.call.set_CaptureMicDevice(TCallIoDeviceType.callIoDeviceTypeFile, "");
                }
            }
            catch (Exception ex)
            {
                AddToLog(ex.Message);
            }

            this.recordings.Add(temp);
        }

        private void BackgroundWorkerDoWork(object sender, DoWorkEventArgs e)
        {
            // This sequence that we're enumerating will block when no elements are available and will end when CompleteAdding is called.
            foreach (var recording in this.recordings.GetConsumingEnumerable())
            {
                // lame_enc.dll is not thread-safe.
                ProcessRecording(recording);
            }
        }

        private void BackgroundWorkerRunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                AddToLog("BackgroundWorker. Cancelled");
            }
            else if (e.Error != null)
            {
                AddToLog("BackgroundWorker. Exception Thrown. " + e.Error.Message);
            }
            else
            {
                AddToLog("BackgroundWorker. Completed");

                //if (this.Mp3Files.Count > 0 && this.callDoneHandler != null)
                //{
                //    this.callDoneHandler(this, EventArgs.Empty);
                //}
            }
        }

        private void ProcessRecording(Tuple<string, string> recording)
        {
            var outputPath = recording.Item1;
            var captureMicPath = recording.Item2;

            var fileName = Path.GetFileNameWithoutExtension(outputPath);

            var documentsFolder = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);

            var rootFolderName = System.Configuration.ConfigurationManager.AppSettings[RootFolderNameSettingKey] ?? DefaultRootFolderName;

            var dir = Path.Combine(documentsFolder, rootFolderName, fileName);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var mp3Path = Path.Combine(dir, fileName.Substring(11) + ".mp3");

            using (var outReader = new WaveFileReader(outputPath))
            {
                // WaveChannel32 outputs stereo.
                var outChannel = new WaveChannel32(outReader);

                using (var micReader = new WaveFileReader(captureMicPath))
                {
                    var micChannel = new WaveChannel32(micReader);

                    using (var mixerStream = new WaveMixerStream32(new[] { outChannel, micChannel }, true))
                    {
                        var stream16 = new WaveFloatTo16Provider(mixerStream);

                        var monoStream = new StereoToMonoProvider16(stream16);
                        monoStream.LeftVolume = 1;
                        monoStream.RightVolume = 1;

                        var monoFormat = monoStream.WaveFormat;
                        var inputFormat = new Yeti.Lame.WaveFormat2(monoFormat.SampleRate, monoFormat.BitsPerSample, monoFormat.Channels);
                        var config = new Yeti.Lame.BE_CONFIG(inputFormat, 64);

                        // The file will be closed by the wrapping writer.
                        var fileStream = new FileStream(mp3Path, FileMode.Create);

                        using (var writer = new Yeti.MMedia.Mp3.Mp3Writer(fileStream, inputFormat, config))
                        {
                            var bufferLength = writer.GetBufferSize();
                            var buffer = new byte[bufferLength];

                            while (true)
                            {
                                var bytesRead = monoStream.Read(buffer, 0, bufferLength);
                                if (bytesRead == 0) break;
                                writer.Write(buffer, 0, bytesRead);
                            }

                            // Close explicitly. Disposing by the finalizer does not write the header of the last MP3 frame properly.
                            writer.Close();
                        }
                    }
                }
            }

            Mp3Files.Add(mp3Path);

            File.Delete(captureMicPath);
            File.Delete(outputPath);

            // Move the event log file from the Temp folder and put it alongside the MP3 file.
            var sourceRemarks = Path.ChangeExtension(outputPath, RecordingEventLogFileExtension);
            var destRemarks = Path.ChangeExtension(mp3Path, RecordingEventLogFileExtension);
            File.Move(sourceRemarks, destRemarks);

            // Notify the outer layer of another recording is ready.
            if (this.recordingDoneHandler != null)
            {
                this.recordingDoneHandler(this, EventArgs.Empty);
            }
        }

        private void WriteRecordingEvent(RecordingEvent recordingEvent)
        {
            string text = null;

            switch (recordingEvent)
            {
                case RecordingEvent.RecordingStarted:
                    text = string.Format(@"<Recording time=""{0}"">", DateTime.UtcNow.ToString("O"));
                    break;
                case RecordingEvent.RecordingStoped:
                    text = "</Recording>";
                    break;
                case RecordingEvent.RemarkSpot:
                    text = string.Format(@"<RemarkSpot time=""{0}"" />", DateTime.UtcNow.ToString("O"));
                    break;
                default:
                    break;
            }
            WriteToRecordingEventLog(text);
        }

        private void WriteToRecordingEventLog(string text)
        {
            if (currentRecording != null)
            {
                var file = Path.ChangeExtension(currentRecording.Item1, RecordingEventLogFileExtension);
                using (StreamWriter writer = File.AppendText(file))
                {
                    writer.WriteLine(text);
                }
            }
        }

        public void WriteRemarkSpot()
        {
            WriteRecordingEvent(RecordingEvent.RemarkSpot);
        }

        public enum RecordingEvent
        {
            RecordingStarted,
            RecordingStoped,
            RemarkSpot,
        }

    }
}
