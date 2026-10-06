using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Windows.Forms;

namespace ClaudeSidecar
{
    /// <summary>
    /// Starts Haltech ECU Manager inside this process, then opens the sidecar window
    /// next to it once ECU Manager's main window appears. ECUManager.exe is never modified.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            string ecuExe = Path.Combine(dir, "ECUManager.exe");

            if (!File.Exists(ecuExe))
            {
                MessageBox.Show(
                    "ECUManager.exe wasn't found next to this program.\n\n" +
                    "Copy ECUManagerSidecar.exe and ECUManagerSidecar.exe.config into the folder that contains ECUManager.exe, then run it from there.\n\n" +
                    "To find that folder: right-click your ECU Manager shortcut and choose Open file location.",
                    "ECU Manager Sidecar", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }

            // Let ECU Manager find its own DLLs (gauges, 3D view, zip) in its folder.
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                string name = new AssemblyName(e.Name).Name;
                foreach (string ext in new[] { ".dll", ".exe" })
                {
                    string p = Path.Combine(dir, name + ext);
                    if (File.Exists(p)) return Assembly.LoadFrom(p);
                }
                return null;
            };

            Assembly ecu;
            try { ecu = Assembly.LoadFrom(ecuExe); }
            catch (Exception ex) { return Fail("Couldn't load ECUManager.exe.", ex); }

            var attacher = new Attacher(ecu);
            Application.Idle += attacher.OnIdle;

            try
            {
                // Run ECU Manager's own startup code, exactly as if it were launched normally.
                ecu.EntryPoint.Invoke(null, new object[] { args });
            }
            catch (TargetInvocationException ex) { return Fail("ECU Manager stopped with an error.", ex.InnerException ?? ex); }
            catch (Exception ex) { return Fail("ECU Manager stopped with an error.", ex); }
            return 0;
        }

        internal static int Fail(string what, Exception ex)
        {
            try
            {
                string log = Path.Combine(Sidecar.DataFolder, "sidecar-error.txt");
                File.AppendAllText(log, DateTime.Now.ToString("o") + "  " + what + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
            MessageBox.Show(what + "\n\n" + ex.Message + "\n\nDetails were saved to Documents\\ECU Manager Sidecar\\sidecar-error.txt",
                "ECU Manager Sidecar", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }
    }

    /// <summary>Waits for ECU Manager's main window, then opens the sidecar beside it.</summary>
    sealed class Attacher
    {
        readonly Assembly ecu;
        readonly Type mainFrameType;
        bool attached;

        public Attacher(Assembly ecu)
        {
            this.ecu = ecu;
            mainFrameType = ecu.GetType("com.Haltech.ECUManager.SwfUI.MainFrame", false);
        }

        public void OnIdle(object sender, EventArgs e)
        {
            if (attached || mainFrameType == null) return;
            Form main = Application.OpenForms.Cast<Form>()
                .FirstOrDefault(f => mainFrameType.IsInstanceOfType(f) && f.Visible && f.IsHandleCreated);
            if (main == null) return;

            attached = true;
            Application.Idle -= OnIdle;
            try
            {
                var bridge = new EcuBridge(ecu, main);
                var window = new SidecarForm(bridge, main);
                window.Show(main);
            }
            catch (Exception ex)
            {
                // Never take ECU Manager down with us: report and carry on without the sidecar.
                Program.Fail("The sidecar couldn't attach. ECU Manager will keep running normally.", ex);
            }
        }
    }

    static class Sidecar
    {
        public static string DataFolder
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ECU Manager Sidecar");
                Directory.CreateDirectory(d);
                return d;
            }
        }
    }
}
