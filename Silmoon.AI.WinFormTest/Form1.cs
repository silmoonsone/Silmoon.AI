namespace Silmoon.AI.WinFormTest
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void ctlOpenAITestButton_Click(object sender, EventArgs e)
        {
            var form = new OpenAIClientTestForm();
            form.FormClosed += (s, args) => Close();
            Hide();
            form.Show();
        }
    }
}
