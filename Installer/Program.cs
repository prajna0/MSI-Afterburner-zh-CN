using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Afterburner / RTSS 简体中文安装器")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.1.0")]
[assembly: System.Reflection.AssemblyVersion("1.0.1.0")]

namespace ZhCnInstaller
{
    internal static class Program
    {
        internal const string Title = "Afterburner / RTSS 简体中文安装器";

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(uint processId);

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 0)
            {
                Application.Run(new InstallerForm());
                return 0;
            }

            bool showResult = args[0] == "--show-result";
            if (!showResult) AttachConsole(UInt32.MaxValue);
            try
            {
                var targets = new List<Target>();
                for (int i = showResult ? 1 : 0; i < args.Length; i += 2)
                {
                    Product product;
                    if (args[i] == "--afterburner") product = Product.Afterburner;
                    else if (args[i] == "--rtss") product = Product.Rtss;
                    else throw new ArgumentException("参数应为 --afterburner <目录> 或 --rtss <目录>。");

                    if (i + 1 >= args.Length || targets.Any(x => x.Product == product))
                        throw new ArgumentException("请为每个程序指定一个安装目录。");
                    targets.Add(new Target(product, args[i + 1]));
                }

                var summary = Installation.Install(targets);
                if (showResult) MessageBox.Show(summary, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                else Console.WriteLine(summary);
                return 0;
            }
            catch (Exception ex)
            {
                if (showResult) MessageBox.Show(ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        internal static bool Elevated
        {
            get
            {
                using (var identity = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        internal static int InstallElevated(IEnumerable<Target> targets)
        {
            // Target paths have been resolved and validated by Target on both sides.
            string args = "--show-result " + String.Join(" ", targets.Select(t =>
                (t.Product == Product.Afterburner ? "--afterburner " : "--rtss ") + QuotePath(t.Root)));
            using (var worker = Process.Start(new ProcessStartInfo(Application.ExecutablePath, args)
            {
                UseShellExecute = true,
                Verb = "runas"
            }))
            {
                worker.WaitForExit();
                return worker.ExitCode;
            }
        }

        private static string QuotePath(string path)
        {
            return "\"" + path + (path.EndsWith("\\", StringComparison.Ordinal) ? "\\" : "") + "\"";
        }
    }

    internal sealed class InstallerForm : Form
    {
        private readonly List<ProductRow> rows = new List<ProductRow>();
        private readonly Label status;
        private readonly Button install;
        private bool busy;

        internal InstallerForm()
        {
            Text = Program.Title;
            Font = new Font("Microsoft YaHei UI", 10);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(690, 318);
            MinimumSize = new Size(650, 350);
            MaximizeBox = false;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24, 18, 24, 20),
                ColumnCount = 1,
                RowCount = 5
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            Controls.Add(layout);

            layout.Controls.Add(new Label
            {
                Text = "安装简体中文和中文皮肤",
                Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            AddProduct(layout, Product.Afterburner, 1);
            AddProduct(layout, Product.Rtss, 2);

            status = new Label
            {
                Text = "选择要安装的程序，并先从系统托盘退出它。旧文件会自动备份。",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            layout.Controls.Add(status, 0, 3);

            install = new Button
            {
                Text = "安装汉化",
                AutoSize = true,
                MinimumSize = new Size(110, 33),
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom
            };
            install.Click += InstallClick;
            layout.Controls.Add(install, 0, 4);
            AcceptButton = install;
            FormClosing += (s, e) => { if (busy && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; };
        }

        private void AddProduct(TableLayoutPanel parent, Product product, int index)
        {
            var row = new ProductRow(product);
            var layout = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, Dock = DockStyle.Fill };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            layout.Controls.Add(row.Selected, 0, 0);
            layout.SetColumnSpan(row.Selected, 2);
            layout.Controls.Add(row.Path, 0, 1);
            layout.Controls.Add(row.Browse, 1, 1);

            row.Browse.Click += (s, e) =>
            {
                using (var picker = new FolderBrowserDialog())
                {
                    picker.Description = "选择包含 " + product.Executable + " 的文件夹";
                    picker.ShowNewFolderButton = false;
                    if (System.IO.Directory.Exists(row.Path.Text)) picker.SelectedPath = row.Path.Text;
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    row.Path.Text = picker.SelectedPath;
                    row.Selected.Checked = true;
                }
            };
            rows.Add(row);
            parent.Controls.Add(layout, 0, index);
        }

        private async void InstallClick(object sender, EventArgs e)
        {
            busy = true;
            install.Enabled = false;
            foreach (var row in rows) { row.Selected.Enabled = row.Path.Enabled = row.Browse.Enabled = false; }
            try
            {
                var targets = rows.Where(row => row.Selected.Checked)
                    .Select(row => new Target(row.Product, row.Path.Text)).ToArray();
                if (targets.Length == 0) throw new InvalidOperationException("请先勾选要安装的软件。");
                status.Text = "正在备份并安装汉化，请稍候。";
                string summary = null;
                try
                {
                    summary = await Task.Run(() => Installation.Install(targets));
                }
                catch (UnauthorizedAccessException)
                {
                    if (Program.Elevated) throw;
                }
                if (summary != null)
                {
                    MessageBox.Show(this, summary, Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    status.Text = "安装完成。重新打开对应软件即可使用。";
                }
                else
                {
                    status.Text = "写入安装目录需要管理员权限，请确认 Windows 提示。";
                    int code = await Task.Run(() => Program.InstallElevated(targets));
                    status.Text = code == 0 ? "安装完成。重新打开对应软件即可使用。" : "安装未完成，可根据提示重试。";
                }
            }
            catch (Exception ex)
            {
                var nativeError = ex as Win32Exception;
                if (nativeError != null && nativeError.NativeErrorCode == 1223)
                {
                    status.Text = "已取消授权。";
                    return;
                }
                status.Text = "安装未完成，可根据提示重试。";
                MessageBox.Show(this, ex.Message, Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                foreach (var row in rows) { row.Selected.Enabled = row.Path.Enabled = row.Browse.Enabled = true; }
                install.Enabled = true;
                busy = false;
            }
        }

        private sealed class ProductRow
        {
            internal readonly Product Product;
            internal readonly CheckBox Selected;
            internal readonly TextBox Path;
            internal readonly Button Browse;

            internal ProductRow(Product product)
            {
                Product = product;
                string path = product.FindDirectory();
                Selected = new CheckBox { Text = product.Name, Checked = path != null, AutoSize = true };
                Path = new TextBox { Text = path ?? "", Dock = DockStyle.Top };
                Browse = new Button { Text = "浏览…", Dock = DockStyle.Top, Height = 28 };
            }
        }
    }
}
