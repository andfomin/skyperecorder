using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
//using System.IO;
using RecorderCore;
using System.Diagnostics;
using System.Xml.Linq;
using System.ComponentModel;
using System.IO;

namespace Recorder
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

#if DEBUG
        const string WebsiteHost = "deva.englisharium.com";
#else
             const string WebsiteHost = "englisharium.com";
#endif
        // var url = String.Format(urlTemplate, WebsiteHost, urlParameter);  
        const string HelpUrlTemplate = "http://{0}/recorder-for-skype/help/?topic={1}";
        const string UploadUrlTemplate = "http://{0}/recorder-for-skype/upload/?dir={1}";

        SkypeAttachHelper skypeAttachHelper;
        RecorderHelper recorderHelper;
        ClipboardHelper clipboardHelper; // We use the PrintScreen key as a spot marker.

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(GetRootFolderPath()); // If the directory exists, does nothing.
            var skypeHelper = new Skype4ComHelper();
            this.skypeAttachHelper = new SkypeAttachHelper(skypeHelper, AttachResultHandler, AddToLog);
            this.recorderHelper = new RecorderHelper(skypeHelper, RecordingDoneHandler, AddToLog);
            this.clipboardHelper = new ClipboardHelper(this);
            this.clipboardHelper.onClipboardUpdateHandler += this.recorderHelper.RemarkSpotEventHandler;
            this.RecordButton.DataContext = recorderHelper;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            this.clipboardHelper.RemoveCBViewer();
        }

        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            this.recorderHelper.OnButtonPressed();
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            DirectUser(null, "Help", HelpUrlTemplate, "0", false);
        }

        private void AddToLog(string logMessage)
        {
#if DEBUG
            var logFilePath = System.IO.Path.Combine(GetRootFolderPath(), "log.txt");
            using (var writer = System.IO.File.AppendText(logFilePath))
            {
                writer.Write("Log Entry : ");
                writer.WriteLine("{0} {1}", DateTime.Now.ToLongDateString(), DateTime.Now.ToLongTimeString());
                writer.WriteLine("  :{0}", logMessage);
                writer.WriteLine("-------------------------------");
            }
#endif
        }

        private void AttachResultHandler(object sender, CancelEventArgs e)
        {
            AddToLog(String.Format("AttachResultHandler. Cancel: {0}", e.Cancel));
            if (e.Cancel)
            {
                // Apparently the user has not allowed our app in the Skype GUI. 
                DirectUser("The recorder was unable to attach to Skype. You will be redirected to the Help webpage.",
                    "Help", HelpUrlTemplate, "1", true);
            }

            /* It seems that Skype does not send a notification on call start. Skype does send notification on call end.
             * Thus only the user knows when a call has started.              
             * When attachment is done not during a call, we do not know when to enable the recording button.
             */
            var isCallInProgress = this.recorderHelper.IsCallInProgress();
            AddToLog(String.Format("Call in progress: {0}", isCallInProgress));
            if (isCallInProgress)
            {
                this.clipboardHelper.InitCBViewer();
                this.recorderHelper.HandleCallInProgress();
            }
            else
            {
                DirectUser("The recorder can be started only when a call is in progress. You will be redirected to the Help webpage.",
                    "Help", HelpUrlTemplate, "2", true);
            }
        }

        private void RecordingDoneHandler(object sender, EventArgs e)
        {
            AddToLog("RecordingDoneHandler");
            var callHelper = sender as SkypeCallHelper;
            if (callHelper != null)
            {
                var mp3FilePath = callHelper.Mp3Files.LastOrDefault();
                if (!string.IsNullOrWhiteSpace(mp3FilePath))
                {
                    var dir = System.IO.Path.GetDirectoryName(mp3FilePath);
                    byte[] dirBytes = System.Text.Encoding.Unicode.GetBytes(dir);
                    string encodedDir = System.Convert.ToBase64String(dirBytes);

                    // This event handler is executed by the background worker thread. It throws if calls Shutdown(). We use Dispatcher to shutdown from the main thread.
                    Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                        DirectUser("You will be redirected to the webpage to upload your recording. To make another recording you will need to start the Recorder again.",
                            "Upload", UploadUrlTemplate, encodedDir, true);
                    }));
                }
            }
        }

        private void DirectUser(string message, string shortcutName, string urlTemplate, string urlParameter, bool shutdown)
        {
            if (!String.IsNullOrEmpty(message))
            {
                MessageBox.Show(message, "Recorder", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            if (!String.IsNullOrEmpty(shortcutName))
            {
                var url = String.Format(urlTemplate, WebsiteHost, urlParameter);
                var shortcutFilePath = CreateUrlShortcut(GetRootFolderPath(), shortcutName, url);
                ExecuteShortcut(shortcutFilePath);
            }
            if (shutdown)
            {
                AddToLog("Shutdown");
                App.Current.Shutdown();
            }
        }

        private string GetRootFolderPath()
        {
            var documentsFolder = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
            var rootFolderName = System.Configuration.ConfigurationManager.AppSettings[SkypeCallHelper.RootFolderNameSettingKey] ?? SkypeCallHelper.DefaultRootFolderName;
            return System.IO.Path.Combine(documentsFolder, rootFolderName);
        }

        private static string CreateUrlShortcut(string dir, string shortcutName, string url)
        {
            var filePath = System.IO.Path.Combine(dir, shortcutName + ".url");

            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }

            using (var writer = new System.IO.StreamWriter(filePath))
            {
                writer.WriteLine("[InternetShortcut]");
                writer.WriteLine("URL=" + url);
                writer.Flush();
            }
            return filePath;
        }

        // I have experienced an unpleasant case when Chrome did not respond to Process.Start().
        // Besides MSDN says at +http://msdn.microsoft.com/en-us/library/vstudio/53ezey2s(v=vs.100).aspx that 
        // If the address of the executable file to start is a URL, the process is not started and null is returned.
        // The documentation changed between the Framework versions 3.5 and 4.0.
        // That is why we use a URL shotcut as an intermediary.
        public static void ExecuteShortcut(string shortcutFilePath)
        {
            //string sysPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
            //string ExplorerPath = System.IO.Path.Combine(Directory.GetParent(sysPath).FullName, "explorer.exe");
            string explorerPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            System.Diagnostics.Process.Start(explorerPath, shortcutFilePath);
        }

    }

}
