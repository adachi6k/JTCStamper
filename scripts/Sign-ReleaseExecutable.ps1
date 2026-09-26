[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][string]$CertificateThumbprint
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($CertificateThumbprint -notmatch '\A[0-9A-Fa-f]{40}\z') { throw 'Expected an explicit certificate thumbprint.' }
$cert = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
if (!$cert.HasPrivateKey -or $cert.NotBefore -gt (Get-Date) -or $cert.NotAfter -le (Get-Date)) { throw 'Signing certificate has no private key or is outside its validity period.' }
if ('1.3.6.1.5.5.7.3.3' -notin @($cert.EnhancedKeyUsageList.ObjectId.Value)) { throw 'Code-signing EKU is required.' }
if ($cert.Subject -ne $cert.Issuer) { throw 'This workflow is for self-signed certificates only.' }
$file = (Resolve-Path -LiteralPath $Path).Path
if ([IO.Path]::GetExtension($file) -ne '.exe') { throw 'Only release EXE files are supported.' }
Set-AuthenticodeSignature -LiteralPath $file -Certificate $cert -HashAlgorithm SHA256 | Out-Null
$signature = Get-AuthenticodeSignature -LiteralPath $file
if (!$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $cert.Thumbprint) { throw 'Signing did not produce the expected signer.' }
# Windows does not trust a self-signed signer by default. Inspect the native result,
# not a localized StatusMessage or an arbitrary UnknownError.
if (-not ('JtcAuthenticodeVerifier' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class JtcAuthenticodeVerifier {
 [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
 struct FileInfo { public uint cbStruct; public IntPtr path; public IntPtr handle; public IntPtr subject; }
 [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
 struct TrustData {
  public uint cbStruct; public IntPtr callback,client; public uint ui,revocation,choice;
  public IntPtr file; public uint state; public IntPtr stateData,url; public uint flags,context;
 }
 [DllImport("wintrust.dll", ExactSpelling=true, PreserveSig=true)]
 static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
 public static uint Verify(string path) {
  IntPtr text=Marshal.StringToCoTaskMemUni(path), file=IntPtr.Zero;
  try {
   var f=new FileInfo { cbStruct=(uint)Marshal.SizeOf(typeof(FileInfo)),path=text };
   file=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(FileInfo)));Marshal.StructureToPtr(f,file,false);
   var d=new TrustData { cbStruct=(uint)Marshal.SizeOf(typeof(TrustData)),ui=2,choice=1,file=file,flags=0x1000 };
   var action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
   return unchecked((uint)WinVerifyTrust(new IntPtr(-1),ref action,ref d));
  } finally { if(file!=IntPtr.Zero)Marshal.FreeHGlobal(file);Marshal.FreeCoTaskMem(text); }
 }
}
'@
}
$result = [JtcAuthenticodeVerifier]::Verify($file)
# CERT_E_UNTRUSTEDROOT is expected; any other error, including a bad digest, fails.
if ($result -ne 0 -and $result -ne [uint32]2148204809) { throw ('Authenticode verification failed: 0x{0:X8}' -f $result) }
$sha = [Security.Cryptography.SHA256]::Create()
try { $certificateHash = [BitConverter]::ToString($sha.ComputeHash($cert.RawData)).Replace('-', '') } finally { $sha.Dispose() }
[pscustomobject]@{
    Mode = 'SelfSigned'; Thumbprint = $cert.Thumbprint; Subject = $cert.Subject
    CertificateSha256 = $certificateHash
    NotAfterUtc = $cert.NotAfter.ToUniversalTime().ToString('o')
    WindowsTrust = if ($result -eq 0) { 'TrustedOnThisMachine' } else { 'UntrustedRoot' }
    Timestamped = $false
}
