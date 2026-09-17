using System;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using System.Windows;

namespace AuthWin
{
    public sealed partial class VaultWorkWindow : Window
    {
        private readonly Func<object> work;
        private object result;
        private Exception error;
        private bool finished;

        private VaultWorkWindow(string message, Func<object> work)
        {
            InitializeComponent();
            this.work = work;
            txtStatus.Text = message;
        }

        internal static T Run<T>(string message, Func<T> work)
        {
            var window = new VaultWorkWindow(message, () => work());
            window.ShowDialog();
            if (window.error != null) ExceptionDispatchInfo.Capture(window.error).Throw();
            return (T)window.result;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                result = await Task.Run(work);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finished = true;
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!finished) e.Cancel = true;
            base.OnClosing(e);
        }
    }
}
