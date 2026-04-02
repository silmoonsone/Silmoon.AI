namespace Silmoon.AI.WinFormTest
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            ctlOpenAITestButton = new Button();
            SuspendLayout();
            // 
            // ctlOpenAITestButton
            // 
            ctlOpenAITestButton.Location = new Point(100, 66);
            ctlOpenAITestButton.Name = "ctlOpenAITestButton";
            ctlOpenAITestButton.Size = new Size(75, 23);
            ctlOpenAITestButton.TabIndex = 0;
            ctlOpenAITestButton.Text = "OpenAI Test Form";
            ctlOpenAITestButton.UseVisualStyleBackColor = true;
            ctlOpenAITestButton.Click += ctlOpenAITestButton_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(ctlOpenAITestButton);
            Name = "Form1";
            Text = "Form1";
            ResumeLayout(false);
        }

        #endregion

        private Button ctlOpenAITestButton;
    }
}
