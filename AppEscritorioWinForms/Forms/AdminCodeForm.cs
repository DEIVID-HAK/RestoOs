using System;
using System.Drawing;
using System.Windows.Forms;
using app_escritorio.Data;

namespace app_escritorio.Forms
{
    /// <summary>
    /// Teclado de PIN. Valida contra la tabla Usuario con sp_ValidarPin (el PIN se guarda cifrado).
    /// Con un rol requerido (p. ej. "Administrador") solo acepta usuarios de ese rol; lo usan el login
    /// y la autorización de anulaciones en Reportes.
    /// </summary>
    public partial class AdminCodeForm : Form
    {
        private const int PinLength = 4;
        private readonly string _rolRequerido;
        private string _code = string.Empty;

        public int UsuarioId { get; private set; }
        public string UsuarioNombre { get; private set; }
        public string UsuarioRol { get; private set; }

        public AdminCodeForm() : this("Administrador") { }

        public AdminCodeForm(string rolRequerido, string titulo = null)
        {
            InitializeComponent();
            _rolRequerido = rolRequerido;
            titleLabel.Text = titulo ?? (rolRequerido == null ? "Ingreso de empleados" : "Acceso " + rolRequerido);
            demoLabel.Text = "Escribe tu PIN de " + PinLength + " dígitos";
            UpdateDisplay();
        }

        private void NumBtn_Click(object sender, EventArgs e)
        {
            var text = ((Button)sender).Text;
            if (text == "C") _code = "";
            else if (text == "⌫") { if (_code.Length > 0) _code = _code.Substring(0, _code.Length - 1); }
            else if (_code.Length < PinLength) _code += text;

            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            string display = "";
            for (int i = 0; i < PinLength; i++)
            {
                display += (i < _code.Length ? "●" : "_") + " ";
            }
            lblCodeDisplay.Text = display.Trim();
            lblError.Visible = false;
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            if (_code.Length < PinLength)
            {
                ShowError("El PIN tiene " + PinLength + " dígitos.");
                return;
            }

            (int Id, string Nombre, string Rol)? user;
            try
            {
                user = Sesion.ValidarPin(_code, _rolRequerido);
            }
            catch (Exception ex)
            {
                Db.ShowError("validar el PIN", ex);
                return;
            }

            if (user == null)
            {
                _code = string.Empty;
                UpdateDisplay();
                ShowError(_rolRequerido == null ? "PIN incorrecto." : "PIN incorrecto o sin rol " + _rolRequerido + ".");
                return;
            }

            UsuarioId = user.Value.Id;
            UsuarioNombre = user.Value.Nombre;
            UsuarioRol = user.Value.Rol;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void ShowError(string text)
        {
            lblError.Text = text;
            lblError.Visible = true;
        }

        /// <summary>Teclado físico: números, Retroceso, Supr (= C) y Enter (= Ingresar).</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            string key = null;
            if (keyData >= Keys.D0 && keyData <= Keys.D9) key = ((int)(keyData - Keys.D0)).ToString();
            else if (keyData >= Keys.NumPad0 && keyData <= Keys.NumPad9) key = ((int)(keyData - Keys.NumPad0)).ToString();
            else if (keyData == Keys.Back) key = "⌫";
            else if (keyData == Keys.Delete) key = "C";
            else if (keyData == Keys.Enter) { BtnOk_Click(this, EventArgs.Empty); return true; }

            if (key == null) return base.ProcessCmdKey(ref msg, keyData);
            if (key == "C") _code = "";
            else if (key == "⌫") { if (_code.Length > 0) _code = _code.Substring(0, _code.Length - 1); }
            else if (_code.Length < PinLength) _code += key;
            UpdateDisplay();
            return true;
        }
    }
}
