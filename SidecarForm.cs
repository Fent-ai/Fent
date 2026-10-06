using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ClaudeSidecar
{
    /// <summary>
    /// Step 1 of the sidecar: a read-only window beside ECU Manager that lists live channels,
    /// shows their raw values and update rates, and records sessions to CSV.
    /// </summary>
    public sealed class SidecarForm : Form
    {
        // Channels watched by default when they exist on this ECU (ECU Manager's own names).
        static readonly string[] DefaultWatch =
        {
            "RPM", "MAPSensor", "ThrottlePosition", "Load", "IgnitionLoad",
            "LambdaSensor1", "LambdaSensor2", "IgnitionTiming", "FuelTime", "CurrentDutyCycle",
            "CoolantTemp", "AirTemp", "BatteryVoltage", "FuelPressure", "OilPressure",
            "KnockLevel", "KnockLevel_2", "KnockAppliedRetardBank1",
            "BoostControl_Target", "BoostControl_Actual", "BoostControl_Output",
            "O2ControlActive", "ShortTermFuelTrimBank1", "AppliedLongTermFuelTrimBank1",
            "ActualIntakeCamAngle1", "DesiredIntakeCamAngle1",
            "Gear", "RoadSpeed", "EngineRunningTime"
        };

        readonly EcuBridge bridge;
        readonly Form owner;
        readonly LiveCapture capture = new LiveCapture();
        readonly Dictionary<int, ChannelInfo> channels = new Dictionary<int, ChannelInfo>();
        readonly Dictionary<int, ListViewItem> rows = new Dictionary<int, ListViewItem>();
        readonly Dictionary<int, long> lastCounts = new Dictionary<int, long>();
        readonly Stopwatch rateClock = Stopwatch.StartNew();

        readonly ListView list;
        readonly Label status, recordInfo;
        readonly Button recordButton;
        readonly CheckBox showAll;
        readonly Timer uiTimer;
        bool populating, dumpedChannelList;

        public SidecarForm(EcuBridge bridge, Form owner)
        {
            this.bridge = bridge;
            this.owner = owner;

            Text = "Sidecar (read-only)";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimumSize = new Size(300, 300);
            Size = new Size(380, Math.Max(500, owner.Height));

            status = new Label { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 6, 8, 0) };
            var help = new Label
            {
                Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 0, 8, 0), ForeColor = SystemColors.GrayText,
                Text = "Tick a channel to watch it. Values are raw ECU numbers for now. This window never writes to the ECU."
            };

            list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true,
                HideSelection = false, Sorting = SortOrder.None
            };
            list.Columns.Add("Channel", 190);
            list.Columns.Add("Raw value", 80, HorizontalAlignment.Right);
            list.Columns.Add("Per sec", 60, HorizontalAlignment.Right);
            list.ItemChecked += OnItemChecked;

            showAll = new CheckBox { Text = "Show channels this ECU doesn't have", Dock = DockStyle.Top, Height = 26, Padding = new Padding(8, 0, 0, 0) };
            showAll.CheckedChanged += (s, e) => RebuildRows();

            recordButton = new Button { Text = "Start recording", Width = 120, Height = 28 };
            recordButton.Click += (s, e) => ToggleRecording();
            var folderButton = new Button { Text = "Open folder", Width = 100, Height = 28 };
            folderButton.Click += (s, e) => Process.Start("explorer.exe", Sidecar.DataFolder);
            recordInfo = new Label { AutoSize = true, Padding = new Padding(0, 7, 0, 0) };
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(6), WrapContents = true };
            bottom.Controls.AddRange(new Control[] { recordButton, folderButton, recordInfo });

            Controls.Add(list);
            Controls.Add(showAll);
            Controls.Add(help);
            Controls.Add(status);
            Controls.Add(bottom);

            owner.LocationChanged += (s, e) => PositionBesideOwner();
            owner.SizeChanged += (s, e) => PositionBesideOwner();
            Load += (s, e) => { PositionBesideOwner(); RefreshChannels(); };
            FormClosing += OnClosing;

            uiTimer = new Timer { Interval = 500 };
            uiTimer.Tick += (s, e) => Tick();
            uiTimer.Start();
        }

        /// <summary>Sit against the right edge of ECU Manager, or inside the screen if there's no room.</summary>
        void PositionBesideOwner()
        {
            if (owner.WindowState == FormWindowState.Minimized) return;
            Rectangle screen = Screen.FromControl(owner).WorkingArea;
            int x = owner.Right;
            if (x + Width > screen.Right) x = Math.Max(screen.Left, screen.Right - Width);
            Location = new Point(x, Math.Max(screen.Top, owner.Top));
            Height = Math.Min(screen.Height, Math.Max(MinimumSize.Height, owner.Height));
        }

        void RefreshChannels()
        {
            List<ChannelInfo> found = bridge.GetChannels();
            if (found.Count == 0) return;
            bool changed = false;
            foreach (ChannelInfo c in found)
            {
                ChannelInfo existing;
                if (channels.TryGetValue(c.Id, out existing))
                {
                    if (existing.Exists != c.Exists) { existing.Exists = c.Exists; changed = true; }
                    if (!ReferenceEquals(existing.Channel, c.Channel))
                    {
                        // ECU Manager rebuilt its channel objects (e.g. a different map was opened): move our subscription.
                        bool watched = existing.Handler != null;
                        bridge.Unsubscribe(existing);
                        existing.Channel = c.Channel;
                        if (watched) bridge.Subscribe(existing, OnValue);
                    }
                    continue;
                }
                channels[c.Id] = c;
                capture.Names[c.Id] = c.Name;
                changed = true;
                if (c.Exists && DefaultWatch.Contains(c.Name)) bridge.Subscribe(c, OnValue);
            }
            if (changed) RebuildRows();
            if (!dumpedChannelList && channels.Values.Any(c => c.Exists)) DumpChannelList();
        }

        void OnValue(ChannelInfo c, int raw) { capture.OnValue(c.Id, raw); }

        void RebuildRows()
        {
            populating = true;
            list.BeginUpdate();
            list.Items.Clear();
            rows.Clear();
            foreach (ChannelInfo c in channels.Values
                         .Where(c => c.Exists || showAll.Checked)
                         .OrderByDescending(c => c.Handler != null)
                         .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                var item = new ListViewItem(new[] { c.Name, "", "" }) { Tag = c, Checked = c.Handler != null };
                if (!c.Exists) item.ForeColor = SystemColors.GrayText;
                list.Items.Add(item);
                rows[c.Id] = item;
            }
            list.EndUpdate();
            populating = false;
        }

        void OnItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (populating) return;
            var c = (ChannelInfo)e.Item.Tag;
            if (e.Item.Checked) bridge.Subscribe(c, OnValue);
            else
            {
                bridge.Unsubscribe(c);
                e.Item.SubItems[1].Text = "";
                e.Item.SubItems[2].Text = "";
            }
        }

        int ticks;
        void Tick()
        {
            if (++ticks % 4 == 0) RefreshChannels(); // pick up channels when a map is opened or the ECU connects

            double secs = Math.Max(0.001, rateClock.Elapsed.TotalSeconds);
            rateClock.Restart();
            int watched = 0;
            double totalRate = 0;
            list.BeginUpdate();
            foreach (KeyValuePair<int, ListViewItem> kv in rows)
            {
                var c = (ChannelInfo)kv.Value.Tag;
                if (c.Handler == null) continue;
                watched++;
                int raw;
                if (capture.Latest.TryGetValue(kv.Key, out raw)) kv.Value.SubItems[1].Text = raw.ToString();
                long count; capture.Counts.TryGetValue(kv.Key, out count);
                long last; lastCounts.TryGetValue(kv.Key, out last);
                lastCounts[kv.Key] = count;
                double rate = (count - last) / secs;
                totalRate += rate;
                kv.Value.SubItems[2].Text = rate.ToString("0.0");
            }
            list.EndUpdate();

            bool online = bridge.Online;
            status.Text = (online ? "Connected to ECU" : "Not connected to ECU (open a map and connect to see live values)") +
                          Environment.NewLine + watched + " channels watched, " + totalRate.ToString("0") + " values per second";
            status.ForeColor = online ? Color.DarkGreen : SystemColors.ControlText;

            if (capture.Recording)
                recordInfo.Text = "Recording: " + capture.SamplesWritten.ToString("N0") + " values saved";
        }

        void ToggleRecording()
        {
            if (capture.Recording)
            {
                capture.Stop();
                recordButton.Text = "Start recording";
                recordInfo.Text = "Saved " + Path.GetFileName(capture.CurrentFile);
            }
            else
            {
                capture.Start();
                recordButton.Text = "Stop recording";
                recordInfo.Text = "Recording…";
            }
        }

        /// <summary>Writes every channel name and ID to channels.txt, which helps the next build step.</summary>
        void DumpChannelList()
        {
            dumpedChannelList = true;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Channels reported by ECU Manager, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                sb.AppendLine("id,name,exists_on_this_ecu");
                foreach (ChannelInfo c in channels.Values.OrderBy(c => c.Id))
                    sb.AppendLine(c.Id + "," + c.Name + "," + (c.Exists ? "yes" : "no"));
                File.WriteAllText(Path.Combine(Sidecar.DataFolder, "channels.txt"), sb.ToString());
            }
            catch { }
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            // Closing the sidecar stops recording and listening; ECU Manager carries on.
            uiTimer.Stop();
            capture.Stop();
            foreach (ChannelInfo c in channels.Values) bridge.Unsubscribe(c);
        }
    }
}
