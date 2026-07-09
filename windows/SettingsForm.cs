using System;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Windows.Forms;
using System.Linq;
using System.Runtime.InteropServices;

namespace Uplink
{
    public class SettingsForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private Form1 mainForm;
        private ComboBox adapterCombo;
        private RadioButton rbBytes;
        private RadioButton rbBits;
        private TrackBar refreshSlider;
        private Label lblRefreshValue;

        public SettingsForm(Form1 mainForm)
        {
            this.mainForm = mainForm;
            InitializeComponent();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            this.Text = "Uplink - Settings";
            this.Size = new Size(350, 350);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            
            bool isLight = mainForm.IsLightTheme();
            this.BackColor = isLight ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32);
            this.ForeColor = isLight ? Color.Black : Color.White;
            this.Font = new Font("Segoe UI", 9);

            // Enable Windows 11 native dark title bar
            if (!isLight)
            {
                int dark = 1;
                DwmSetWindowAttribute(this.Handle, 20, ref dark, sizeof(int)); // newer windows
                DwmSetWindowAttribute(this.Handle, 19, ref dark, sizeof(int)); // older windows
            }

            Label lblAdapter = new Label { Text = "Network Adapter:", Location = new Point(20, 20), AutoSize = true };
            adapterCombo = new ComboBox { Location = new Point(20, 45), Width = 290, DropDownStyle = ComboBoxStyle.DropDownList };
            
            Label lblUnits = new Label { Text = "Display Units:", Location = new Point(20, 85), AutoSize = true };
            rbBytes = new RadioButton { Text = "Bytes (MB/s)", Location = new Point(20, 110), AutoSize = true, Checked = !mainForm.useBits };
            rbBits = new RadioButton { Text = "Bits (Mbps)", Location = new Point(120, 110), AutoSize = true, Checked = mainForm.useBits };

            Label lblRefresh = new Label { Text = "Refresh Rate:", Location = new Point(20, 155), AutoSize = true };
            lblRefreshValue = new Label { Text = $"{mainForm.refreshInterval} ms", Location = new Point(250, 155), AutoSize = true };
            
            refreshSlider = new TrackBar { Location = new Point(20, 185), Width = 290, Minimum = 1, Maximum = 50, Value = mainForm.refreshInterval / 100, TickFrequency = 5 };
            refreshSlider.ValueChanged += (s, e) => lblRefreshValue.Text = $"{refreshSlider.Value * 100} ms";

            Button btnSave = new Button { Text = "Save", Location = new Point(140, 260), Width = 80, Height = 30, FlatStyle = FlatStyle.System };
            btnSave.Click += BtnSave_Click;

            Button btnCancel = new Button { Text = "Cancel", Location = new Point(230, 260), Width = 80, Height = 30, FlatStyle = FlatStyle.System };
            btnCancel.Click += (s, e) => this.Close();

            if (!isLight)
            {
                adapterCombo.BackColor = Color.FromArgb(43, 43, 43);
                adapterCombo.ForeColor = Color.White;
                adapterCombo.FlatStyle = FlatStyle.Flat;

                btnSave.FlatStyle = FlatStyle.Flat;
                btnSave.BackColor = Color.FromArgb(60, 60, 60);
                btnSave.ForeColor = Color.White;
                btnSave.FlatAppearance.BorderSize = 0;

                btnCancel.FlatStyle = FlatStyle.Flat;
                btnCancel.BackColor = Color.FromArgb(60, 60, 60);
                btnCancel.ForeColor = Color.White;
                btnCancel.FlatAppearance.BorderSize = 0;
            }

            this.Controls.Add(lblAdapter);
            this.Controls.Add(adapterCombo);
            this.Controls.Add(lblUnits);
            this.Controls.Add(rbBytes);
            this.Controls.Add(rbBits);
            this.Controls.Add(lblRefresh);
            this.Controls.Add(lblRefreshValue);
            this.Controls.Add(refreshSlider);
            this.Controls.Add(btnSave);
            this.Controls.Add(btnCancel);
        }

        private void LoadSettings()
        {
            adapterCombo.Items.Add("Auto");
            
            // Aggressively filter out virtual/hidden/internal adapters to make it user-friendly
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Where(ni => !ni.Name.Contains("-WFP") &&
                             !ni.Name.Contains("-QoS") &&
                             !ni.Name.Contains("Bluetooth") &&
                             !ni.Name.Contains("Debugger") &&
                             !ni.Name.StartsWith("Local Area Connection*") &&
                             !ni.Description.Contains("Virtual") &&
                             !ni.Description.Contains("Pseudo"))
                .ToArray();

            foreach (var ni in interfaces)
            {
                adapterCombo.Items.Add(new ComboBoxItem(ni.Name, ni.Id));
            }
            
            int adapterIndex = 0;
            if (!string.IsNullOrEmpty(mainForm.selectedAdapterId) && mainForm.selectedAdapterId != "Auto")
            {
                for(int i = 1; i < adapterCombo.Items.Count; i++)
                {
                    if (((ComboBoxItem)adapterCombo.Items[i]).Id == mainForm.selectedAdapterId)
                    {
                        adapterIndex = i;
                        break;
                    }
                }
            }
            adapterCombo.SelectedIndex = adapterIndex;

            if (mainForm.useBits) rbBits.Checked = true; else rbBytes.Checked = true;

            int tickValue = mainForm.refreshInterval / 100;
            if (tickValue < 1) tickValue = 1;
            if (tickValue > 50) tickValue = 50;
            refreshSlider.Value = tickValue;
            lblRefreshValue.Text = $"{refreshSlider.Value * 100} ms";
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (adapterCombo.SelectedIndex == 0)
                mainForm.selectedAdapterId = "Auto";
            else
                mainForm.selectedAdapterId = ((ComboBoxItem)adapterCombo.SelectedItem).Id;

            mainForm.useBits = rbBits.Checked;
            mainForm.refreshInterval = refreshSlider.Value * 100;

            mainForm.ApplySettings();
            this.Close();
        }
    }

    public class ComboBoxItem
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public ComboBoxItem(string name, string id) { Name = name; Id = id; }
        public override string ToString() => Name;
    }
}
