using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using app_escritorio.Data;
using app_escritorio.Models;
using app_escritorio.Utils;

namespace app_escritorio.Forms
{
    /// <summary>Historial de ventas de los últimos 30 días (desde Menú digital). Lee la tabla Venta con VentaStore.</summary>
    public partial class OrderHistoryForm : Form
    {
        private static readonly System.Globalization.CultureInfo Co = System.Globalization.CultureInfo.GetCultureInfo("es-CO");
        private List<Venta> records;

        public OrderHistoryForm() : this(null) { }

        /// <param name="historyFile">Ya no se usa (antes era data/orders.xml); se conserva la firma.</param>
        public OrderHistoryForm(string historyFile)
        {
            InitializeComponent();
            LoadRecords();
        }

        private void LoadRecords()
        {
            try
            {
                records = VentaStore.Load(DateTime.Today.AddDays(-29)).OrderByDescending(r => r.Fecha).ToList();
            }
            catch (Exception ex)
            {
                Db.ShowError("cargar el historial", ex);
                records = new List<Venta>();
            }
            var cobradas = records.Where(r => r.Estado == "Cobrado").ToList();
            summary.Text = string.Format(Co, "Ventas: {0}    Total cobrado: {1:C0}    Hoy: {2}",
                cobradas.Count, cobradas.Sum(r => r.Total), cobradas.Count(r => r.Fecha.Date == DateTime.Today));
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var record in records)
            {
                var row = new ListViewItem(record.Fecha.ToString("dd/MM/yyyy HH:mm"));
                row.SubItems.Add(record.Cajero ?? "");
                row.SubItems.Add(record.Mesa ?? "");
                row.SubItems.Add(record.Estado ?? "");
                row.SubItems.Add(record.Total.ToString("C0", Co));
                row.SubItems.Add(record.Detalle ?? "");
                list.Items.Add(row);
            }
            list.EndUpdate();
        }

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            LoadRecords();
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void Footer_Resize(object sender, EventArgs e)
        {
            this.close.Left = this.footer.ClientSize.Width - 125;
            this.refresh.Left = this.footer.ClientSize.Width - 245;
        }
    }
}
