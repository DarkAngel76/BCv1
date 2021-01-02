# Load WinSCP .NET assembly
#Add-Type -Path "$PSScriptRoot\WinSCPnet.dll"
Add-Type -Path "C:\Data\Development\Includes\WinSCP-5.17.9-Automation\WinSCPnet.dll"

function FileTransferProgress {
param($e)
Write-Progress `
-Activity "Uploading" -Status ("{0:P0} complete:" -f $e.OverallProgress) `
-PercentComplete ($e.OverallProgress * 100)
Write-Progress `
-Id 1 -Activity $e.FileName -Status ("{0:P0} complete:" -f $e.FileProgress) `
-PercentComplete ($e.FileProgress * 100)
}
# Set up session options
$sessionOptions = New-Object WinSCP.SessionOptions -Property @{
Protocol              = [WinSCP.Protocol]::Sftp
HostName              = "<raspberry ip>"
UserName              = "pi"
Password              = "raspberry"
SshHostKeyFingerprint = "ssh-ed25519 255 <please see README>"
}
$session = New-Object WinSCP.Session
try {
# Will continuously report progress of transfer
$session.add_FileTransferProgress( { FileTransferProgress($_) } )
# Connect
$session.Open($sessionOptions)
try {
$session.ExecuteCommand("killall BCv1").Check();
}
catch {
Write-Host 'didnt kill BCv1 because it wasnt running '
}
Start-Process dotnet -ArgumentList 'publish -r linux-arm -f netcoreapp3.1' -Wait -NoNewWindow -WorkingDirectory $PSScriptRoot
$result = $session.PutFiles("$PSScriptRoot\bin\Debug\netcoreapp3.1\linux-arm\publish\*", "programs/BCv1/").Check();
Write-Host $result
$session.ExecuteCommand("chown pi /home/pi/programs/BCv1 -R").Check();
$session.ExecuteCommand("chmod 777 /home/pi/programs/BCv1 -R").Check();
# Comment out if you want to run the app at once.
#$session.ExecuteCommand("programs/BCv1/BCv1").Check();
}
finally {
$session.Dispose()
}