using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace MagicForceClick.Tests
{
    internal static class IntegrationTests
    {
        [STAThread] public static int Main()
        {
            string path=Path.Combine(Path.GetTempPath(),"MagicForceClick-reload-"+Guid.NewGuid().ToString("N")+".json");
            try
            {
                var type=typeof(RawInputSinkForm);
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                using(var form=new RawInputSinkForm(null))
                {
                    if(form.IsHandleCreated)throw new Exception("Headless test unexpectedly registered input.");
                    type.GetField("_configPath",flags).SetValue(form,path);
                    var method=type.GetMethod("LoadConfiguration",flags);
                    var field=type.GetField("_options",flags);
                    var o=new ForceClickOptions { EnableAcceleration=false, EnableMonitorCompensation=false, ForceClickThreshold=190 };
                    o.Save(path);method.Invoke(form,new object[]{true});
                    var after=(ForceClickOptions)field.GetValue(form);
                    if(after.EnableAcceleration||after.EnableMonitorCompensation||after.ForceClickThreshold!=190)throw new Exception("Valid reload failed.");
                    Console.WriteLine("PASS runtime applies independent options without input registration");
                    File.WriteAllText(path,"{broken");File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddSeconds(2));method.Invoke(form,new object[]{false});
                    if(!object.ReferenceEquals(after,field.GetValue(form)))throw new Exception("Invalid reload replaced last good configuration.");
                    Console.WriteLine("PASS invalid live reload retains exact last good settings");
                    o.EnableAcceleration=true;o.Save(path);File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddSeconds(4));method.Invoke(form,new object[]{false});
                    if(!((ForceClickOptions)field.GetValue(form)).EnableAcceleration)throw new Exception("Recovery from invalid configuration failed.");
                    Console.WriteLine("PASS valid configuration recovers after invalid edit");
                }
                using(var settings=new SettingsForm(path))
                {
                    int checks=0,numbers=0;
                    Count(settings,ref checks,ref numbers);
                    if(checks!=4||numbers!=6)throw new Exception("Settings controls missing.");
                    Console.WriteLine("PASS settings panel has four independent switches and six adjustments");
                }
                Console.WriteLine("RESULT integration_passed=4 failed=0");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
            finally {if(File.Exists(path))File.Delete(path);}
        }
        static void Count(Control parent,ref int checks,ref int numbers)
        {foreach(Control c in parent.Controls){if(c is CheckBox)checks++;if(c is NumericUpDown)numbers++;Count(c,ref checks,ref numbers);}}
    }
}
