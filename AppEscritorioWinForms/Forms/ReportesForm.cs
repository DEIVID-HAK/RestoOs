using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using app_escritorio.Data;
using app_escritorio.Models;
using app_escritorio.UI;
using app_escritorio.Utils;
using app_escritorio.Views.Pos;

namespace app_escritorio.Forms
{
    /// <summary>
    /// Reportes + Trazabilidad (migración de ReportesView.xaml de WPF). Solo Admin.
    /// Lee las ventas reales de SQL Server (VentaStore): KPIs, gráfico, top platos, ventas por mesa e historial.
    /// Desde el detalle de una venta se puede anular con PIN de administrador y motivo (sp_AnularVenta).
    /// </summary>
    public partial class ReportesForm : Form
    {
        private static readonly CultureInfo Co = CultureInfo.GetCultureInfo("es-CO");
        private List<Venta> _ventas = new List<Venta>();
        private bool _ready;

        public ReportesForm()
        {
            InitializeComponent();
            if (UiHelpers.IsDesignTime) return;

            cmbPeriodo.SelectedIndex = 1;
            cmbOrigen.SelectedIndex = 0;
            cmbMetodo.SelectedIndex = 0;
            cmbEstado.SelectedIndex = 0;
            _ready = true;
            Reload();
        }

        private static string Money(decimal v) => v.ToString("C0", Co);

        // ===================== Datos =====================

        /// <summary>Relee las ventas de la base (el período más largo es 30 días o el mes en curso).</summary>
        public void Reload()
        {
            if (!_ready || IsDisposed || Disposing) return;
            var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var from = monthStart < DateTime.Today.AddDays(-29) ? monthStart : DateTime.Today.AddDays(-29);
            try
            {
                _ventas = VentaStore.Load(from);
            }
            catch (Exception ex)
            {
                Db.ShowError("cargar las ventas", ex);
                _ventas = new List<Venta>();
            }
            RefreshData();
        }

        private DateTime PeriodStart()
        {
            switch (cmbPeriodo.SelectedIndex)
            {
                case 0: return DateTime.Today;
                case 2: return DateTime.Today.AddDays(-29);
                case 3: return new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                default: return DateTime.Today.AddDays(-6);
            }
        }

        private IEnumerable<Venta> InPeriod()
        {
            var from = PeriodStart();
            string origen = cmbOrigen.SelectedIndex == 1 ? "Salón" : cmbOrigen.SelectedIndex == 2 ? "Delivery" : null;
            return _ventas.Where(v => v.Fecha >= from && (origen == null || v.Origen == origen));
        }

        // ===================== Pintado =====================

        private void RefreshData()
        {
            var cobradas = InPeriod().Where(v => v.Estado == "Cobrado").ToList();
            decimal total = cobradas.Sum(v => v.Total);
            int tickets = cobradas.Count;

            lblTotal.Text = Money(total);
            lblTotalSub.Text = tickets + " tickets · " + cmbPeriodo.Text.ToLower(Co);
            lblTicket.Text = Money(tickets == 0 ? 0 : Math.Round(total / tickets));
            lblTax.Text = Money(Math.Round(cobradas.Sum(v => v.TaxAmount())));
            lblTaxSub.Text = "Impuesto cobrado en cada venta";
            lblMetodo.Text = Money(cobradas.Sum(v => v.Efectivo)) + " / " + Money(cobradas.Sum(v => v.Tarjeta));

            // Gráfico: siempre los últimos 7 días
            var values = new decimal[7];
            var labels = new string[7];
            for (int i = 0; i < 7; i++)
            {
                var day = DateTime.Today.AddDays(i - 6);
                values[i] = _ventas.Where(v => v.Estado == "Cobrado" && v.Fecha.Date == day).Sum(v => v.Total);
                labels[i] = Co.TextInfo.ToTitleCase(day.ToString("ddd", Co).TrimEnd('.'));
            }
            chart.SetData(values, labels);

            gridTop.Rows.Clear();
            int rank = 1;
            foreach (var g in cobradas.SelectMany(v => v.Items).GroupBy(x => x.Plato)
                                      .Select(g => new { Plato = g.Key, Cant = g.Sum(x => x.Cantidad), Total = g.Sum(x => x.Subtotal) })
                                      .OrderByDescending(x => x.Total).Take(6))
                gridTop.Rows.Add(rank++, g.Plato, g.Cant, Money(g.Total));

            gridMesa.Rows.Clear();
            foreach (var g in cobradas.GroupBy(v => v.Mesa).OrderByDescending(g => g.Sum(v => v.Total)))
                gridMesa.Rows.Add(g.Key, g.Count(), Money(g.Sum(v => v.Total)),
                                  g.GroupBy(v => v.Cajero).OrderByDescending(c => c.Count()).First().Key);

            RefreshHistorial();
        }

        private void RefreshHistorial()
        {
            string text = txtHistSearch.Text.Trim().ToLowerInvariant();
            string metodo = cmbMetodo.SelectedItem?.ToString() ?? "Todos";
            string estado = cmbEstado.SelectedItem?.ToString() ?? "Todos";

            var rows = InPeriod().Where(v => (metodo == "Todos" || (metodo == "Efectivo" ? v.Efectivo > 0 : v.Tarjeta > 0))
                                             && (estado == "Todos" || v.Estado == estado)
                                             && (text.Length == 0 || (v.Mesa ?? "").ToLowerInvariant().Contains(text)
                                                 || (v.Detalle ?? "").ToLowerInvariant().Contains(text) || (v.Cajero ?? "").ToLowerInvariant().Contains(text)))
                                 .OrderByDescending(v => v.Fecha).ToList();

            gridHist.Rows.Clear();
            foreach (var v in rows)
            {
                int i = gridHist.Rows.Add(v.Fecha.ToString("dd/MM HH:mm"), v.Mesa, v.Detalle, v.TotalText, v.Metodo, v.Cajero, v.Estado);
                gridHist.Rows[i].Tag = v;
                gridHist.Rows[i].Cells[colHEstado.Index].Style.ForeColor = v.EstadoColor;
                gridHist.Rows[i].Cells[colHTotal.Index].Style.ForeColor = Theme.Primary;
            }
            decimal cobrado = rows.Where(v => v.Estado == "Cobrado").Sum(v => v.Total);
            lblRango.Text = cmbPeriodo.Text + ": " + Money(cobrado) + " · " + rows.Count + " ventas";
        }

        private void Filter_Changed(object sender, EventArgs e)
        {
            if (_ready) RefreshData();
        }

        private void Hist_Changed(object sender, EventArgs e)
        {
            if (_ready) RefreshHistorial();
        }

        private void BtnRefresh_Click(object sender, EventArgs e) => Reload();

        private void GridHist_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != colHVer.Index || !(gridHist.Rows[e.RowIndex].Tag is Venta v)) return;

            var sb = new StringBuilder();
            sb.AppendLine("RESTOOS — TRAZABILIDAD POS");
            sb.AppendLine("Venta N.º " + v.Id + " · " + v.Fecha.ToString("dd/MM/yyyy HH:mm"));
            sb.AppendLine("Mesa: " + v.Mesa + " — Cajero: " + v.Cajero);
            sb.AppendLine("----------------------------");
            foreach (var it in v.Items) sb.AppendLine("x" + it.Cantidad + " " + it.Plato + "   " + Money(it.Subtotal));
            sb.AppendLine("----------------------------");
            sb.AppendLine("Subtotal: " + Money(v.Subtotal));
            sb.AppendLine("Impuesto (" + v.TaxPct + "%): " + Money(v.Impuesto));
            sb.AppendLine("Total: " + v.TotalText);
            sb.AppendLine("Método: " + v.Metodo + (v.Metodo == "Mixto" ? " (efectivo " + Money(v.Efectivo) + ", tarjeta " + Money(v.Tarjeta) + ")" : ""));
            sb.AppendLine("Estado: " + v.Estado);

            if (v.Estado != "Cobrado")
            {
                MessageBox.Show(sb.ToString(), "Detalle de venta", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            sb.AppendLine();
            sb.AppendLine("¿Quieres ANULAR esta venta? (requiere PIN de administrador y motivo)");
            if (MessageBox.Show(sb.ToString(), "Detalle de venta", MessageBoxButtons.YesNo, MessageBoxIcon.Information,
                                MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                AnularVenta(v);
        }

        /// <summary>Anulación (requisitos §8): PIN de un administrador distinto al cajero, motivo obligatorio y devolución de insumos.</summary>
        private void AnularVenta(Venta v)
        {
            int supervisorId;
            using (var pin = new AdminCodeForm("Administrador", "Autorizar anulación"))
            {
                if (pin.ShowDialog(TopLevelControl ?? this) != DialogResult.OK) return;
                supervisorId = pin.UsuarioId;
            }

            string motivo;
            using (var note = new NoteDialog("Motivo de la anulación", string.Empty))
            {
                if (note.ShowDialog(TopLevelControl ?? this) != DialogResult.OK) return;
                motivo = note.NoteResult;
            }
            if (string.IsNullOrWhiteSpace(motivo))
            {
                MessageBox.Show("El motivo de la anulación es obligatorio.", "RestoOS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                VentaStore.Anular(v.Id, supervisorId, motivo);
            }
            catch (Exception ex)
            {
                Db.ShowError("anular la venta", ex);
                return;
            }
            MessageBox.Show("Venta N.º " + v.Id + " anulada. Los insumos volvieron al inventario.", "RestoOS", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Reload();
        }
    }
}
