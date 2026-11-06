using System;
using System.Drawing;
using System.Windows.Forms;
using app_escritorio.Data;

namespace app_escritorio.Forms
{
    /// <summary>
    /// Elección de perfil. Ambos perfiles piden PIN (usuarios de la tabla Usuario):
    /// "Administrador" solo acepta usuarios con ese rol; "Empleados" acepta cualquier usuario activo.
    /// </summary>
    public partial class LoginForm : Form
    {
        public string Role { get; private set; } = "Empleado";

        public LoginForm()
        {
            InitializeComponent();
        }

        private void BtnAdmin_Click(object sender, EventArgs e) => Login("Administrador");

        private void BtnEmpleado_Click(object sender, EventArgs e) => Login(null);

        private void Login(string rolRequerido)
        {
            using (var pin = new AdminCodeForm(rolRequerido))
            {
                if (pin.ShowDialog(this) != DialogResult.OK) return;
                Sesion.Iniciar(pin.UsuarioId, pin.UsuarioNombre, pin.UsuarioRol);
            }
            Role = Sesion.EsAdministrador ? "Admin" : "Empleado";
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
