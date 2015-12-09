--------------------------------------------
lame_enc.dll is included in the Recorder project because otherwise ClickOnce publishing does not include it into a distrbutive.
--------------------------------------------
ClickOnce publishing takes the executable from the obj folder, not from bin.
--------------------------------------------
Do not sign strong name for the assembly. It interferes with Authenticode signing.
--------------------------------------------
"Create application without a manifest" remedies hash exception when ClickOnce installs the exe file.
--------------------------------------------
+http://blogs.skype.com/2013/11/06/feature-evolution-and-support-for-the-skype-desktop-api/
+https://support.skype.com/en/faq/FA12395/how-can-i-record-my-skype-calls
--------------------------------------------
Skype does not accept attachment from a process being debugged.
--------------------------------------------
--------------------------------------------
