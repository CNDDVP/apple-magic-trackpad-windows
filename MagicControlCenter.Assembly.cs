// The legacy csc compiler does not infer a target framework from app.config.
// WinForms enables its modern DPI path only for a retargeted assembly.
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
