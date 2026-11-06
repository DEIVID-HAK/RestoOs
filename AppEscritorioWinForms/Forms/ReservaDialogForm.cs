using System;
using System.Globalization;
using System.Windows.Forms;
using app_escritorio.Data;
using app_escritorio.Models;
using app_escritorio.UI;

namespace app_escritorio.Forms
{
    /// <summary>Crear o editar una reserva.</summary>
    public partial class ReservaDialogForm : RDialogForm
    {
        public Reserva Result { get; private set; }

        public ReservaDialogForm() : this(null, DateTime.Today) { }

        private readonly int _id;

        public ReservaDialogForm(Reserva r, DateTime defaultDate)
        {
            InitializeComponent();
            _id = r?.Id ?? 0;
            if (!UiHelpers.IsDesignTime)
            {
                // Mesas reales del salón (antes era una lista fija).
                var mesas = MesaStore.Names();
                if (mesas.Count > 0)
                {
                    cmbMesa.Items.Clear();
                    cmbMesa.Items.Add(ReservaStore.SinMesa);
                    foreach (var m in mesas) cmbMesa.Items.Add(m);
                }
            }
            if (r == null)
            {
                Text = lblTitle.Text = "Nueva reserva";
                txtFecha.Text = defaultDate.ToString("dd/MM/yyyy");
                txtHora.Text = "19:00";
                cmbPersonas.Text = "2";
                cmbMesa.Text = cmbMesa.Items.Count > 1 ? cmbMesa.Items[1].ToString() : ReservaStore.SinMesa;
                cmbEstado.SelectedIndex = 0;
                return;
            }
            Text = lblTitle.Text = "Editar reserva";
            txtCliente.Text = r.Cliente;
            txtFecha.Text = r.Fecha.ToString("dd/MM/yyyy");
            txtHora.Text = r.Fecha.ToString("HH:mm");
            cmbPersonas.Text = r.Personas.ToString();
            cmbMesa.Text = r.Mesa;
            txtTel.Text = r.Telefono;
            int e = cmbEstado.Items.IndexOf(r.Estado);
            cmbEstado.SelectedIndex = e >= 0 ? e : 0;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            txtCliente.Focus();
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            string cliente = txtCliente.Text.Trim();
            if (cliente.Length == 0)
            {
                MessageBox.Show("Cliente requerido.", "RestoOS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtCliente.Focus();
                return;
            }
            if (!DateTime.TryParseExact(txtFecha.Text.Trim(), new[] { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fecha))
            {
                MessageBox.Show("Fecha inválida. Usa el formato dd/mm/aaaa.", "RestoOS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtFecha.Focus();
                return;
            }
            fecha = TimeSpan.TryParse(txtHora.Text.Trim(), out TimeSpan hora) ? fecha.Add(hora) : fecha.AddHours(19);
            int personas = int.TryParse(cmbPersonas.Text.Trim(), out int p) && p > 0 ? p : 2;
            string mesa = string.IsNullOrWhiteSpace(cmbMesa.Text) ? ReservaStore.SinMesa : cmbMesa.Text.Trim();
            string estado = cmbEstado.SelectedItem?.ToString() ?? "Confirmada";

            Result = new Reserva(fecha, cliente, personas, mesa, estado, txtTel.Text.Trim()) { Id = _id };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
