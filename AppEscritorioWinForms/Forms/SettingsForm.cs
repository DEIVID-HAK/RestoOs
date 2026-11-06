using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using app_escritorio.Data;
using app_escritorio.UI;

namespace app_escritorio.Forms
{
    /// <summary>
    /// Configuración (migración de ConfiguracionView.xaml de WPF). Solo Admin.
    /// Guarda los datos del negocio y el impuesto en SQL Server (tabla Configuracion)
    /// y crea respaldos completos de la base (BACKUP DATABASE) con fecha y hora.
    /// </summary>
    public partial class SettingsForm : Form
    {
        /// <summary>Se guardaron nombre/dirección (el Shell actualiza la sucursal del sidebar).</summary>
        public event EventHandler RestaurantSaved;

        private static readonly int[] TaxValues = { 0, 8, 19, 27 };
        private static string BackupRoot => Path.Combine(LocalSettings.AppDataDir, "backups");

        /// <summary>Tablas que se cuentan en "Datos guardados".</summary>
        private static readonly (string Table, string Label)[] DataTables =
        {
            ("Producto", "Platos"), ("Mesa", "Mesas"), ("Insumo", "Insumos"), ("Pedido", "Pedidos"),
            ("Venta", "Ventas"), ("Reserva", "Reservas"), ("Cliente", "Clientes"), ("Usuario", "Usuarios")
        };

        public SettingsForm()
        {
            InitializeComponent();
            if (UiHelpers.IsDesignTime) return;
            LoadData();
        }

        public void LoadData()
        {
            var info = LocalSettings.LoadRestaurant();
            txtName.Text = info.Name;
            txtAddress.Text = info.Address;
            txtPhone.Text = info.Phone;
            txtCurrency.Text = info.CurrencySymbol;
            txtLogo.Text = info.LogoPath;

            int tax = Array.IndexOf(TaxValues, LocalSettings.LoadTaxPercent());
            cmbTax.SelectedIndex = tax >= 0 ? tax : 1;

            try
            {
                string[] files = Directory.Exists(BackupRoot) ? Directory.GetFiles(BackupRoot, "*.bak") : new string[0];
                if (files.Length == 0) lblBackupInfo.Text = "Sin respaldos aún.";
                else
                {
                    Array.Sort(files);
                    string last = files[files.Length - 1];
                    lblBackupInfo.Text = files.Length + " respaldo(s). Último: " + Path.GetFileName(last) +
                                         " (" + (new FileInfo(last).Length / 1024) + " KB).";
                }
            }
            catch { lblBackupInfo.Text = "No se pudo leer la carpeta de respaldos."; }

            var sb = new StringBuilder();
            sb.AppendLine("SQL Server: " + Db.ServerName + " · base " + Db.DatabaseName);
            try
            {
                var counts = new List<string>();
                foreach (var (table, label) in DataTables)
                    counts.Add(label + ": " + Db.Scalar("SELECT COUNT(*) FROM dbo." + table));
                sb.AppendLine(string.Join(" · ", counts));
            }
            catch (Exception ex)
            {
                sb.AppendLine("No se pudo leer la base: " + ex.Message);
            }
            sb.Append("Conexión: " + (File.Exists(Db.ConnectionFile) ? Db.ConnectionFile : "LocalDB por defecto (crea " + Db.ConnectionFile + " para cambiarla)"));
            lblDataInfo.Text = sb.ToString();
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Filter = "Imágenes|*.jpg;*.jpeg;*.png;*.bmp|Todos|*.*" })
                if (dlg.ShowDialog(TopLevelControl ?? this) == DialogResult.OK) txtLogo.Text = dlg.FileName;
        }

        private void BtnSaveRestaurant_Click(object sender, EventArgs e)
        {
            string name = txtName.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show("El nombre del restaurante es requerido.", "RestoOS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtName.Focus();
                return;
            }
            try
            {
                LocalSettings.SaveRestaurant(new LocalSettings.RestaurantInfo
                {
                    Name = name,
                    Address = txtAddress.Text.Trim(),
                    Phone = txtPhone.Text.Trim(),
                    CurrencySymbol = txtCurrency.Text.Trim(),
                    LogoPath = txtLogo.Text.Trim()
                });
                RestaurantSaved?.Invoke(this, EventArgs.Empty);
                LoadData();
                MessageBox.Show("Datos guardados.", "RestoOS", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Db.ShowError("guardar los datos del restaurante", ex);
            }
        }

        private void BtnSaveTax_Click(object sender, EventArgs e)
        {
            int value = TaxValues[Math.Max(0, cmbTax.SelectedIndex)];
            try
            {
                LocalSettings.SaveTaxPercent(value);
                LoadData();
                MessageBox.Show("Impuesto por defecto: " + cmbTax.SelectedItem + ".", "RestoOS", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Db.ShowError("guardar el impuesto", ex);
            }
        }

        /// <summary>
        /// Respaldo completo de la base. Lo escribe el servicio de SQL Server: con LocalDB va a la carpeta de respaldos de la app;
        /// si el servicio no tiene permiso ahí (SQL Server Express), se usa su carpeta de respaldos por defecto.
        /// </summary>
        private void BtnBackup_Click(object sender, EventArgs e)
        {
            string fileName = "RestoOS_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak";
            string dest = Path.Combine(BackupRoot, fileName);
            try
            {
                Directory.CreateDirectory(BackupRoot);
                Backup(dest);
            }
            catch (Exception first)
            {
                try
                {
                    Backup(fileName);   // ruta relativa = carpeta de respaldos del servidor
                    dest = "la carpeta de respaldos de SQL Server (" + fileName + ")";
                }
                catch
                {
                    Db.ShowError("crear el respaldo", first);
                    return;
                }
            }
            LoadData();
            MessageBox.Show("Respaldo creado en:\n" + dest, "RestoOS", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void Backup(string path)
        {
            using (var cn = Db.Open())
            using (var cmd = Db.Cmd(cn, null, "BACKUP DATABASE [" + Db.DatabaseName + "] TO DISK = @f WITH INIT, NAME = N'RestoOS'", "@f", path))
            {
                cmd.CommandTimeout = 300;
                cmd.ExecuteNonQuery();
            }
        }

        private void BtnOpenFolder_Click(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(BackupRoot);
                Process.Start("explorer.exe", BackupRoot);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo abrir la carpeta: " + ex.Message, "RestoOS", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnRefreshData_Click(object sender, EventArgs e) => LoadData();
    }
}
