Script descriptions:

**GPOLogging.cs**\
Retrofitted from the ControlUp GPO analyzer: https://www.controlup.com/script-library-posts/analyze-detailed-gpo-duration/
Allows for individual GPOs to be reported during a test since LE only shows the total GPO processing time. Ensure GPO logging is enabled using the ps1's -enable flag and that the ps1 location is properly defined in the script. Note that names are shortened due to character limits on timers.

<img width="742" height="314" alt="image" src="https://github.com/user-attachments/assets/a400df44-3614-4c14-82fb-1431a490a399" /> 
<br>
<br>
**Analyze-Logon-Duration.cs**
Retrofitted from the ControlUp Analyze Logon Duration: https://www.controlup.com/script-library-posts/analyze-logon-duration/
Allows for the logon process to be broken down and imported into LE timers. Due limitations of how the script collects the logon process, it must be ran as an administrator during the test. The results look something like this:

<img width="1047" height="725" alt="image" src="https://github.com/user-attachments/assets/22fc0e8c-738b-4d92-96ec-509aada95aba" />
