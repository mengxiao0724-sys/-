using PcbPinInspection.ViewModels;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using 引脚检测.Services;

namespace 引脚检测
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly HalconUiService _halconUi = new HalconUiService();
        public readonly MainViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();

            _vm = new MainViewModel(_halconUi);
            DataContext = _vm;

            // HSmartWindowControlWPF 在构造时还没创建内部 HWindow，
            // 必须等它 Loaded 之后再 Attach。
            HalconWindow.Loaded += OnHalconWindowLoaded;
        }

        private void OnHalconWindowLoaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate ()
            {
                try
                {
                    _halconUi.Attach(HalconWindow);
                    _vm.Log("HALCON 窗口已就绪");
                }
                catch (Exception ex)
                {
                    _vm.Log("HALCON 窗口 attach 失败：" + ex.Message);
                }
            }), DispatcherPriority.Loaded);
        }
    }
}