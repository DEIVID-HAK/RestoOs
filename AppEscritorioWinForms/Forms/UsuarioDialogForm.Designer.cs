namespace app_escritorio.Forms
{
    partial class UsuarioDialogForm
    {
        /// <summary>Variable del diseñador requerida.</summary>
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        #region Código generado por el Diseñador

        private void InitializeComponent()
        {
            this.lblTitle = new app_escritorio.UI.RLabel();
            this.lblNombre = new app_escritorio.UI.RLabel();
            this.txtNombre = new app_escritorio.UI.RTextBox();
            this.lblRol = new app_escritorio.UI.RLabel();
            this.cmbRol = new app_escritorio.UI.RComboBox();
            this.lblPin = new app_escritorio.UI.RLabel();
            this.txtPin = new app_escritorio.UI.RTextBox();
            this.lblPinHint = new app_escritorio.UI.RLabel();
            this.btnCancel = new app_escritorio.UI.RButton();
            this.btnSave = new app_escritorio.UI.RButton();
            this.SuspendLayout();
            // 
            // lblTitle
            // 
            this.lblTitle.Location = new System.Drawing.Point(24, 18);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(400, 32);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "Nuevo usuario";
            this.lblTitle.TextStyle = app_escritorio.UI.TextStyle.Heading;
            // 
            // lblNombre
            // 
            this.lblNombre.Location = new System.Drawing.Point(24, 60);
            this.lblNombre.Name = "lblNombre";
            this.lblNombre.Size = new System.Drawing.Size(400, 18);
            this.lblNombre.TabIndex = 1;
            this.lblNombre.Text = "Nombre del usuario *";
            this.lblNombre.TextStyle = app_escritorio.UI.TextStyle.Muted;
            // 
            // txtNombre
            // 
            this.txtNombre.Location = new System.Drawing.Point(24, 82);
            this.txtNombre.MaxLength = 80;
            this.txtNombre.Name = "txtNombre";
            this.txtNombre.PlaceholderText = "Ej.: María Gómez";
            this.txtNombre.Size = new System.Drawing.Size(400, 40);
            this.txtNombre.TabIndex = 2;
            // 
            // lblRol
            // 
            this.lblRol.Location = new System.Drawing.Point(24, 136);
            this.lblRol.Name = "lblRol";
            this.lblRol.Size = new System.Drawing.Size(400, 18);
            this.lblRol.TabIndex = 3;
            this.lblRol.Text = "Rol *";
            this.lblRol.TextStyle = app_escritorio.UI.TextStyle.Muted;
            // 
            // cmbRol
            // 
            this.cmbRol.Location = new System.Drawing.Point(24, 158);
            this.cmbRol.Name = "cmbRol";
            this.cmbRol.Size = new System.Drawing.Size(400, 32);
            this.cmbRol.TabIndex = 4;
            // 
            // lblPin
            // 
            this.lblPin.Location = new System.Drawing.Point(24, 204);
            this.lblPin.Name = "lblPin";
            this.lblPin.Size = new System.Drawing.Size(400, 18);
            this.lblPin.TabIndex = 5;
            this.lblPin.Text = "PIN (4 a 8 dígitos) *";
            this.lblPin.TextStyle = app_escritorio.UI.TextStyle.Muted;
            // 
            // txtPin
            // 
            this.txtPin.Location = new System.Drawing.Point(24, 226);
            this.txtPin.MaxLength = 8;
            this.txtPin.Name = "txtPin";
            this.txtPin.PasswordChar = '●';
            this.txtPin.PlaceholderText = "••••";
            this.txtPin.Size = new System.Drawing.Size(400, 40);
            this.txtPin.TabIndex = 6;
            // 
            // lblPinHint
            // 
            this.lblPinHint.Location = new System.Drawing.Point(24, 272);
            this.lblPinHint.Name = "lblPinHint";
            this.lblPinHint.Size = new System.Drawing.Size(400, 34);
            this.lblPinHint.TabIndex = 7;
            this.lblPinHint.Text = "El PIN se guarda cifrado y sirve para entrar al sistema.";
            this.lblPinHint.TextStyle = app_escritorio.UI.TextStyle.Caption;
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(24, 330);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(190, 40);
            this.btnCancel.TabIndex = 8;
            this.btnCancel.Text = "Cancelar";
            this.btnCancel.Variant = app_escritorio.UI.ButtonVariant.Secondary;
            // 
            // btnSave
            // 
            this.btnSave.Location = new System.Drawing.Point(234, 330);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(190, 40);
            this.btnSave.TabIndex = 9;
            this.btnSave.Text = "Guardar";
            this.btnSave.Variant = app_escritorio.UI.ButtonVariant.Primary;
            this.btnSave.Click += new System.EventHandler(this.BtnSave_Click);
            // 
            // UsuarioDialogForm
            // 
            this.AcceptButton = this.btnSave;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(448, 396);
            this.Controls.Add(this.btnSave);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.lblPinHint);
            this.Controls.Add(this.txtPin);
            this.Controls.Add(this.lblPin);
            this.Controls.Add(this.cmbRol);
            this.Controls.Add(this.lblRol);
            this.Controls.Add(this.txtNombre);
            this.Controls.Add(this.lblNombre);
            this.Controls.Add(this.lblTitle);
            this.Name = "UsuarioDialogForm";
            this.Text = "Usuario";
            this.ResumeLayout(false);
        }

        #endregion

        private app_escritorio.UI.RLabel lblTitle;
        private app_escritorio.UI.RLabel lblNombre;
        private app_escritorio.UI.RTextBox txtNombre;
        private app_escritorio.UI.RLabel lblRol;
        private app_escritorio.UI.RComboBox cmbRol;
        private app_escritorio.UI.RLabel lblPin;
        private app_escritorio.UI.RTextBox txtPin;
        private app_escritorio.UI.RLabel lblPinHint;
        private app_escritorio.UI.RButton btnCancel;
        private app_escritorio.UI.RButton btnSave;
    }
}
