using Windows.UI.Xaml.Controls;

namespace TouchChanX.UWP
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a <see cref="Frame">.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            //Process.Start("notepad");
            var dir = Directory.GetDirectories("c:/");
        }
    }
}
