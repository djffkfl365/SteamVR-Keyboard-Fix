If WScript.Arguments.Count = 0 Then
    WScript.Echo "[PatchMsi Error] Cannot find MSI file path! (Macro check required)"
    WScript.Quit 1
End If

Dim installer, database, view, record
Dim msiPath : msiPath = WScript.Arguments(0)

' Create Windows Installer COM object
Set installer = CreateObject("WindowsInstaller.Installer")

' Open the MSI file in write mode (2)
Set database = installer.OpenDatabase(msiPath, 2)

' 1. Search the File table to automatically find the Component ID of SteamVRKeyboardFix.exe
Dim compId : compId = ""
Set view = database.OpenView("SELECT `Component_`, `FileName` FROM `File`")
view.Execute
Set record = view.Fetch

Do While Not (record Is Nothing)
    ' Check if the FileName column contains "SteamVRKeyboardFix.exe" (Handles ShortName|LongName format)
    If InStr(record.StringData(2), "SteamVRKeyboardFix.exe") > 0 Then
        compId = record.StringData(1)
        Exit Do
    End If
    Set record = view.Fetch
Loop

' If Component ID is not found, exit with error code 1
If compId = "" Then
    WScript.Echo "[PatchMsi Error] Could not find SteamVRKeyboardFix.exe in the File table. (Check with Orca)"
    WScript.Quit 1
End If

WScript.Echo "[PatchMsi] Auto-detected Component ID: " & compId

' 2. Execute table creation query for ServiceControl if it doesn't exist
Dim sqlCreate
sqlCreate = "CREATE TABLE `ServiceControl` (`ServiceControl` CHAR(72) NOT NULL, `Name` CHAR(255) NOT NULL, `Event` SHORT NOT NULL, `Arguments` CHAR(255), `Wait` SHORT, `Component_` CHAR(72) NOT NULL PRIMARY KEY `ServiceControl`)"

On Error Resume Next ' IF table already exists, ignore the error and continue
Set view = database.OpenView(sqlCreate)
view.Execute
Err.Clear ' 에러 초기화

' 3. Stop Service control command (INSERT)
' Warning: Component_ value is the unique ID of the target file in vdproj.
Dim sqlInsert
sqlInsert = "INSERT INTO `ServiceControl` (`ServiceControl`, `Name`, `Event`, `Wait`, `Component_`) " & _
            "VALUES ('StopSVKF', 'SteamVRKeyboardFix', 34, 1, '" & compId & "')"

Set view = database.OpenView(sqlInsert)
view.Execute

If Err.Number = 0 Then
    ' Save changes when successfully executed
    database.Commit
    WScript.Echo "[PatchMsi] ServiceControl updated successfully! (In-use file warning disabled)"
Else
    WScript.Echo "[PatchMsi] Already applied or error occurred: " & Err.Description
End If

Set view = Nothing
Set database = Nothing
Set installer = Nothing
